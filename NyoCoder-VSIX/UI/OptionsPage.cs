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
