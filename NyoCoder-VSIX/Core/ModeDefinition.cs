namespace NyoCoder
{
    public enum ModeToolPolicy
    {
        All,
        AllowList
    }

    /// <summary>
    /// Resolved mode definition used at runtime and in the options UI.
    /// </summary>
    public class ModeDefinition
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        /// <summary>Empty for built-ins means use embedded default prompt.</summary>
        public string SystemPrompt { get; set; }
        public ModeToolPolicy ToolPolicy { get; set; }
        public string[] Tools { get; set; }
        public bool IsBuiltIn { get; set; }

        public ModeDefinition Clone()
        {
            return new ModeDefinition
            {
                Id = Id,
                DisplayName = DisplayName,
                SystemPrompt = SystemPrompt,
                ToolPolicy = ToolPolicy,
                Tools = Tools != null ? (string[])Tools.Clone() : null,
                IsBuiltIn = IsBuiltIn
            };
        }
    }
}
