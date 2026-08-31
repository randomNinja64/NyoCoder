using System.Runtime.InteropServices;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("03C6AF2D-6C6A-4ACE-B985-E25DB04593FC")]
    public class ModesOptionsPage : ConfigDialogPage<ModesOptionsPageHost>
    {
        protected override ModesOptionsPageHost CreateHost()
        {
            return new ModesOptionsPageHost();
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
