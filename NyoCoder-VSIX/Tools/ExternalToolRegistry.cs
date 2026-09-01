using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NyoCoder
{
    /// <summary>
    /// Loads and executes SimpleLLMChat-compatible external tool packages from
    /// %APPDATA%\NyoCoder\tools\. Each package is a folder containing a JSON
    /// manifest and one or more tool executables.
    ///
    /// Invocation protocol (identical to SimpleLLMChat):
    ///   1. Spawn: <executable> <tool_name>
    ///   2. Write UTF-8 JSON to stdin: { "config": {...}, "arguments": {...} }
    ///   3. Read stdout as the tool result; stderr is appended if non-empty.
    ///
    /// Context injectors (optional manifest field "context_injector"):
    ///   1. Spawn: <executable> <context_injector>
    ///   2. Write UTF-8 JSON to stdin: { "config": {...}, "arguments": {} }
    ///   3. Append stdout to the system prompt when at least one tool from the package is enabled.
    /// </summary>
    internal static class ExternalToolRegistry
    {
        public struct ToolParameterInfo
        {
            public string Name;
            public string Type;
            public string Description;
            public bool Required;
            public JObject Items;
        }

        public struct ExternalToolDefinition
        {
            public string Name;
            public string Description;
            public string ExecutablePath;
            public List<ToolParameterInfo> Parameters;
        }

        public struct OptionDefinition
        {
            public string Name;    // config key
            public string Label;   // display label
            public string Type;    // "string", "int", or "bool"
            public string Default;
        }

        public struct PackageInfo
        {
            public string DisplayName;
            public List<string> ToolNames;
            public List<OptionDefinition> Options;
        }

        private static readonly Dictionary<string, ExternalToolDefinition> _tools =
            new Dictionary<string, ExternalToolDefinition>(StringComparer.OrdinalIgnoreCase);

        // Context injectors declared by manifests via the "context_injector" field (executablePath -> commandName).
        private static readonly Dictionary<string, string> _contextInjectorsByExecutable =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<PackageInfo> _packages = new List<PackageInfo>();

        // Option defaults declared in manifests (fallback before NyoCoder config)
        private static readonly Dictionary<string, string> _optionDefaults =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded = false;
        private static readonly object _lock = new object();

        public static string ToolsDirectory
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "NyoCoder", "tools");
            }
        }

        /// <summary>
        /// Loads tools on first access. Safe to call multiple times.
        /// </summary>
        public static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (!_loaded)
                {
                    LoadToolsFromDirectory(ToolsDirectory);
                    _loaded = true;
                }
            }
        }

        public static void LoadToolsFromDirectory(string toolsDir)
        {
            if (!Directory.Exists(toolsDir))
                return;

            foreach (string jsonFile in GetManifestFiles(toolsDir))
            {
                try { LoadManifest(jsonFile); }
                catch { /* skip malformed manifests */ }
            }
        }

        private static IEnumerable<string> GetManifestFiles(string toolsDir)
        {
            var files = new List<string>(Directory.GetFiles(toolsDir, "*.json"));
            foreach (string subDir in Directory.GetDirectories(toolsDir))
                files.AddRange(Directory.GetFiles(subDir, "*.json"));
            return files;
        }

        private static void LoadManifest(string jsonFilePath)
        {
            string json = File.ReadAllText(jsonFilePath, Encoding.UTF8);
            JObject manifest = JObject.Parse(json);

            string packageName = (string)manifest["name"] ?? "";
            string executable = (string)manifest["executable"] ?? "";
            string manifestDir = Path.GetDirectoryName(jsonFilePath);
            string executablePath = Path.Combine(manifestDir, executable);

            string contextInjectorCommand = (string)manifest["context_injector"];
            if (!string.IsNullOrEmpty(contextInjectorCommand))
                _contextInjectorsByExecutable[executablePath] = contextInjectorCommand;

            // Collect option definitions for the UI and store defaults for process invocation
            var optionDefs = new List<OptionDefinition>();
            JArray optionsArray = manifest["options"] as JArray;
            if (optionsArray != null)
            {
                foreach (JObject optObj in optionsArray)
                {
                    string optName = (string)optObj["name"] ?? "";
                    string optDefault = (string)optObj["default"] ?? "";
                    if (!string.IsNullOrEmpty(optName))
                    {
                        _optionDefaults[optName] = optDefault;
                        optionDefs.Add(new OptionDefinition
                        {
                            Name = optName,
                            Label = (string)optObj["label"] ?? optName,
                            Type = (string)optObj["type"] ?? "string",
                            Default = optDefault
                        });
                    }
                }
            }

            JArray toolsArray = manifest["tools"] as JArray;
            if (toolsArray == null)
                return;

            var toolNames = new List<string>();

            foreach (JObject toolObj in toolsArray)
            {
                string name = (string)toolObj["name"] ?? "";
                if (string.IsNullOrEmpty(name))
                    continue;

                var def = new ExternalToolDefinition
                {
                    Name = name,
                    Description = (string)toolObj["description"] ?? "",
                    ExecutablePath = executablePath,
                    Parameters = new List<ToolParameterInfo>()
                };

                JArray paramsArray = toolObj["parameters"] as JArray;
                if (paramsArray != null)
                {
                    foreach (JObject paramObj in paramsArray)
                    {
                        JToken requiredToken = paramObj["required"];
                        bool isRequired = requiredToken != null && requiredToken.Type != JTokenType.Null
                            && requiredToken.Value<bool>();
                        def.Parameters.Add(new ToolParameterInfo
                        {
                            Name = (string)paramObj["name"] ?? "",
                            Type = (string)paramObj["type"] ?? "string",
                            Description = (string)paramObj["description"] ?? "",
                            Required = isRequired,
                            Items = paramObj["items"] as JObject
                        });
                    }
                }

                _tools[name] = def;
                toolNames.Add(name);
            }

            if (toolNames.Count > 0 || optionDefs.Count > 0)
            {
                _packages.Add(new PackageInfo
                {
                    DisplayName = string.IsNullOrEmpty(packageName) ? Path.GetFileNameWithoutExtension(jsonFilePath) : packageName,
                    ToolNames = toolNames,
                    Options = optionDefs
                });
            }
        }

        public static bool HasTool(string name)
        {
            EnsureLoaded();
            return _tools.ContainsKey(name);
        }

        /// <summary>
        /// Returns all loaded packages with their tool names and option definitions.
        /// Used by the options UI to build dynamic controls.
        /// </summary>
        public static List<PackageInfo> GetPackages()
        {
            EnsureLoaded();
            return new List<PackageInfo>(_packages);
        }

        /// <summary>
        /// Returns manifest-declared defaults for all option keys across all packages.
        /// </summary>
        public static Dictionary<string, string> GetOptionDefaults()
        {
            EnsureLoaded();
            return new Dictionary<string, string>(_optionDefaults, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Calls each package's context injector for packages that have at least
        /// one enabled tool, and returns all non-empty results for injection into the system prompt.
        /// </summary>
        public static List<string> GetContextInjections(IEnumerable<string> enabledTools)
        {
            var results = new List<string>();

            if (enabledTools == null || _contextInjectorsByExecutable.Count == 0)
                return results;

            EnsureLoaded();

            var activeExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string toolName in enabledTools)
            {
                ExternalToolDefinition def;
                if (_tools.TryGetValue(toolName, out def) && !string.IsNullOrEmpty(def.ExecutablePath))
                    activeExecutables.Add(def.ExecutablePath);
            }

            foreach (var kvp in _contextInjectorsByExecutable)
            {
                if (!activeExecutables.Contains(kvp.Key))
                    continue;

                string output = InvokeContextProvider(kvp.Key, kvp.Value);
                if (!string.IsNullOrWhiteSpace(output))
                    results.Add(output.Trim());
            }

            return results;
        }

        private static string InvokeContextProvider(string executablePath, string commandName)
        {
            try
            {
                JObject configObj = new JObject();
                foreach (var kvp in _optionDefaults)
                    configObj[kvp.Key] = kvp.Value;
                foreach (var kvp in ConfigHandler.GetAllValues())
                    configObj[kvp.Key] = kvp.Value;

                JObject stdinPayload = new JObject();
                stdinPayload["config"] = configObj;
                stdinPayload["arguments"] = new JObject();

                string stdinData = stdinPayload.ToString(Formatting.None);

                int exitCode;
                return ProcessRunner.RunCommand(
                    executablePath,
                    commandName,
                    out exitCode,
                    combineErrorOutput: false,
                    stdinData: stdinData,
                    workingDirectory: Path.GetDirectoryName(executablePath));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the OpenAI-compatible tool definitions for all loaded external tools.
        /// </summary>
        public static JArray BuildToolsArray()
        {
            EnsureLoaded();
            var result = new JArray();

            foreach (var kvp in _tools)
            {
                if (ConfigHandler.IsToolDisabled(kvp.Key))
                    continue;

                ExternalToolDefinition def = kvp.Value;

                JObject props = new JObject();
                JArray required = new JArray();

                foreach (var param in def.Parameters)
                {
                    JObject propObj = new JObject();
                    propObj["type"] = param.Type;
                    propObj["description"] = param.Description;
                    if (param.Items != null)
                        propObj["items"] = param.Items;
                    props[param.Name] = propObj;
                    if (param.Required)
                        required.Add(param.Name);
                }

                JObject parameters = new JObject();
                parameters["type"] = "object";
                parameters["properties"] = props;
                parameters["required"] = required;

                JObject function = new JObject();
                function["name"] = def.Name;
                function["description"] = def.Description;
                function["parameters"] = parameters;

                JObject entry = new JObject();
                entry["type"] = "function";
                entry["function"] = function;
                result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// Executes an external tool by spawning its process and communicating via stdin/stdout.
        /// </summary>
        public static void ExecuteToolCall(string toolName, string arguments, out string toolContent, out int exitCode)
        {
            toolContent = "";
            exitCode = 0;

            EnsureLoaded();

            ExternalToolDefinition def;
            if (!_tools.TryGetValue(toolName, out def))
            {
                toolContent = ToolHandler.FormatCommandResult(toolName, "error: unknown tool '" + toolName + "'.", 1);
                exitCode = 1;
                return;
            }

            try
            {
                // Build stdin payload: { "config": {...}, "arguments": {...} }
                JObject configObj = new JObject();
                // Seed with manifest option defaults first
                foreach (var kvp in _optionDefaults)
                    configObj[kvp.Key] = kvp.Value;
                // Overlay with NyoCoder's own config values
                foreach (var kvp in ConfigHandler.GetAllValues())
                    configObj[kvp.Key] = kvp.Value;

                JObject stdinPayload = new JObject();
                stdinPayload["config"] = configObj;
                stdinPayload["arguments"] = string.IsNullOrWhiteSpace(arguments)
                    ? new JObject()
                    : JToken.Parse(arguments);

                string stdinData = stdinPayload.ToString(Formatting.None);

                string output = ProcessRunner.RunCommand(
                    def.ExecutablePath,
                    toolName,
                    out exitCode,
                    combineErrorOutput: true,
                    stdinData: stdinData,
                    workingDirectory: Path.GetDirectoryName(def.ExecutablePath));

                toolContent = ToolHandler.FormatCommandResult(toolName, output, exitCode);
            }
            catch (Exception ex)
            {
                exitCode = 1;
                toolContent = ToolHandler.FormatCommandResult(toolName, "error: " + ex.Message, exitCode);
            }
        }
    }
}
