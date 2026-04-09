using System;
using System.Runtime.InteropServices;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("8A5B5E5C-4F3D-4E8A-9B2C-1D3E4F5A6B7C")]
    public class OptionsPage : ConfigDialogPage<OptionsPageHost>
    {
        public string ApiKey
        {
            get { return ConfigHandler.GetApiKey(); }
            set { ConfigHandler.SetApiKey(value); }
        }

        public string LlmServer
        {
            get { return ConfigHandler.GetLlmServer(); }
            set { ConfigHandler.SetLlmServer(value); }
        }

        public string Model
        {
            get { return ConfigHandler.GetModel(); }
            set { ConfigHandler.SetModel(value); }
        }

        public int MaxReadLines
        {
            get { return ConfigHandler.MaxReadLines; }
            set { ConfigHandler.SetMaxReadLines(value); }
        }

        public int? ContextWindowSize
        {
            get { return ConfigHandler.ContextWindowSize; }
            set { ConfigHandler.SetContextWindowSize(value); }
        }

        protected override OptionsPageHost CreateHost()
        {
            return new OptionsPageHost(this);
        }

        protected override void UpdateHostFromConfig()
        {
            if (Host == null) return;
            Host.ApiKey = ApiKey ?? string.Empty;
            Host.LlmServer = LlmServer ?? string.Empty;
            Host.Model = Model ?? string.Empty;
            Host.MaxReadLines = MaxReadLines;
            Host.ContextWindowSize = ContextWindowSize;
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
