using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("3E4F5A6B-7C8D-9E0F-A1B2-C3D4E5F60718")]
    public class ToolsOptionsPage : DialogPage
    {
        private ToolsOptionsPageHost host;

        protected override IWin32Window Window
        {
            get
            {
                if (host == null)
                {
                    host = new ToolsOptionsPageHost(this);
                    UpdateHostFromConfig();
                }
                return host;
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
            if (host != null)
            {
                ConfigHandler.SetDisabledTools(host.GetDisabledTools());
                foreach (var kvp in host.GetToolOptions())
                    ConfigHandler.SetConfigValue(kvp.Key, kvp.Value);
            }
            ConfigHandler.SaveConfig();
            ConfigHandler.ReloadConfig();
        }

        private void UpdateHostFromConfig()
        {
            if (host != null)
            {
                host.SetDisabledTools(ConfigHandler.GetDisabledTools());
                // Seed with manifest defaults, then overlay saved config values
                var toolOpts = new Dictionary<string, string>(ExternalToolRegistry.GetOptionDefaults(), StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in ConfigHandler.GetAllValues())
                    toolOpts[kvp.Key] = kvp.Value;
                host.SetToolOptions(toolOpts);
            }
        }
    }
}
