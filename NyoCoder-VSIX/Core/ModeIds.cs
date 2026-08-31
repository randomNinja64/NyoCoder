namespace NyoCoder
{
    /// <summary>
    /// Well-known built-in mode identifiers.
    /// </summary>
    public static class ModeIds
    {
        public const string Agent = "agent";
        public const string Plan = "plan";
        public const string Debug = "debug";

        public static readonly string[] BuiltInOrder = { Agent, Plan, Debug };
    }
}
