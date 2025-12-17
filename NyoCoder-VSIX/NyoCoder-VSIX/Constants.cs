namespace NyoCoder
{
    /// <summary>
    /// Application constants for NyoCoder.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Maximum number of characters to read from a file at once.
        /// Used by the read_file tool to limit memory usage and API context size.
        /// </summary>
        public const int MAX_CONTENT_LENGTH = 50000;
    }
}
