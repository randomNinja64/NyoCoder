using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;

namespace NyoCoder.NyoCoder_VSIX
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("8A5B5E5C-4F3D-4E8A-9B2C-1D3E4F5A6B7C")]
    public class OptionsPage : DialogPage
    {
        private ConfigHandler configHandler;
        private OptionsPageHost host;

        public OptionsPage()
        {
            configHandler = new ConfigHandler();
        }

        protected override IWin32Window Window
        {
            get
            {
                if (host == null)
                {
                    host = new OptionsPageHost(this);
                    // Load settings into the host when it's first created
                    LoadSettings();
                }
                return host;
            }
        }

        public string ApiKey { get; set; }
        public string LlmServer { get; set; }
        public string Model { get; set; }

        public override void LoadSettingsFromStorage()
        {
            base.LoadSettingsFromStorage();
            // Load settings from INI file into properties
            ApiKey = configHandler.GetApiKey();
            LlmServer = configHandler.GetLlmServer();
            Model = configHandler.GetModel();
            
            // Update host if it's already created (UI is already shown)
            if (host != null)
            {
                host.ApiKey = ApiKey ?? string.Empty;
                host.LlmServer = LlmServer ?? string.Empty;
                host.Model = Model ?? string.Empty;
            }
        }

        public override void SaveSettingsToStorage()
        {
            base.SaveSettingsToStorage();
            SaveSettings();
        }

        private void LoadSettings()
        {
            // Load settings from INI file
            ApiKey = configHandler.GetApiKey();
            LlmServer = configHandler.GetLlmServer();
            Model = configHandler.GetModel();

            // Update host with loaded values
            if (host != null)
            {
                host.ApiKey = ApiKey ?? string.Empty;
                host.LlmServer = LlmServer ?? string.Empty;
                host.Model = Model ?? string.Empty;
            }
        }

        private void SaveSettings()
        {
            // Get values from control if available, otherwise use properties
            if (host != null)
            {
                ApiKey = host.ApiKey;
                LlmServer = host.LlmServer;
                Model = host.Model;
            }

            configHandler.SetApiKey(ApiKey);
            configHandler.SetLlmServer(LlmServer);
            configHandler.SetModel(Model);
            configHandler.SaveConfig();
        }
    }
}
