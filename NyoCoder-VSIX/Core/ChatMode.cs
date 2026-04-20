namespace NyoCoder
{
    /// <summary>
    /// The interaction mode for a chat message.
    /// </summary>
    public enum ChatMode
    {
        /// <summary>
        /// Full autonomous agent — reads, writes, executes, and manages plans.
        /// </summary>
        Agent,

        /// <summary>
        /// Plan-only mode — reads and proposes a plan of changes for user review
        /// before any files are modified.
        /// </summary>
        Plan,

        /// <summary>
        /// Debug mode — focuses on diagnosing errors with full compiler-error
        /// context and all tools available.
        /// </summary>
        Debug
    }
}
