using System.Runtime.InteropServices;

namespace NyoCoder
{
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [ComVisible(true)]
    [Guid("B3E8F4A1-6D2C-4E9B-8F71-2A5C9D0E3B84")]
    public class BuildErrorHandlingOptionsPage : ConfigDialogPage<BuildErrorHandlingOptionsPageHost>
    {
        protected override BuildErrorHandlingOptionsPageHost CreateHost()
        {
            return new BuildErrorHandlingOptionsPageHost();
        }

        protected override void UpdateHostFromConfig()
        {
            if (Host == null) return;
            Host.Mode = ConfigHandler.GetBuildErrorCheckMode();
            Host.WaitSeconds = ConfigHandler.GetBuildErrorCheckWaitSeconds();
            Host.MaxAttempts = ConfigHandler.GetBuildErrorFixMaxAttempts();
        }

        protected override void SaveHostToConfig()
        {
            if (Host == null) return;
            ConfigHandler.SetBuildErrorCheckMode(Host.Mode);
            ConfigHandler.SetBuildErrorCheckWaitSeconds(Host.WaitSeconds);
            ConfigHandler.SetBuildErrorFixMaxAttempts(Host.MaxAttempts);
        }
    }
}
