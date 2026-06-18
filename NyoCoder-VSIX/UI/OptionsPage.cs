using System.Runtime.InteropServices;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("8A5B5E5C-4F3D-4E8A-9B2C-1D3E4F5A6B7C")]
    public class OptionsPage : ConfigDialogPage<OptionsPageHost>
    {
        protected override OptionsPageHost CreateHost()
        {
            return new OptionsPageHost();
        }

        protected override void UpdateHostFromConfig()
        {
            if (Host == null) return;
            Host.ApiKey = ConfigHandler.GetApiKey();
            Host.LlmServer = ConfigHandler.GetLlmServer();
            Host.Model = ConfigHandler.GetModel();
            Host.MaxReadLines = ConfigHandler.MaxReadLines;
            Host.ContextWindowSize = ConfigHandler.ContextWindowSize;
        }

        protected override void SaveHostToConfig()
        {
            ConfigHandler.SetApiKey(Host.ApiKey);
            ConfigHandler.SetLlmServer(Host.LlmServer);
            ConfigHandler.SetModel(Host.Model);
            ConfigHandler.SetMaxReadLines(Host.MaxReadLines);
            ConfigHandler.SetContextWindowSize(Host.ContextWindowSize);
        }
    }
}
