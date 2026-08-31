using System.Runtime.InteropServices;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("8A5B5E5C-4F3D-4E8A-9B2C-1D3E4F5A6B7C")]
    public class OptionsPage : ConfigDialogPage<OptionsPageHost> { }

    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("D755ACD3-1DA5-41B8-9C06-71CCBBDBF657")]
    public class ContextOptionsPage : ConfigDialogPage<ContextOptionsPageHost> { }

    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("3E4F5A6B-7C8D-9E0F-A1B2-C3D4E5F60718")]
    public class ToolsOptionsPage : ConfigDialogPage<ToolsOptionsPageHost> { }

    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("B2C3D4E5-F6A7-4B8C-9D0E-1F2A3B4C5D6E")]
    public class WebSearchOptionsPage : ConfigDialogPage<WebSearchOptionsPageHost> { }

    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("B3E8F4A1-6D2C-4E9B-8F71-2A5C9D0E3B84")]
    public class BuildErrorHandlingOptionsPage : ConfigDialogPage<BuildErrorHandlingOptionsPageHost> { }

    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("03C6AF2D-6C6A-4ACE-B985-E25DB04593FC")]
    public class ModesOptionsPage : ConfigDialogPage<ModesOptionsPageHost> { }

    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("A7E3C1D4-5B82-4F9E-A6D0-2E8F4C9B1A73")]
    public class AppearanceOptionsPage : ConfigDialogPage<AppearanceOptionsPageHost> { }
}
