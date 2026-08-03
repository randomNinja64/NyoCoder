using System.Runtime.InteropServices;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("A7E3C1D4-5B82-4F9E-A6D0-2E8F4C9B1A73")]
    public class AppearanceOptionsPage : ConfigDialogPage<AppearanceOptionsPageHost>
    {
        protected override AppearanceOptionsPageHost CreateHost()
        {
            return new AppearanceOptionsPageHost();
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
