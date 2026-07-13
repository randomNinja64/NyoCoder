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
            if (Host == null) return;
            Host.ContextWindowSize = ConfigHandler.ContextWindowSize;
            Host.MaxReadLines = ConfigHandler.MaxReadLines;
            Host.MaxOpenFilesInContext = ConfigHandler.MaxOpenFilesInContext;
        }

        protected override void SaveHostToConfig()
        {
            ConfigHandler.SetContextWindowSize(Host.ContextWindowSize);
            ConfigHandler.SetMaxReadLines(Host.MaxReadLines);
            ConfigHandler.SetMaxOpenFilesInContext(Host.MaxOpenFilesInContext);
        }
    }
}
