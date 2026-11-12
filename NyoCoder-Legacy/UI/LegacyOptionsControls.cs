using System.Runtime.InteropServices;
using System.Windows.Forms;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace NyoCoder
{
    /// <summary>
    /// Hosts one of the shared options pages as an add-in Tools > Options page (registered in
    /// NyoCoder-Legacy.AddIn, which needs one concrete class per page).
    /// </summary>
    public abstract class LegacyOptionsControl<TPage> : UserControl, IDTToolsOptionsPage
        where TPage : DialogPage, new()
    {
        private readonly TPage _page = new TPage();

        protected LegacyOptionsControl()
        {
            // VS shows add-in option pages at the control's own size (it does not resize
            // them); this is the page area of the VS 2005/2008 Options dialog.
            Size = new System.Drawing.Size(395, 290);
        }

        public void OnAfterCreated(DTE dte)
        {
            Control host = _page.Control;
            // The shared pages ask for at least 420px, wider than this dialog's page area.
            host.MinimumSize = System.Drawing.Size.Empty;
            host.Dock = DockStyle.Fill;
            Controls.Add(host);
            _page.LoadSettingsFromStorage();
        }

        public void GetProperties(ref object propertiesObject) { propertiesObject = null; }
        public void OnOK() { _page.SaveSettingsToStorage(); }
        public void OnCancel() { }
        public void OnHelp() { }
    }

    [ComVisible(true)] public class GeneralOptionsControl : LegacyOptionsControl<OptionsPage> { }
    [ComVisible(true)] public class ContextOptionsControl : LegacyOptionsControl<ContextOptionsPage> { }
    [ComVisible(true)] public class AppearanceOptionsControl : LegacyOptionsControl<AppearanceOptionsPage> { }
    [ComVisible(true)] public class ModesOptionsControl : LegacyOptionsControl<ModesOptionsPage> { }
    [ComVisible(true)] public class BuildErrorHandlingOptionsControl : LegacyOptionsControl<BuildErrorHandlingOptionsPage> { }
    [ComVisible(true)] public class IndexingOptionsControl : LegacyOptionsControl<IndexingOptionsPage> { }
    [ComVisible(true)] public class ToolsOptionsControl : LegacyOptionsControl<ToolsOptionsPage> { }
    [ComVisible(true)] public class ApprovalsOptionsControl : LegacyOptionsControl<ApprovalsOptionsPage> { }
    [ComVisible(true)] public class WebSearchOptionsControl : LegacyOptionsControl<WebSearchOptionsPage> { }
}
