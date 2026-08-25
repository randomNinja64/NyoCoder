using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace NyoCoder
{
    /// <summary>
    /// Defines the tool schemas for the LLM API.
    /// These definitions describe the available tools and their parameters.
    /// </summary>
    internal static class ToolDefinitions
    {
        private struct PropertyInfo
        {
            public string Type;
            public string Description;
            public string ItemType;
            public Dictionary<string, PropertyInfo> ItemProperties;
            public string[] ItemRequired;

            public PropertyInfo(string type, string description, string itemType = null,
                Dictionary<string, PropertyInfo> itemProperties = null, string[] itemRequired = null)
            {
                Type = type;
                Description = description;
                ItemType = itemType;
                ItemProperties = itemProperties;
                ItemRequired = itemRequired;
            }
        }

        private struct ToolEntry
        {
            public string Name;
            public string Description;
            public Dictionary<string, PropertyInfo> Props;
            public string[] Required;

            public ToolEntry(string name, string description, Dictionary<string, PropertyInfo> props, string[] required)
            {
                Name = name;
                Description = description;
                Props = props;
                Required = required;
            }
        }

        /// <summary>
        /// All built-in tool names, in display order.
        /// </summary>
        public static readonly string[] BuiltInToolNames = new[]
        {
            "run_shell_command",
            "read_file",
            "write_file",
            "move_file",
            "copy_file",
            "delete_file",
            "list_directory",
            "codebase_search",
            "grep_search",
            "search_replace",
            "run_web_search",
            "read_website",
            "view_skill",
            "create_skill",
            "edit_skill",
            "edit_skill_file",
            "remove_skill",
            "ask_user_question",
            "manage_plan"
        };

        private static readonly HashSet<string> AfterPreviewApprovalTools =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "write_file",
            "search_replace"
        };

        private static readonly HashSet<string> FileModifyingTools =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "write_file",
            "search_replace",
            "delete_file",
            "move_file",
            "copy_file"
        };

        /// <summary>
        /// Returns true when the tool can modify files on disk.
        /// </summary>
        public static bool IsFileModifyingTool(string toolName)
        {
            return !string.IsNullOrEmpty(toolName) && FileModifyingTools.Contains(toolName);
        }

        /// <summary>
        /// Built-in tools that approve after showing a diff preview rather than before execution.
        /// </summary>
        public static bool UsesAfterPreviewApproval(string toolName)
        {
            return !string.IsNullOrEmpty(toolName) && AfterPreviewApprovalTools.Contains(toolName);
        }

        /// <summary>
        /// Tools available in Plan mode (read-only + planning).
        /// write_file and search_replace are included so the plan can be authored and
        /// refined in PLAN.md; ToolHandler restricts them to that file while in Plan mode.
        /// </summary>
        private static readonly HashSet<string> PlanModeTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "read_file",
            "list_directory",
            "codebase_search",
            "grep_search",
            "run_web_search",
            "read_website",
            "view_skill",
            "ask_user_question",
            "write_file",
            "search_replace"
        };

        /// <summary>
        /// Returns the names of all tools that would be sent to the LLM for the given mode.
        /// </summary>
        public static List<string> GetEnabledToolNames(ChatMode mode = ChatMode.Agent)
        {
            var enabled = new List<string>();

            foreach (string name in BuiltInToolNames)
            {
                if (ConfigHandler.IsToolDisabled(name))
                    continue;
                if (mode == ChatMode.Plan && !PlanModeTools.Contains(name))
                    continue;
                enabled.Add(name);
            }

            foreach (ExternalToolRegistry.PackageInfo pkg in ExternalToolRegistry.GetPackages())
            {
                foreach (string name in pkg.ToolNames)
                {
                    if (!ConfigHandler.IsToolDisabled(name))
                        enabled.Add(name);
                }
            }

            return enabled;
        }

        /// <summary>
        /// Builds the JSON array of tool definitions for the LLM API.
        /// Uses Agent mode (all tools) by default.
        /// </summary>
        public static JArray BuildToolsArray()
        {
            return BuildToolsArray(ChatMode.Agent);
        }

        /// <summary>
        /// Builds the JSON array of tool definitions filtered by chat mode.
        /// In Plan mode, only read-only + planning tools are included.
        /// </summary>
        public static JArray BuildToolsArray(ChatMode mode)
        {
            ToolEntry[] definitions = new ToolEntry[]
            {
                new ToolEntry(
                    "run_shell_command",
                    "Execute a shell command on the host system and return its output.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "command", new PropertyInfo("string", "Full command line to execute. Keep it short and avoid interactive programs.") }
                    },
                    new[] { "command" }
                ),
                new ToolEntry(
                    "read_file",
                    "Read the contents of a local file and return it as a string. Always reads up to " + ConfigHandler.MaxReadLines + " lines. Use the offset parameter to read different parts of large files.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "file_path", new PropertyInfo("string", "The full path of the file to read. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                        { "offset", new PropertyInfo("string", "Optional. Line number to start reading from (0-indexed, default: 0). Use this to read different parts of large files. For example, offset " + ConfigHandler.MaxReadLines + " reads lines " + ConfigHandler.MaxReadLines + "-" + (ConfigHandler.MaxReadLines * 2 - 1) + ".") }
                    },
                    new[] { "file_path" }
                ),
                new ToolEntry(
                    "write_file",
                    "Write the given content to a local file, creating or overwriting it.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "file_path", new PropertyInfo("string", "The full path of the file to write to. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                        { "content", new PropertyInfo("string", "The content to write into the file.") }
                    },
                    new[] { "file_path", "content" }
                ),
                new ToolEntry(
                    "move_file",
                    "Move or rename a file from one location to another. Destination directory will be created if it doesn't exist.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "source_path", new PropertyInfo("string", "The full path of the file to move. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                        { "destination_path", new PropertyInfo("string", "The full path where the file should be moved to. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                    },
                    new[] { "source_path", "destination_path" }
                ),
                new ToolEntry(
                    "copy_file",
                    "Copy a file from one location to another. Destination directory will be created if it doesn't exist.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "source_path", new PropertyInfo("string", "The full path of the file to copy. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                        { "destination_path", new PropertyInfo("string", "The full path where the file should be copied to. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                    },
                    new[] { "source_path", "destination_path" }
                ),
                new ToolEntry(
                    "delete_file",
                    "Delete a file from the file system. Use with caution as this operation cannot be undone.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "file_path", new PropertyInfo("string", "The full path of the file to delete. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                    },
                    new[] { "file_path" }
                ),
                new ToolEntry(
                    "list_directory",
                    "List all files and subdirectories in a given directory.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "directory_path", new PropertyInfo("string", "The full path of the directory to list. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                    },
                    new[] { "directory_path" }
                ),
                new ToolEntry(
                    "codebase_search",
                    "Find snippets of code from the codebase most relevant to the search query. This is a semantic search tool, so the query should ask for something semantically matching what is needed rather than exact text. Unless there is a clear reason to use your own wording, just reuse the user's exact query, since their phrasing is often helpful for semantic search. This is your primary search tool for exploring the codebase; only use grep_search when you need an exact string or regex match.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "query", new PropertyInfo("string", "The search query to find relevant code. Reuse the user's exact query with their wording unless there is a clear reason not to.") }
                    },
                    new[] { "query" }
                ),
                new ToolEntry(
                    "grep_search",
                    "Recursively search for a regular expression pattern in files. Very fast and automatically ignores files you should not read like .pyc files, .venv directories, node_modules, .git, bin/obj folders, etc. Use this only for an exact string or regex match, such as a specific error message; codebase_search is preferred if available for all other code exploration, including finding definitions and usages.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "pattern", new PropertyInfo("string", "The regular expression pattern to search for.") },
                        { "directory_path", new PropertyInfo("string", "Optional. The directory to search in. Defaults to current directory if not specified. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                        { "file_pattern", new PropertyInfo("string", "Optional. File pattern to filter (e.g., '*.cs', '*.py'). Searches all files if not specified.") },
                        { "case_insensitive", new PropertyInfo("string", "Optional. Set to 'true' for case-insensitive search. Default is case-sensitive.") }
                    },
                    new[] { "pattern" }
                ),
                new ToolEntry(
                    "search_replace",
                    "Replace exact text in a file. Provide one or more edits; each edit's old_string must match exactly (including whitespace, indentation, and line endings) and occur exactly once in the file. Multiple edits can be provided to make multiple changes to the same file in one call. If the file is part of the project, it will be opened in Visual Studio with changes highlighted.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "file_path", new PropertyInfo("string", "The full path of the file to modify. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                        { "edits", new PropertyInfo("array", "One or more edits to apply. Each edit's old_string must be unique in the file.", "object",
                            new Dictionary<string, PropertyInfo>
                            {
                                { "old_string", new PropertyInfo("string", "The exact text to find, including whitespace and indentation. Must occur exactly once in the file.") },
                                { "new_string", new PropertyInfo("string", "The text to replace old_string with.") }
                            },
                            new[] { "old_string", "new_string" }) }
                    },
                    new[] { "file_path", "edits" }
                ),
                new ToolEntry(
                    "run_web_search",
                    "Search the web for current information that may not be in your training data. Returns a list of relevant URLs with brief snippets. Use read_website to follow up on specific results.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "query", new PropertyInfo("string", "The search query to look up on the web.") }
                    },
                    new[] { "query" }
                ),
                new ToolEntry(
                    "read_website",
                    "Browse to a URL and return Title/Desc, page text and images (alts+srcs), and a Links section. Useful for reading documentation, articles, or any web page. Output is capped at " + ConfigHandler.GetConfigInt("maxWebContentLength", 10000) + " characters; truncating body as needed.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "url", new PropertyInfo("string", "The URL of the web page to fetch.") }
                    },
                    new[] { "url" }
                ),
                new ToolEntry(
                    "view_skill",
                    "Load a skill's SKILL.md, absolute skill root, and relative linked-file listing. With relative_path, reads that file (Skill root / File headers; no listing).",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "name", new PropertyInfo("string", "The skill name.") },
                        { "relative_path", new PropertyInfo("string", "Optional relative path within the skill to read (e.g. 'references/api.md'). Omit to get SKILL.md plus the linked-files listing.") }
                    },
                    new[] { "name" }
                ),
                new ToolEntry(
                    "create_skill",
                    "Create a new skill directory with SKILL.md only (YAML frontmatter + markdown instructions). Errors if the skill already exists.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "name", new PropertyInfo("string", "Skill name: lowercase letters, numbers, and hyphens only (e.g. 'code-review'). Becomes the folder name.") },
                        { "description", new PropertyInfo("string", "What the skill does and when to use it (third person). Truncated to 1024 characters.") },
                        { "instructions", new PropertyInfo("string", "Markdown body of SKILL.md (procedure, when to load supporting files, examples).") }
                    },
                    new[] { "name", "description", "instructions" }
                ),
                new ToolEntry(
                    "edit_skill",
                    "Update an existing skill's SKILL.md (description and/or instructions; omitted fields unchanged). Errors if the skill does not exist.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "name", new PropertyInfo("string", "The existing skill name.") },
                        { "description", new PropertyInfo("string", "Optional. New description (third person). Truncated to 1024 characters. Omit to leave the current description unchanged.") },
                        { "instructions", new PropertyInfo("string", "Optional. New markdown body for SKILL.md. Omit to leave current instructions unchanged.") }
                    },
                    new[] { "name" }
                ),
                new ToolEntry(
                    "edit_skill_file",
                    "Create or overwrite a supporting file under an existing skill. Errors if the skill does not exist. Creates parent directories as needed.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "name", new PropertyInfo("string", "The existing skill name.") },
                        { "relative_path", new PropertyInfo("string", "Relative path within the skill (e.g. 'scripts/helper.py' or 'references/api.md').") },
                        { "content", new PropertyInfo("string", "Full file contents to write.") }
                    },
                    new[] { "name", "relative_path", "content" }
                ),
                new ToolEntry(
                    "remove_skill",
                    "Delete an agent skill directory (SKILL.md and all supporting files) by skill name.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "name", new PropertyInfo("string", "The skill name to remove.") }
                    },
                    new[] { "name" }
                ),
                new ToolEntry(
                    "ask_user_question",
                    "Ask the user a question and wait for their response. The UI displays a button for each option plus a free-form text field so the user can type their own answer. Use this to gather preferences, clarify ambiguous requirements, or get a decision before continuing. Prefer this over guessing when the user's intent is unclear.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "question", new PropertyInfo("string", "The question text to present to the user.") },
                        { "options", new PropertyInfo("array", "Preset answer choices (2-4 recommended). Each appears as a clickable button. A free-form text field is always shown in addition to these.", "string") }
                    },
                    new[] { "question" }
                ),
                new ToolEntry(
                    "manage_plan",
                    "Track progress on multi-step tasks. Use proactively for complex tasks (3+ steps) or when the user provides multiple tasks. Skip for single straightforward tasks or conversational requests.\n\nRules:\n- Only ONE step in_progress at a time\n- Mark a step in_progress BEFORE starting it\n- Mark completed IMMEDIATELY after finishing (only when fully done)\n- When writing, include ALL steps — omitted steps are removed\n- Add newly discovered work as new steps",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "action", new PropertyInfo("string", "'read' to view the current plan, or 'write' to replace it entirely.") },
                        { "steps", new PropertyInfo("array", "Required for 'write'. Complete list of ALL steps — omitted steps are removed.", "object",
                            new Dictionary<string, PropertyInfo>
                            {
                                { "title", new PropertyInfo("string", "Short description of the step.") },
                                { "status", new PropertyInfo("string", "One of: pending, in_progress, completed, failed, skipped.") }
                            },
                            new[] { "title", "status" }) }
                    },
                    new[] { "action" }
                ),
            };

            JArray toolsArray = new JArray();

            foreach (ToolEntry def in definitions)
            {
                if (ConfigHandler.IsToolDisabled(def.Name))
                    continue;

                // In Plan mode, only include read-only / planning tools
                if (mode == ChatMode.Plan && !PlanModeTools.Contains(def.Name))
                    continue;

                toolsArray.Add(CreateToolDefinition(def.Name, def.Description, def.Props, def.Required));
            }

            // Append any SimpleLLMChat-compatible external tools installed under
            // %APPDATA%\NyoCoder\tools\
            foreach (JToken externalTool in ExternalToolRegistry.BuildToolsArray())
                toolsArray.Add(externalTool);

            return toolsArray;
        }

        private static JObject CreateToolDefinition(string name, string description, Dictionary<string, PropertyInfo> properties, string[] required)
        {
            JObject tool = new JObject();
            tool["type"] = "function";

            JObject func = new JObject();
            func["name"] = name;
            func["description"] = description;

            JObject parameters = new JObject();
            parameters["type"] = "object";

            JObject props = new JObject();
            foreach (var prop in properties)
            {
                JObject propObj = new JObject();
                propObj["type"] = prop.Value.Type;
                propObj["description"] = prop.Value.Description;
                if (prop.Value.Type == "array" && prop.Value.ItemType != null)
                {
                    JObject itemsObj = new JObject();
                    itemsObj["type"] = prop.Value.ItemType;
                    if (prop.Value.ItemProperties != null)
                    {
                        JObject itemProps = new JObject();
                        foreach (var itemProp in prop.Value.ItemProperties)
                        {
                            JObject itemPropObj = new JObject();
                            itemPropObj["type"] = itemProp.Value.Type;
                            itemPropObj["description"] = itemProp.Value.Description;
                            itemProps[itemProp.Key] = itemPropObj;
                        }
                        itemsObj["properties"] = itemProps;
                        if (prop.Value.ItemRequired != null && prop.Value.ItemRequired.Length > 0)
                            itemsObj["required"] = new JArray(prop.Value.ItemRequired);
                    }
                    propObj["items"] = itemsObj;
                }
                props[prop.Key] = propObj;
            }
            parameters["properties"] = props;
            parameters["required"] = new JArray(required);

            func["parameters"] = parameters;
            tool["function"] = func;
            return tool;
        }

        internal static int _toolDefinitionsLength = -1;

        /// <summary>
        /// Gets the approximate character length of all tool definitions.
        /// Used for token estimation. Cached until the disabled tools list changes.
        /// </summary>
        public static int GetToolDefinitionsLength()
        {
            if (_toolDefinitionsLength < 0)
                _toolDefinitionsLength = BuildToolsArray().ToString().Length;
            return _toolDefinitionsLength;
        }
    }
}
