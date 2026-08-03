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
            return new WebSearchOptionsPageHost();
        }

        protected override void UpdateHostFromConfig()
        {
            if (Host != null)
                Host.LoadFromConfig();
        }

        protected override void SaveHostToConfig()
        {
            if (Host != null)
                Host.SaveToConfig();
        }
    }
}
