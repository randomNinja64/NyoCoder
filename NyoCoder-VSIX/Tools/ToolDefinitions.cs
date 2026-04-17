using Newtonsoft.Json.Linq;
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
            "grep_search",
            "search_replace",
            "run_web_search",
            "read_website",
            "ask_user_question",
            "manage_plan"
        };

        /// <summary>
        /// Builds the JSON array of tool definitions for the LLM API.
        /// </summary>
        public static JArray BuildToolsArray()
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
                        { "filename", new PropertyInfo("string", "The full path of the file to read. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                        { "offset", new PropertyInfo("string", "Optional. Line number to start reading from (0-indexed, default: 0). Use this to read different parts of large files. For example, offset " + ConfigHandler.MaxReadLines + " reads lines " + ConfigHandler.MaxReadLines + "-" + (ConfigHandler.MaxReadLines * 2 - 1) + ".") }
                    },
                    new[] { "filename" }
                ),
                new ToolEntry(
                    "write_file",
                    "Write the given content to a local file, creating or overwriting it.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "filename", new PropertyInfo("string", "The full path of the file to write to. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                        { "content", new PropertyInfo("string", "The content to write into the file.") }
                    },
                    new[] { "filename", "content" }
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
                    "grep_search",
                    "Recursively search for a regular expression pattern in files. Very fast and automatically ignores files you should not read like .pyc files, .venv directories, node_modules, .git, bin/obj folders, etc. Use this to find where functions are defined, how variables are used, or to locate specific error messages.",
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
                    "Replace sections of a file using SEARCH/REPLACE blocks. The SEARCH text must match exactly (including whitespace, indentation, and line endings) and must be unique in the file. Multiple blocks can be provided to make multiple changes to the same file. Format: <<<<<<< SEARCH\n[exact text to find]\n=======\n[replacement text]\n>>>>>>> REPLACE. If the file is part of the project, it will be opened in Visual Studio with changes highlighted.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "file_path", new PropertyInfo("string", "The full path of the file to modify. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                        { "content", new PropertyInfo("string", "The SEARCH/REPLACE blocks defining the changes. Format: <<<<<<< SEARCH\n[exact text to find]\n=======\n[exact text to replace with]\n>>>>>>> REPLACE. Multiple blocks can be included for multiple changes.") }
                    },
                    new[] { "file_path", "content" }
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
                    "Fetch the content of a specific URL to read current information from the web. Useful for reading documentation, articles, or any web page in full. Returns up to " + ConfigHandler.GetConfigInt("maxWebContentLength", 8000) + " characters of page content.",
                    new Dictionary<string, PropertyInfo>
                    {
                        { "url", new PropertyInfo("string", "The URL of the web page to fetch.") }
                    },
                    new[] { "url" }
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
                if (!ConfigHandler.IsToolDisabled(def.Name))
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
