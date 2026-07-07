namespace NyoCoder
{
    /// <summary>
    /// How build errors are detected after the agent modifies files.
    /// </summary>
    public enum BuildErrorCheckMode
    {
        /// <summary>Read the Error List after a short wait (IntelliSense diagnostics).</summary>
        IntelliSense,

        /// <summary>Build the solution, then read the Error List.</summary>
        BuildSolution,

        /// <summary>No automatic build-error checking.</summary>
        Off
    }
}
