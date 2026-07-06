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
            if (Host == null) return;
            Host.MarkdownParsing = ConfigHandler.GetMarkdownParsing();
            Host.ShowReasoningOutput = ConfigHandler.GetShowReasoningOutput();
            Host.ShowToolOutput = ConfigHandler.GetShowToolOutput();
        }

        protected override void SaveHostToConfig()
        {
            ConfigHandler.SetMarkdownParsing(Host.MarkdownParsing);
            ConfigHandler.SetShowReasoningOutput(Host.ShowReasoningOutput);
            ConfigHandler.SetShowToolOutput(Host.ShowToolOutput);
        }
    }
}
