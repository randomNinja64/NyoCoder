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
        private OptionsPageHost host;

        public OptionsPage()
        {
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

	public override void LoadSettingsFromStorage()
        {
            base.LoadSettingsFromStorage();
            // Reload config from file (in case it was changed externally)
            ConfigHandler.ReloadConfig();
            UpdateHostFromConfig();
        }

        public override void SaveSettingsToStorage()
        {
            base.SaveSettingsToStorage();
		// Get values from host if available (it's the source of truth for UI),
		// otherwise use properties (which read from ConfigHandler)
		if (host != null)
		{
			ConfigHandler.SetApiKey(host.ApiKey);
			ConfigHandler.SetLlmServer(host.LlmServer);
			ConfigHandler.SetModel(host.Model);
			ConfigHandler.SetMaxReadLines(host.MaxReadLines);
			ConfigHandler.SetContextWindowSize(host.ContextWindowSize);
		}
		// Properties already update ConfigHandler when set, so we just need to save
		ConfigHandler.SaveConfig();
		// Reload to ensure all cached values are updated
		ConfigHandler.ReloadConfig();
        }

	private void UpdateHostFromConfig()
	{
		if (host != null)
		{
			host.ApiKey = ApiKey ?? string.Empty;
			host.LlmServer = LlmServer ?? string.Empty;
			host.Model = Model ?? string.Empty;
			host.MaxReadLines = MaxReadLines;
			host.ContextWindowSize = ContextWindowSize;
		}
	}
    }
}
