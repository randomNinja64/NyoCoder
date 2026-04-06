using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("B2C3D4E5-F6A7-4B8C-9D0E-1F2A3B4C5D6E")]
    public class WebSearchOptionsPage : DialogPage
    {
        private WebSearchOptionsPageHost host;

        protected override IWin32Window Window
        {
            get
            {
                if (host == null)
                {
                    host = new WebSearchOptionsPageHost(this);
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
                ConfigHandler.SetConfigValue("searxngInstance", host.SearXNGInstance);
                ConfigHandler.SetConfigValue("webUserAgent", host.UserAgent);

                int maxSearchResults = host.MaxSearchResults;
                ConfigHandler.SetConfigValue("maxSearchResults", maxSearchResults > 0 ? maxSearchResults.ToString() : null);

                int maxWebContentLength = host.MaxWebContentLength;
                ConfigHandler.SetConfigValue("maxWebContentLength", maxWebContentLength > 0 ? maxWebContentLength.ToString() : null);
            }
            ConfigHandler.SaveConfig();
            ConfigHandler.ReloadConfig();
        }

        private void UpdateHostFromConfig()
        {
            if (host != null)
            {
                host.SearXNGInstance = ConfigHandler.GetConfigValue("searxngInstance", "");
                host.UserAgent = ConfigHandler.GetConfigValue("webUserAgent", WebSearchTool.DefaultUserAgent);
                host.MaxSearchResults = ConfigHandler.GetConfigInt("maxSearchResults", 20);
                host.MaxWebContentLength = ConfigHandler.GetConfigInt("maxWebContentLength", 8000);
            }
        }
    }
}
