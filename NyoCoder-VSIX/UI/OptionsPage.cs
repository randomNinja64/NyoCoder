using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;

namespace NyoCoder
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
                    UpdateHostFromConfig();
                }
                return host;
            }
        }

        public string ApiKey
        {
            get { return configHandler.GetApiKey(); }
            set { configHandler.SetApiKey(value); }
        }

        public string LlmServer
        {
            get { return configHandler.GetLlmServer(); }
            set { configHandler.SetLlmServer(value); }
        }

	public string Model
	{
		get { return configHandler.GetModel(); }
		set { configHandler.SetModel(value); }
	}

	public int MaxContentLength
	{
		get { return ConfigHandler.MaxContentLength; }
		set { configHandler.SetMaxContentLength(value); }
	}

	public override void LoadSettingsFromStorage()
        {
            base.LoadSettingsFromStorage();
            // Reload config from file (in case it was changed externally)
            configHandler = new ConfigHandler();
            UpdateHostFromConfig();
        }

        public override void SaveSettingsToStorage()
        {
            base.SaveSettingsToStorage();
		// Get values from host if available (it's the source of truth for UI),
		// otherwise use properties (which read from ConfigHandler)
		if (host != null)
		{
			configHandler.SetApiKey(host.ApiKey);
			configHandler.SetLlmServer(host.LlmServer);
			configHandler.SetModel(host.Model);
			configHandler.SetMaxContentLength(host.MaxContentLength);
		}
		// Properties already update ConfigHandler when set, so we just need to save
		configHandler.SaveConfig();
        }

	private void UpdateHostFromConfig()
	{
		if (host != null)
		{
			host.ApiKey = ApiKey ?? string.Empty;
			host.LlmServer = LlmServer ?? string.Empty;
			host.Model = Model ?? string.Empty;
			host.MaxContentLength = MaxContentLength;
		}
	}
    }
}
