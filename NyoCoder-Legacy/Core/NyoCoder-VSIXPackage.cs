namespace NyoCoder
{
    /// <summary>
    /// VS 2005/2008 "package": created and sited by the add-in (<see cref="Connect"/>) rather
    /// than registered with the shell. Product logic lives in <see cref="NyoCoderPackageBase"/>.
    /// </summary>
    public sealed class NyoCoder_VSIXPackage : NyoCoderPackageBase
    {
        /// <summary>
        /// Gets the singleton instance of the package.
        /// </summary>
        public new static NyoCoder_VSIXPackage Instance
        {
            get { return (NyoCoder_VSIXPackage)_instance; }
        }

        /// <summary>Add-in commands are addressed by name, not by command-set GUID and ID.</summary>
        protected override void SetupKeyboardBinding()
        {
            try
            {
                ApplicationObject.Commands.Item(Connect.CommandPrefix + "nyoCoderAskCmd", -1).Bindings =
                    new object[] { "Global::Ctrl+Alt+N" };
            }
            catch { }
        }
    }
}
