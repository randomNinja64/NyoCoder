using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace NyoCoder
{
    /// <summary>
    /// VS 2010–2015 registered package. Product logic lives in <see cref="NyoCoderPackageBase"/>.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(NyoCoderToolWindow))]
    [ProvideOptionPage(typeof(OptionsPage), "NyoCoder", "General", 0, 0, true)]
    [ProvideOptionPage(typeof(ContextOptionsPage), "NyoCoder", "Context", 0, 0, true)]
    [ProvideOptionPage(typeof(ToolsOptionsPage), "NyoCoder", "Tools", 0, 0, true)]
    [ProvideOptionPage(typeof(WebSearchOptionsPage), "NyoCoder", "Web Search", 0, 0, true)]
    [ProvideOptionPage(typeof(IndexingOptionsPage), "NyoCoder", "Indexing", 0, 0, true)]
    [ProvideOptionPage(typeof(AppearanceOptionsPage), "NyoCoder", "Appearance", 0, 0, true)]
    [ProvideOptionPage(typeof(BuildErrorHandlingOptionsPage), "NyoCoder", "Build Error Handling", 0, 0, true)]
    [Guid(GuidList.guidNyoCoder_VSIXPkgString)]
    public sealed class NyoCoder_VSIXPackage : NyoCoderPackageBase
    {
        /// <summary>
        /// Gets the singleton instance of the registered package.
        /// </summary>
        public new static NyoCoder_VSIXPackage Instance
        {
            get { return (NyoCoder_VSIXPackage)_instance; }
        }

        public NyoCoder_VSIXPackage()
        {
        }
    }
}
