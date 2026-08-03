using System.Runtime.InteropServices;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("D755ACD3-1DA5-41B8-9C06-71CCBBDBF657")]
    public class ContextOptionsPage : ConfigDialogPage<ContextOptionsPageHost>
    {
        protected override ContextOptionsPageHost CreateHost()
        {
            return new ContextOptionsPageHost();
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
