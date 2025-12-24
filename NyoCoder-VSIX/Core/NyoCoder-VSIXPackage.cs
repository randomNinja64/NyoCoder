using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.ComponentModel.Design;
using System.Threading;
using Microsoft.Win32;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using EnvDTE;
using EnvDTE80;

namespace NyoCoder
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the 
    /// IVsPackage interface and uses the registration attributes defined in the framework to 
    /// register itself and its components with the shell.
    /// </summary>
    // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
    // a package.
    [PackageRegistration(UseManagedResourcesOnly = true)]
    // This attribute is used to register the informations needed to show the this package
    // in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    // This attribute is needed to let the shell know that this package exposes some menus.
    [ProvideMenuResource("Menus.ctmenu", 1)]
    // This attribute registers a tool window exposed by this package.
    [ProvideToolWindow(typeof(NyoCoderToolWindow))]
    // This attribute registers an options page exposed by this package.
    [ProvideOptionPage(typeof(OptionsPage), "NyoCoder", "General", 0, 0, true)]
    [Guid(GuidList.guidNyoCoder_VSIXPkgString)]
    public sealed class NyoCoder_VSIXPackage : Package
    {
        private static NyoCoder_VSIXPackage _instance;
        internal int _isAiRunning = 0; // 0 = not running, 1 = running
        public LLMClient LlmClient;

        /// <summary>
        /// Gets the singleton instance of the package.
        /// </summary>
        public static NyoCoder_VSIXPackage Instance
        {
            get { return _instance; }
        }

        /// <summary>
        /// Gets the DTE2 application object for Visual Studio integration.
        /// </summary>
        public DTE2 ApplicationObject
        {
            get { return GetService(typeof(DTE)) as DTE2; }
        }

        /// <summary>
        /// Gets the NyoCoderControl from the tool window if it exists.
        /// </summary>
        public NyoCoderControl ToolWindowControl
        {
            get
            {
                NyoCoderToolWindow toolWindow = FindToolWindow(typeof(NyoCoderToolWindow), 0, false) as NyoCoderToolWindow;
                return toolWindow != null ? toolWindow.Control : null;
            }
        }

        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require 
        /// any Visual Studio service because at this point the package object is created but 
        /// not sited yet inside Visual Studio environment. The place to do all the other 
        /// initialization is the Initialize method.
        /// </summary>
        public NyoCoder_VSIXPackage()
        {
            Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", this.ToString()));
            _instance = this;
        }

        /// <summary>
        /// This function is called when the user clicks the menu item that shows the 
        /// tool window. See the Initialize method to see how the menu item is associated to 
        /// this function using the OleMenuCommandService service and the MenuCommand class.
        /// </summary>
        private void ShowToolWindow(object sender, EventArgs e)
        {
            // Get the instance number 0 of this tool window. This window is single instance so this instance
            // is actually the only one.
            // The last flag is set to true so that if the tool window does not exists it will be created.
            ToolWindowPane window = this.FindToolWindow(typeof(NyoCoderToolWindow), 0, true);
            if ((null == window) || (null == window.Frame))
            {
                throw new NotSupportedException(Resources.CanNotCreateWindow);
            }
            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }


        /////////////////////////////////////////////////////////////////////////////
        // Overriden Package Implementation
        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initilaization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            Trace.WriteLine (string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", this.ToString()));
            base.Initialize();

            // Initialize ConfigHandler to load config on extension startup
            ConfigHandler.Initialize();

            // Add our command handlers for menu (commands must exist in the .vsct file)
            OleMenuCommandService mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if ( null != mcs )
            {
                // Create the command for the menu item.
                CommandID menuCommandID = new CommandID(GuidList.guidNyoCoder_VSIXCmdSet, (int)PkgCmdIDList.nyoCoderOptionsCmd);
                MenuCommand menuItem = new MenuCommand(MenuItemCallback, menuCommandID );
                mcs.AddCommand( menuItem );
                // Create the command for the tool window
                CommandID toolwndCommandID = new CommandID(GuidList.guidNyoCoder_VSIXCmdSet, (int)PkgCmdIDList.nyoCoderTool);
                MenuCommand menuToolWin = new MenuCommand(ShowToolWindow, toolwndCommandID);
                mcs.AddCommand( menuToolWin );
                // Create the command for the context menu item
                CommandID askCommandID = new CommandID(GuidList.guidNyoCoder_VSIXCmdSet, (int)PkgCmdIDList.nyoCoderAskCmd);
                MenuCommand askMenuItem = new MenuCommand(AskNyoCoderCallback, askCommandID);
                mcs.AddCommand( askMenuItem );
            }

            // Set up keyboard binding for Ask NyoCoder command (VS2010 compatible)
            SetupKeyboardBinding();
        }

        /// <summary>
        /// Sets up the keyboard binding for the Ask NyoCoder command.
        /// </summary>
        private void SetupKeyboardBinding()
        {
            try
            {
                DTE2 dte = ApplicationObject;
                if (dte == null || dte.Commands == null) return;

                // The command name format for VSIX packages is: {guid}:{id} (numeric id)
                // Or we can search by looking for our command in the list
                string targetGuid = "{" + GuidList.guidNyoCoder_VSIXCmdSetString + "}";
                int targetId = (int)PkgCmdIDList.nyoCoderAskCmd;

                Command cmd = null;
                try
                {
                    // Try to get command directly by GUID and ID
                    cmd = dte.Commands.Item(targetGuid, targetId);
                }
                catch
                {
                    // If that fails, search through all commands
                    try
                    {
                        foreach (Command c in dte.Commands)
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(c.Name) && 
                                    (c.Name.Contains("nyoCoderAskCmd") || c.Name.Contains("Ask NyoCoder")))
                                {
                                    cmd = c;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // Set the keyboard binding if command found
                if (cmd != null)
                {
                    try
                    {
                        cmd.Bindings = new object[] { "Text Editor::Ctrl+Alt+N" };
                    }
                    catch { }
                }
            }
            catch { }
        }
        #endregion

        /// <summary>
        /// This function is the callback used to execute a command when the a menu item is clicked.
        /// See the Initialize method to see how the menu item is associated to this function using
        /// the OleMenuCommandService service and the MenuCommand class.
        /// </summary>
        private void MenuItemCallback(object sender, EventArgs e)
        {
            // Open the options page
            ShowOptionPage(typeof(OptionsPage));
        }

        /// <summary>
        /// Saves all open documents in Visual Studio.
        /// </summary>
        public void SaveAllOpenFiles()
        {
            try
            {
                DTE2 dte = ApplicationObject;
                if (dte != null && dte.Documents != null)
                {
                    dte.Documents.SaveAll();
                }
            }
            catch
            {
                // If we can't save files, continue without it
                // This prevents the feature from breaking if there's an issue with DTE
            }
        }



        /// <summary>
        /// This function is the callback used when the "Ask NyoCoder" context menu item is clicked.
        /// Clears the LLM client/context and shows the input box for user input.
        /// </summary>
        private void AskNyoCoderCallback(object sender, EventArgs e)
        {
            // Create context engine
            DTE2 dte = ApplicationObject;
            ContextEngine contextEngine = new ContextEngine(dte);

            // Only proceed if in a text editor
            if (!contextEngine.IsInTextEditor()) return;

            // Check if an AI request is already running
            if (Interlocked.CompareExchange(ref _isAiRunning, 1, 0) != 0)
            {
                System.Windows.Forms.MessageBox.Show(
                    "An AI request is already in progress. Please wait for it to complete.",
                    "NyoCoder", 
                    System.Windows.Forms.MessageBoxButtons.OK, 
                    System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            // Ensure tool window is created and visible
            ShowToolWindow(null, EventArgs.Empty);

            // Get the tool window control
            NyoCoderControl toolWindowControl = ToolWindowControl;
            if (toolWindowControl == null)
            {
                Interlocked.Exchange(ref _isAiRunning, 0); // Reset flag
                System.Windows.Forms.MessageBox.Show(
                    "Failed to access NyoCoder output window. Please try again.",
                    "NyoCoder", 
                    System.Windows.Forms.MessageBoxButtons.OK, 
                    System.Windows.Forms.MessageBoxIcon.Error);
                return;
            }

            // Validate configuration and create LLM client
            LLMClient newClient = LLMClient.CreateFromConfig();
            if (newClient == null)
            {
                Interlocked.Exchange(ref _isAiRunning, 0); // Reset flag
                return;
            }

            LlmClient = newClient;

            // Clear previous output
            toolWindowControl.ClearOutput();

            // Show input bar and focus it
            toolWindowControl.ShowInputBar();

            // Reset the AI running flag (no request is running yet, user will type and send)
            Interlocked.Exchange(ref _isAiRunning, 0);
        }

    }
}
