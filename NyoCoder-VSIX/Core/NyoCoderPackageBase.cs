using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using EnvDTE80;

namespace NyoCoder
{
    /// <summary>
    /// Shared package implementation. Legacy and modern VSIX projects each register a thin
    /// <see cref="NyoCoder_VSIXPackage"/> subclass; modern overrides indexing triggers.
    /// </summary>
    public class NyoCoderPackageBase : Package
    {
        protected static NyoCoderPackageBase _instance;
        internal int _isAiRunning = 0; // 0 = not running, 1 = running
        public LLMClient LlmClient;

        // DTE event objects for indexing triggers (legacy default). Held in fields so they are
        // not garbage-collected (which would silently stop the events from firing).
        private SolutionEvents _solutionEvents;
        private DocumentEvents _documentEvents;
        private ProjectItemsEvents _projectItemsEvents;
        private ProjectItemsEvents _solutionItemsEvents;

        /// <summary>
        /// Gets the singleton instance of the package.
        /// </summary>
        public static NyoCoderPackageBase Instance
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

        protected NyoCoderPackageBase()
        {
            Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", this.ToString()));
            _instance = this;
        }

        private void ShowToolWindow(object sender, EventArgs e)
        {
            ToolWindowPane window = this.FindToolWindow(typeof(NyoCoderToolWindow), 0, true);
            if ((null == window) || (null == window.Frame))
            {
                throw new NotSupportedException(Resources.CanNotCreateWindow);
            }
            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }

        #region Package Members

        protected override void Initialize()
        {
            Trace.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", this.ToString()));
            base.Initialize();

            ConfigHandler.Initialize();

            if (!System.IO.Directory.Exists(ExternalToolRegistry.ToolsDirectory))
                System.IO.Directory.CreateDirectory(ExternalToolRegistry.ToolsDirectory);

            OleMenuCommandService mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (null != mcs)
            {
                CommandID menuCommandID = new CommandID(GuidList.guidNyoCoder_VSIXCmdSet, (int)PkgCmdIDList.nyoCoderOptionsCmd);
                MenuCommand menuItem = new MenuCommand(MenuItemCallback, menuCommandID);
                mcs.AddCommand(menuItem);

                CommandID toolwndCommandID = new CommandID(GuidList.guidNyoCoder_VSIXCmdSet, (int)PkgCmdIDList.nyoCoderTool);
                MenuCommand menuToolWin = new MenuCommand(ShowToolWindow, toolwndCommandID);
                mcs.AddCommand(menuToolWin);

                CommandID askCommandID = new CommandID(GuidList.guidNyoCoder_VSIXCmdSet, (int)PkgCmdIDList.nyoCoderAskCmd);
                MenuCommand askMenuItem = new MenuCommand(AskNyoCoderCallback, askCommandID);
                mcs.AddCommand(askMenuItem);
            }

            SetupKeyboardBinding();
            SetupIndexingTriggers();
        }

        /// <summary>
        /// Subscribes to events that keep the codebase index fresh. Default uses EnvDTE;
        /// the VS2017+ package overrides with IVs* APIs (EnvDTE sinks break on VS2022).
        /// </summary>
        protected virtual void SetupIndexingTriggers()
        {
            try
            {
                DTE2 dte = ApplicationObject;
                if (dte == null || dte.Events == null)
                    return;

                _solutionEvents = dte.Events.SolutionEvents;
                if (_solutionEvents != null)
                {
                    _solutionEvents.Opened += OnSolutionOpened;
                    _solutionEvents.AfterClosing += OnSolutionClosed;
                }

                _documentEvents = dte.Events.get_DocumentEvents(null);
                if (_documentEvents != null)
                    _documentEvents.DocumentSaved += OnDocumentSaved;

                Events2 events2 = dte.Events as Events2;
                if (events2 != null)
                {
                    _projectItemsEvents = events2.ProjectItemsEvents;
                    if (_projectItemsEvents != null)
                    {
                        _projectItemsEvents.ItemRemoved += OnProjectItemRemoved;
                        _projectItemsEvents.ItemRenamed += OnProjectItemRenamed;
                    }

                    _solutionItemsEvents = events2.SolutionItemsEvents;
                    if (_solutionItemsEvents != null)
                    {
                        _solutionItemsEvents.ItemRemoved += OnSolutionItemRemoved;
                        _solutionItemsEvents.ItemRenamed += OnSolutionItemRenamed;
                    }
                }

                try
                {
                    if (dte.Solution != null && dte.Solution.IsOpen)
                        OnSolutionOpened();
                }
                catch { }
            }
            catch
            {
                // Indexing triggers are best-effort; never block package init.
            }
        }

        protected void OnSolutionOpened()
        {
            try
            {
                try
                {
                    DTE2 dte = ApplicationObject;
                    string solutionPath = dte != null && dte.Solution != null ? dte.Solution.FullName : null;
                    if (!string.IsNullOrEmpty(solutionPath))
                    {
                        string solutionDir = System.IO.Path.GetDirectoryName(solutionPath);
                        if (!string.IsNullOrEmpty(solutionDir) && System.IO.Directory.Exists(solutionDir))
                            Environment.CurrentDirectory = solutionDir;
                    }
                }
                catch { }

                CodebaseIndex.Invalidate();
                CodebaseIndex.PublishStatus();
                if (ConfigHandler.GetIndexOnSolutionOpen())
                    CodebaseIndexer.RequestReconcile();
            }
            catch { }
        }

        protected void OnSolutionClosed()
        {
            try
            {
                CodebaseIndex.Invalidate();
                CodebaseIndex.PublishStatus();
            }
            catch { }
        }

        private void OnDocumentSaved(Document document)
        {
            try
            {
                if (document == null || !ConfigHandler.GetIndexOnSave())
                    return;
                string path = document.FullName;
                if (!string.IsNullOrEmpty(path))
                    CodebaseIndexer.RequestIndexFile(path);
            }
            catch { }
        }

        private void OnProjectItemRemoved(ProjectItem item)
        {
            RemoveProjectItem(item);
        }

        private void OnSolutionItemRemoved(ProjectItem item)
        {
            RemoveProjectItem(item);
        }

        private void RemoveProjectItem(ProjectItem item)
        {
            try
            {
                string path = GetProjectItemPath(item);
                if (!string.IsNullOrEmpty(path))
                    CodebaseIndexer.RequestRemoveFile(path);
            }
            catch { }
        }

        private void OnProjectItemRenamed(ProjectItem item, string oldName)
        {
            RenameProjectItem(item, oldName);
        }

        private void OnSolutionItemRenamed(ProjectItem item, string oldName)
        {
            RenameProjectItem(item, oldName);
        }

        private void RenameProjectItem(ProjectItem item, string oldName)
        {
            try
            {
                string newPath = GetProjectItemPath(item);
                if (string.IsNullOrEmpty(newPath))
                    return;
                string oldPath = null;
                if (!string.IsNullOrEmpty(oldName))
                {
                    try { oldPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(newPath), oldName); }
                    catch { oldPath = null; }
                }
                CodebaseIndexer.RequestRenameFile(oldPath, newPath);
            }
            catch { }
        }

        private static string GetProjectItemPath(ProjectItem item)
        {
            try
            {
                if (item != null && item.FileCount >= 1)
                    return item.get_FileNames(1);
            }
            catch { }
            return null;
        }

        private void SetupKeyboardBinding()
        {
            try
            {
                DTE2 dte = ApplicationObject;
                if (dte == null || dte.Commands == null) return;

                string targetGuid = "{" + GuidList.guidNyoCoder_VSIXCmdSetString + "}";
                int targetId = (int)PkgCmdIDList.nyoCoderAskCmd;

                Command cmd = null;
                try
                {
                    cmd = dte.Commands.Item(targetGuid, targetId);
                }
                catch
                {
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

                if (cmd != null)
                {
                    try
                    {
                        cmd.Bindings = new object[] { "Global::Ctrl+Alt+N" };
                    }
                    catch { }
                }
            }
            catch { }
        }
        #endregion

        private void MenuItemCallback(object sender, EventArgs e)
        {
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
            }
        }

        private void AskNyoCoderCallback(object sender, EventArgs e)
        {
            if (Interlocked.CompareExchange(ref _isAiRunning, 1, 0) != 0)
            {
                System.Windows.Forms.MessageBox.Show(
                    "An AI request is already in progress. Please wait for it to complete.",
                    "NyoCoder",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            ShowToolWindow(null, EventArgs.Empty);

            NyoCoderControl toolWindowControl = ToolWindowControl;
            if (toolWindowControl == null)
            {
                Interlocked.Exchange(ref _isAiRunning, 0);
                System.Windows.Forms.MessageBox.Show(
                    "Failed to access NyoCoder output window. Please try again.",
                    "NyoCoder",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return;
            }

            LLMClient newClient = LLMClient.CreateFromConfig();
            if (newClient == null)
            {
                Interlocked.Exchange(ref _isAiRunning, 0);
                return;
            }

            LlmClient = newClient;
            toolWindowControl.ClearOutput();
            toolWindowControl.ShowInputBar();
            Interlocked.Exchange(ref _isAiRunning, 0);
        }
    }
}
