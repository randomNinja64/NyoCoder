using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("3E4F5A6B-7C8D-9E0F-A1B2-C3D4E5F60718")]
    public class ToolsOptionsPage : ConfigDialogPage<ToolsOptionsPageHost>
    {
        protected override ToolsOptionsPageHost CreateHost()
        {
            return new ToolsOptionsPageHost(this);
        }

        protected override void UpdateHostFromConfig()
        {
            if (Host == null) return;
            Host.SetDisabledTools(ConfigHandler.GetDisabledTools());
            // Seed with manifest defaults, then overlay saved config values
            var toolOpts = new Dictionary<string, string>(ExternalToolRegistry.GetOptionDefaults(), StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in ConfigHandler.GetAllValues())
                toolOpts[kvp.Key] = kvp.Value;
            Host.SetToolOptions(toolOpts);
        }

        protected override void SaveHostToConfig()
        {
            ConfigHandler.SetDisabledTools(Host.GetDisabledTools());
            foreach (var kvp in Host.GetToolOptions())
                ConfigHandler.SetConfigValue(kvp.Key, kvp.Value);
        }
    }
}
