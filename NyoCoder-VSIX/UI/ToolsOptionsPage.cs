using System.Runtime.InteropServices;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("3E4F5A6B-7C8D-9E0F-A1B2-C3D4E5F60718")]
    public class ToolsOptionsPage : ConfigDialogPage<ToolsOptionsPageHost>
    {
        protected override ToolsOptionsPageHost CreateHost()
        {
            return new ToolsOptionsPageHost();
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
