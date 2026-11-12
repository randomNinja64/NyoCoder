// Stand-ins for the few Managed Package Framework types the shared NyoCoder sources use.
// VS 2005/2008 cannot load an unsigned-by-PLK VSPackage, so NyoCoder-Legacy is an add-in;
// these types keep the shared files compiling unchanged and route to add-in equivalents.
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell.Interop;
using IOleServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace Microsoft.VisualStudio
{
    public static class VSConstants
    {
        public const int S_OK = 0;
        public const int E_NOTIMPL = unchecked((int)0x80004001);
        public static readonly Guid LOGVIEWID_TextView = new Guid("7651A703-06E5-11D1-8EBD-00A0C90F26EA");
    }

    public static class ErrorHandler
    {
        public static bool Succeeded(int hr) { return hr >= 0; }

        public static int ThrowOnFailure(int hr)
        {
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
            return hr;
        }
    }
}

namespace Microsoft.VisualStudio.Shell
{
    /// <summary>VS 2005/2008 are not themed: the VS brush keys map to system colors.</summary>
    public static class VsBrushes
    {
        public static object WindowKey { get { return SystemColors.WindowBrushKey; } }
        public static object WindowTextKey { get { return SystemColors.WindowTextBrushKey; } }
        public static object ToolWindowBackgroundKey { get { return SystemColors.ControlBrushKey; } }
        public static object ToolWindowTextKey { get { return SystemColors.ControlTextBrushKey; } }
        public static object CommandBarGradientBeginKey { get { return SystemColors.ControlBrushKey; } }
        public static object GrayTextKey { get { return SystemColors.GrayTextBrushKey; } }
    }

    /// <summary>Holds the package's command handlers; the add-in's Exec invokes them.</summary>
    public sealed class OleMenuCommandService
    {
        private readonly Dictionary<CommandID, MenuCommand> _commands = new Dictionary<CommandID, MenuCommand>();

        public void AddCommand(MenuCommand command)
        {
            _commands[command.CommandID] = command;
        }

        internal bool Invoke(CommandID id)
        {
            MenuCommand command;
            if (!_commands.TryGetValue(id, out command))
                return false;
            command.Invoke();
            return true;
        }
    }

    public class DialogPage : System.ComponentModel.Component
    {
        protected virtual IWin32Window Window { get { return null; } }
        public virtual void LoadSettingsFromStorage() { }
        public virtual void SaveSettingsToStorage() { }
        internal Control Control { get { return (Control)Window; } }
    }

    public class ToolWindowPane
    {
        public ToolWindowPane(IServiceProvider provider) { }
        public string Caption { get; set; }
        public int BitmapResourceID { get; set; }
        public int BitmapIndex { get; set; }
        public object Content { get; set; }
        public object Frame { get; internal set; }
    }

    /// <summary>WinForms control VS instantiates for the tool window; hosts the WPF content.</summary>
    [ComVisible(true)]
    public class ToolWindowHost : UserControl
    {
        internal void SetContent(UIElement content)
        {
            // WPF 3.5 does not fall back to another font for symbols the UI font lacks (the
            // plan status icons and step header rules), so name fallback fonts explicitly.
            // The existing font stays first, so only characters it lacks use the others.
            const string fallbacks = ", Lucida Sans Unicode, Arial Unicode MS, MS UI Gothic, MS Gothic, SimSun";

            // Tooltips live in their own popup and take their font from a style, not the window.
            System.Windows.Style toolTipStyle = new System.Windows.Style(typeof(System.Windows.Controls.ToolTip));
            toolTipStyle.Setters.Add(new Setter(
                System.Windows.Controls.Control.FontFamilyProperty,
                new System.Windows.Media.FontFamily(SystemFonts.MessageFontFamily.Source + fallbacks)));
            ((FrameworkElement)content).Resources.Add(typeof(System.Windows.Controls.ToolTip), toolTipStyle);

            ElementHost host = new ElementHost();
            host.Dock = DockStyle.Fill;
            host.Child = content;
            Controls.Add(host);

            // After Child is assigned: ElementHost has by now copied its WinForms font onto the child.
            System.Windows.Media.FontFamily current =
                (System.Windows.Media.FontFamily)content.GetValue(System.Windows.Documents.TextElement.FontFamilyProperty);
            content.SetValue(
                System.Windows.Documents.TextElement.FontFamilyProperty,
                new System.Windows.Media.FontFamily(current.Source + fallbacks));
        }
    }

    public class Package
    {
        private static DTE2 _dte;
        private static AddIn _addIn;
        private static readonly OleMenuCommandService _menuCommands = new OleMenuCommandService();
        private readonly Dictionary<Type, ToolWindowPane> _toolWindows = new Dictionary<Type, ToolWindowPane>();

        internal void SetSite(DTE2 dte, AddIn addIn)
        {
            _dte = dte;
            _addIn = addIn;
            Initialize();
        }

        protected virtual void Initialize() { }

        protected object GetService(Type serviceType)
        {
            return GetGlobalService(serviceType);
        }

        public static object GetGlobalService(Type serviceType)
        {
            if (serviceType == typeof(DTE))
                return _dte;
            if (serviceType == typeof(IMenuCommandService))
                return _menuCommands;

            IOleServiceProvider provider = _dte as IOleServiceProvider;
            if (provider == null)
                return null;

            Guid service = serviceType.GUID;
            Guid iid = new Guid("00000000-0000-0000-C000-000000000046"); // IUnknown
            IntPtr unknown;
            if (provider.QueryService(ref service, ref iid, out unknown) < 0 || unknown == IntPtr.Zero)
                return null;
            try { return Marshal.GetObjectForIUnknown(unknown); }
            finally { Marshal.Release(unknown); }
        }

        public ToolWindowPane FindToolWindow(Type toolWindowType, int id, bool create)
        {
            ToolWindowPane pane;
            if (_toolWindows.TryGetValue(toolWindowType, out pane) || !create)
                return pane;

            pane = (ToolWindowPane)Activator.CreateInstance(toolWindowType);
            object host = null;
            EnvDTE.Window window = ((Windows2)_dte.Windows).CreateToolWindow2(
                _addIn,
                typeof(ToolWindowHost).Assembly.Location,
                typeof(ToolWindowHost).FullName,
                pane.Caption,
                toolWindowType.GUID.ToString("B"),
                ref host);
            ((ToolWindowHost)host).SetContent((UIElement)pane.Content);

            // A new add-in tool window starts floating at a tiny default size. Leave it alone
            // once the user has docked it or given it a usable size (VS remembers both).
            try
            {
                window.Visible = true;
                if (window.IsFloating && (window.Width < 300 || window.Height < 300))
                {
                    window.Width = 420;
                    window.Height = 600;
                }
            }
            catch { }

            pane.Frame = new WindowFrame(window);
            _toolWindows[toolWindowType] = pane;
            return pane;
        }

        /// <summary>
        /// Tools > Options cannot be opened at an add-in's page, so this shows the same pages
        /// in a dialog of their own, with the requested page selected.
        /// </summary>
        public void ShowOptionPage(Type optionsPageType)
        {
            using (Form form = new Form())
            {
                form.Text = "NyoCoder Options";
                form.ClientSize = new System.Drawing.Size(520, 460);
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;

                TabControl tabs = new TabControl();
                tabs.Dock = DockStyle.Fill;
                tabs.Multiline = true;

                List<DialogPage> pages = new List<DialogPage>();
                List<TabPage> ordered = new List<TabPage>();
                foreach (Type type in GetType().Assembly.GetTypes())
                {
                    if (type.IsAbstract || !type.IsSubclassOf(typeof(DialogPage)))
                        continue;

                    DialogPage page = (DialogPage)Activator.CreateInstance(type);
                    page.LoadSettingsFromStorage();
                    pages.Add(page);

                    // "BuildErrorHandlingOptionsPage" -> "Build Error Handling"; "OptionsPage" -> "General"
                    string title = System.Text.RegularExpressions.Regex.Replace(
                        type.Name.Replace("OptionsPage", string.Empty), "(?<=[a-z])(?=[A-Z])", " ");
                    TabPage tab = new TabPage(title.Length == 0 ? "General" : title);
                    Control content = page.Control;
                    content.Dock = DockStyle.Fill;
                    tab.Controls.Add(content);
                    // The requested page (General, from the menu item) goes first. Ordered in a
                    // list because TabPages.Insert drops the page before the handle exists.
                    ordered.Insert(type == optionsPageType ? 0 : ordered.Count, tab);
                }
                tabs.TabPages.AddRange(ordered.ToArray());

                Button ok = new Button();
                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                Button cancel = new Button();
                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Bottom;
                buttons.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
                buttons.AutoSize = true;
                buttons.Padding = new Padding(6);
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);

                form.Controls.Add(tabs);
                form.Controls.Add(buttons);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                if (form.ShowDialog(new OwnerWindow((IntPtr)_dte.MainWindow.HWnd)) != DialogResult.OK)
                    return;
                foreach (DialogPage page in pages)
                    page.SaveSettingsToStorage();
            }
        }

        private sealed class OwnerWindow : IWin32Window
        {
            private readonly IntPtr _handle;
            public OwnerWindow(IntPtr handle) { _handle = handle; }
            public IntPtr Handle { get { return _handle; } }
        }

        /// <summary>Just enough IVsWindowFrame for the shared code to show the tool window.</summary>
        private sealed class WindowFrame : IVsWindowFrame
        {
            private readonly EnvDTE.Window _window;

            public WindowFrame(EnvDTE.Window window) { _window = window; }

            public int Show() { _window.Visible = true; _window.Activate(); return VSConstants.S_OK; }
            public int ShowNoActivate() { _window.Visible = true; return VSConstants.S_OK; }
            public int Hide() { _window.Visible = false; return VSConstants.S_OK; }
            public int IsVisible() { return _window.Visible ? 0 : 1; }
            public int IsOnScreen(out int pfOnScreen) { pfOnScreen = _window.Visible ? 1 : 0; return VSConstants.S_OK; }
            public int CloseFrame(uint grfSaveOptions) { _window.Close(vsSaveChanges.vsSaveChangesNo); return VSConstants.S_OK; }
            public int SetFramePos(VSSETFRAMEPOS dwSFP, ref Guid rguidRelativeTo, int x, int y, int cx, int cy) { return VSConstants.E_NOTIMPL; }
            public int GetFramePos(VSSETFRAMEPOS[] pdwSFP, out Guid pguidRelativeTo, out int px, out int py, out int pcx, out int pcy) { pguidRelativeTo = Guid.Empty; px = py = pcx = pcy = 0; return VSConstants.E_NOTIMPL; }
            public int GetProperty(int propid, out object pvar) { pvar = null; return VSConstants.E_NOTIMPL; }
            public int SetProperty(int propid, object var) { return VSConstants.E_NOTIMPL; }
            public int GetGuidProperty(int propid, out Guid pguid) { pguid = Guid.Empty; return VSConstants.E_NOTIMPL; }
            public int SetGuidProperty(int propid, ref Guid rguid) { return VSConstants.E_NOTIMPL; }
            public int QueryViewInterface(ref Guid riid, out IntPtr ppv) { ppv = IntPtr.Zero; return VSConstants.E_NOTIMPL; }
        }
    }
}
