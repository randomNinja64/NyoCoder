using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Shell;

namespace NyoCoder
{
    /// <summary>
    /// Add-in entry point for VS 2005/2008. Creates the commands and menu items the VSIX builds
    /// declare in .vsct, then sites the shared package logic.
    /// </summary>
    [ComVisible(true)]
    [Guid("6E3B0E0C-4F6B-4B0B-9C59-6F1B8E6C1A52")]
    public sealed class Connect : IDTExtensibility2, IDTCommandTarget
    {
        internal const string CommandPrefix = "NyoCoder.Connect.";

        private DTE2 _dte;
        private AddIn _addIn;
        private NyoCoder_VSIXPackage _package;
        private readonly Dictionary<string, uint> _commandIds = new Dictionary<string, uint>();
        private readonly List<CommandBarControl> _controls = new List<CommandBarControl>();

        public void OnConnection(object application, int connectMode, object addInInst, ref Array custom)
        {
            _dte = (DTE2)application;
            _addIn = (AddIn)addInInst;
            // Loaded from the Add-in Manager after startup; otherwise wait for OnStartupComplete,
            // when the command bars exist.
            if (connectMode == 0 /* ext_cm_AfterStartup */)
                Initialize();
        }

        public void OnStartupComplete(ref Array custom)
        {
            Initialize();
        }

        public void OnDisconnection(int removeMode, ref Array custom)
        {
            foreach (CommandBarControl control in _controls)
            {
                try { control.Delete(true); } catch { }
            }
            _controls.Clear();
        }

        public void OnAddInsUpdate(ref Array custom) { }
        public void OnBeginShutdown(ref Array custom) { }

        private void Initialize()
        {
            if (_package != null)
                return;

            // The shared code marshals to the UI thread through Application.Current, which
            // only exists once something creates a WPF Application (VS 2005/2008 are not WPF).
            if (System.Windows.Application.Current == null)
                new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

            AddCommand("nyoCoderOptionsCmd", PkgCmdIDList.nyoCoderOptionsCmd, "NyoCoder Options...", "Tools");
            AddCommand("nyoCoderTool", PkgCmdIDList.nyoCoderTool, "NyoCoder", "View");
            AddCommand("nyoCoderAskCmd", PkgCmdIDList.nyoCoderAskCmd, "Ask NyoCoder", "Code Window");

            _package = new NyoCoder_VSIXPackage();
            _package.SetSite(_dte, _addIn);
            DiffHighlightMarkers.Attach();
        }

        /// <summary>
        /// Creates the named command on first run (commands persist across sessions) and adds a
        /// menu item for it, which is removed again in <see cref="OnDisconnection"/>.
        /// </summary>
        private void AddCommand(string name, uint id, string text, string commandBarName)
        {
            try
            {
                _commandIds[CommandPrefix + name] = id;

                Command command;
                try
                {
                    command = _dte.Commands.Item(CommandPrefix + name, -1);
                }
                catch
                {
                    object[] contextGuids = new object[0];
                    command = ((Commands2)_dte.Commands).AddNamedCommand2(
                        _addIn, name, text, text, true, 59, ref contextGuids,
                        (int)(vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusEnabled),
                        (int)vsCommandStyle.vsCommandStyleText,
                        vsCommandControlType.vsCommandControlTypeButton);
                }

                CommandBars commandBars = (CommandBars)_dte.CommandBars;
                CommandBar commandBar;
                try { commandBar = ((CommandBarPopup)commandBars["MenuBar"].Controls[commandBarName]).CommandBar; }
                catch { commandBar = commandBars[commandBarName]; }

                _controls.Add((CommandBarControl)command.AddControl(commandBar, commandBar.Controls.Count + 1));
            }
            catch
            {
                // A missing menu item must not stop the add-in from loading.
            }
        }

        public void QueryStatus(string cmdName, vsCommandStatusTextWanted neededText, ref vsCommandStatus statusOption, ref object commandText)
        {
            if (neededText == vsCommandStatusTextWanted.vsCommandStatusTextWantedNone && _commandIds.ContainsKey(cmdName))
                statusOption = vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusEnabled;
        }

        public void Exec(string cmdName, vsCommandExecOption executeOption, ref object variantIn, ref object variantOut, ref bool handled)
        {
            handled = false;
            uint id;
            if (executeOption != vsCommandExecOption.vsCommandExecOptionDoDefault || !_commandIds.TryGetValue(cmdName, out id))
                return;

            OleMenuCommandService commands = Package.GetGlobalService(typeof(IMenuCommandService)) as OleMenuCommandService;
            handled = commands != null && commands.Invoke(new CommandID(GuidList.guidNyoCoder_VSIXCmdSet, (int)id));
        }
    }

    /// <summary>
    /// Same COM interface as Extensibility.IDTExtensibility2 (matched by IID), declared here so
    /// the build does not depend on Extensibility.dll being installed.
    /// </summary>
    [ComImport, ComVisible(true)]
    [Guid("B65AD801-ABAF-11D0-BB8B-00A0C90F2744")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IDTExtensibility2
    {
        [DispId(1)]
        void OnConnection(
            [In, MarshalAs(UnmanagedType.IDispatch)] object application,
            [In] int connectMode,
            [In, MarshalAs(UnmanagedType.IDispatch)] object addInInst,
            [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(2)]
        void OnDisconnection(
            [In] int removeMode,
            [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(3)]
        void OnAddInsUpdate([In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(4)]
        void OnStartupComplete([In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(5)]
        void OnBeginShutdown([In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);
    }
}
