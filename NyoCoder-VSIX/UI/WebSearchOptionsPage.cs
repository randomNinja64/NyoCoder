using System.Runtime.InteropServices;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("B2C3D4E5-F6A7-4B8C-9D0E-1F2A3B4C5D6E")]
    public class WebSearchOptionsPage : ConfigDialogPage<WebSearchOptionsPageHost>
    {
        protected override WebSearchOptionsPageHost CreateHost()
        {
            return new WebSearchOptionsPageHost(this);
        }

        protected override void UpdateHostFromConfig()
        {
            if (Host == null) return;
            Host.SearXNGInstance = ConfigHandler.GetConfigValue("searxngInstance", "");
            Host.UserAgent = ConfigHandler.GetConfigValue("webUserAgent", WebSearchTool.DefaultUserAgent);
            Host.MaxSearchResults = ConfigHandler.GetConfigInt("maxSearchResults", 20);
            Host.MaxWebContentLength = ConfigHandler.GetConfigInt("maxWebContentLength", 8000);
        }

        protected override void SaveHostToConfig()
        {
            ConfigHandler.SetConfigValue("searxngInstance", Host.SearXNGInstance);
            ConfigHandler.SetConfigValue("webUserAgent", Host.UserAgent);

            int maxSearchResults = Host.MaxSearchResults;
            ConfigHandler.SetConfigValue("maxSearchResults", maxSearchResults > 0 ? maxSearchResults.ToString() : null);

            int maxWebContentLength = Host.MaxWebContentLength;
            ConfigHandler.SetConfigValue("maxWebContentLength", maxWebContentLength > 0 ? maxWebContentLength.ToString() : null);
        }
    }
}
