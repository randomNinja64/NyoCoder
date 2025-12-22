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

            public PropertyInfo(string type, string description)
            {
                Type = type;
                Description = description;
            }
        }

        /// <summary>
        /// Builds the JSON array of tool definitions for the LLM API.
        /// </summary>
        public static JArray BuildToolsArray()
        {
            JArray toolsArray = new JArray();

            toolsArray.Add(CreateToolDefinition(
                "run_shell_command",
                "Execute a shell command on the host system and return its output.",
                new Dictionary<string, PropertyInfo>
                {
                    { "command", new PropertyInfo("string", "Full command line to execute. Keep it short and avoid interactive programs.") }
                },
                new[] { "command" }
            ));

            toolsArray.Add(CreateToolDefinition(
                "read_file",
                "Read the contents of a local file and return it as a string. Always reads up to " + ConfigHandler.MaxReadLines + " lines. Use the offset parameter to read different parts of large files.",
                new Dictionary<string, PropertyInfo>
                {
                    { "filename", new PropertyInfo("string", "The full path of the file to read. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                    { "offset", new PropertyInfo("string", "Optional. Line number to start reading from (0-indexed, default: 0). Use this to read different parts of large files. For example, offset " + ConfigHandler.MaxReadLines + " reads lines " + ConfigHandler.MaxReadLines + "-" + (ConfigHandler.MaxReadLines * 2 - 1) + ".") }
                },
                new[] { "filename" }
            ));

            toolsArray.Add(CreateToolDefinition(
                "write_file",
                "Write the given content to a local file, creating or overwriting it.",
                new Dictionary<string, PropertyInfo>
                {
                    { "filename", new PropertyInfo("string", "The full path of the file to write to. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                    { "content", new PropertyInfo("string", "The content to write into the file.") }
                },
                new[] { "filename", "content" }
            ));

            toolsArray.Add(CreateToolDefinition(
                "move_file",
                "Move or rename a file from one location to another. Destination directory will be created if it doesn't exist.",
                new Dictionary<string, PropertyInfo>
                {
                    { "source_path", new PropertyInfo("string", "The full path of the file to move. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                    { "destination_path", new PropertyInfo("string", "The full path where the file should be moved to. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                },
                new[] { "source_path", "destination_path" }
            ));

            toolsArray.Add(CreateToolDefinition(
                "copy_file",
                "Copy a file from one location to another. Destination directory will be created if it doesn't exist.",
                new Dictionary<string, PropertyInfo>
                {
                    { "source_path", new PropertyInfo("string", "The full path of the file to copy. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                    { "destination_path", new PropertyInfo("string", "The full path where the file should be copied to. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                },
                new[] { "source_path", "destination_path" }
            ));

            toolsArray.Add(CreateToolDefinition(
                "delete_file",
                "Delete a file from the file system. Use with caution as this operation cannot be undone.",
                new Dictionary<string, PropertyInfo>
                {
                    { "file_path", new PropertyInfo("string", "The full path of the file to delete. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                },
                new[] { "file_path" }
            ));

            toolsArray.Add(CreateToolDefinition(
                "list_directory",
                "List all files and subdirectories in a given directory.",
                new Dictionary<string, PropertyInfo>
                {
                    { "directory_path", new PropertyInfo("string", "The full path of the directory to list. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") }
                },
                new[] { "directory_path" }
            ));

            toolsArray.Add(CreateToolDefinition(
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
            ));

            toolsArray.Add(CreateToolDefinition(
                "search_replace",
                "Use `search_replace` to make targeted changes to files using SEARCH/REPLACE blocks. This tool finds exact text matches and replaces them. The content format uses SEARCH/REPLACE blocks: <<<<<<< SEARCH\n[exact text to find]\n=======\n[exact text to replace with]\n>>>>>>> REPLACE. You can include multiple SEARCH/REPLACE blocks to make multiple changes to the same file. The SEARCH text must match EXACTLY (including whitespace, indentation, and line endings). If the file is part of the project, it will be opened in Visual Studio with changes highlighted.",
                new Dictionary<string, PropertyInfo>
                {
                    { "file_path", new PropertyInfo("string", "The full path of the file to modify. Supports environment variables like %USERPROFILE%, %APPDATA%, %TEMP%, etc.") },
                    { "content", new PropertyInfo("string", "The SEARCH/REPLACE blocks defining the changes. Format: <<<<<<< SEARCH\n[exact text to find]\n=======\n[exact text to replace with]\n>>>>>>> REPLACE. Multiple blocks can be included for multiple changes.") }
                },
                new[] { "file_path", "content" }
            ));

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
                props[prop.Key] = propObj;
            }
            parameters["properties"] = props;
            parameters["required"] = new JArray(required);

            func["parameters"] = parameters;
            tool["function"] = func;
            return tool;
        }

        /// <summary>
        /// Gets the approximate character length of all tool definitions.
        /// Used for token estimation.
        /// </summary>
        public static int GetToolDefinitionsLength()
        {
            JArray tools = BuildToolsArray();
            return tools.ToString().Length;
        }
    }
}
