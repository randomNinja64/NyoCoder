using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;

namespace NyoCoder
{
    /// <summary>
    /// Base class for options pages that host a WinForms UserControl.
    /// Handles the lazy-creation lifecycle, config reload on open, and save/reload on close.
    /// Subclasses implement <see cref="CreateHost"/>, <see cref="UpdateHostFromConfig"/>,
    /// and <see cref="SaveHostToConfig"/> to define page-specific UI binding.
    /// </summary>
    public abstract class ConfigDialogPage<THost> : DialogPage
        where THost : Control
    {
        private THost _host;

        protected THost Host { get { return _host; } }

        protected override IWin32Window Window
        {
            get
            {
                if (_host == null)
                {
                    _host = CreateHost();
                    UpdateHostFromConfig();
                }
                return _host;
            }
        }

        public override void LoadSettingsFromStorage()
        {
            base.LoadSettingsFromStorage();
            ConfigHandler.ReloadConfig();
            UpdateHostFromConfig();
        }

        public override void SaveSettingsToStorage()
        {
            base.SaveSettingsToStorage();
            if (_host != null)
                SaveHostToConfig();
            ConfigHandler.SaveConfig();
            ConfigHandler.ReloadConfig();
        }

        /// <summary>Creates and returns the WinForms host control for this page.</summary>
        protected abstract THost CreateHost();

        /// <summary>Pushes current config values into the host control.</summary>
        protected abstract void UpdateHostFromConfig();

        /// <summary>Reads values from the host control and writes them to ConfigHandler.</summary>
        protected abstract void SaveHostToConfig();
    }
}
