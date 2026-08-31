using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace NyoCoder
{
    /// <summary>
    /// Loads, merges, and persists chat mode definitions from modes.json.
    /// </summary>
    public static class ModeRegistry
    {
        private static readonly Dictionary<string, ModeDefinition> _resolved =
            new Dictionary<string, ModeDefinition>(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, ModeDefinition> _builtInOverrides =
            new Dictionary<string, ModeDefinition>(StringComparer.OrdinalIgnoreCase);

        private static List<ModeDefinition> _customModes = new List<ModeDefinition>();

        private static string _modesFilePath;

        public static event Action ModesChanged;

        public static void Initialize()
        {
            if (_modesFilePath != null)
                return;

            string configFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NyoCoder");

            if (!Directory.Exists(configFolder))
                Directory.CreateDirectory(configFolder);

            _modesFilePath = Path.Combine(configFolder, "modes.json");
            Reload();
            ToolDefinitions.InvalidateToolDefinitionsCache();
        }

        public static void Reload()
        {
            LoadFromDisk();
            RebuildResolved();
        }

        public static string GetModesFilePath()
        {
            Initialize();
            return _modesFilePath;
        }

        /// <summary>
        /// Returns modes for UI display: built-in modes (Agent, Plan, Debug) first,
        /// then custom modes in their definition order.
        /// </summary>
        public static List<ModeDefinition> GetOrderedForDisplay()
        {
            Initialize();
            return BuildOrderedDisplayListFromRegistry();
        }

        /// <summary>
        /// Orders an arbitrary mode list for display (built-ins first, then custom).
        /// Used by the options page while editing unsaved local state.
        /// </summary>
        public static List<ModeDefinition> OrderForDisplay(IEnumerable<ModeDefinition> modes)
        {
            if (modes == null)
                return new List<ModeDefinition>();
            return BuildOrderedDisplayList(modes);
        }

        private static List<ModeDefinition> BuildOrderedDisplayList(IEnumerable<ModeDefinition> source)
        {
            var builtInById = new Dictionary<string, ModeDefinition>(StringComparer.OrdinalIgnoreCase);
            var customModes = new List<ModeDefinition>();

            foreach (ModeDefinition mode in source)
            {
                if (mode == null)
                    continue;
                if (mode.IsBuiltIn)
                    builtInById[mode.Id] = mode;
                else
                    customModes.Add(mode);
            }

            var ordered = new List<ModeDefinition>();
            foreach (string id in ModeIds.BuiltInOrder)
            {
                ModeDefinition builtIn;
                if (builtInById.TryGetValue(id, out builtIn))
                    ordered.Add(builtIn.Clone());
            }

            foreach (ModeDefinition custom in customModes)
                ordered.Add(custom.Clone());

            return ordered;
        }

        private static List<ModeDefinition> BuildOrderedDisplayListFromRegistry()
        {
            var ordered = new List<ModeDefinition>();

            foreach (string id in ModeIds.BuiltInOrder)
            {
                ModeDefinition mode;
                if (_resolved.TryGetValue(id, out mode))
                    ordered.Add(mode.Clone());
            }

            foreach (ModeDefinition custom in _customModes)
            {
                ModeDefinition mode;
                if (_resolved.TryGetValue(custom.Id, out mode))
                    ordered.Add(mode.Clone());
            }

            return ordered;
        }

        public static ModeDefinition Get(string id)
        {
            Initialize();
            if (string.IsNullOrEmpty(id))
                id = ModeIds.Agent;

            ModeDefinition mode;
            if (_resolved.TryGetValue(id, out mode))
                return mode.Clone();

            ModeDefinition fallback;
            if (_resolved.TryGetValue(ModeIds.Agent, out fallback))
                return fallback.Clone();

            return ModeDefaults.CreateBuiltInDefault(ModeIds.Agent);
        }

        public static string GetSystemPrompt(string id)
        {
            ModeDefinition mode = Get(id);
            if (!string.IsNullOrWhiteSpace(mode.SystemPrompt))
                return mode.SystemPrompt.Trim();

            return ModeDefaults.GetDefaultSystemPrompt(mode.Id);
        }

        public static bool IsToolEnabled(string modeId, string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return false;

            if (ConfigHandler.IsToolDisabled(toolName))
                return false;

            ModeDefinition mode = Get(modeId);
            if (mode.ToolPolicy == ModeToolPolicy.All)
                return true;

            if (mode.Tools == null || mode.Tools.Length == 0)
                return false;

            foreach (string allowed in mode.Tools)
            {
                if (string.Equals(allowed, toolName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns editable storage snapshot for the options UI (overrides + custom modes only).
        /// </summary>
        public static void GetEditableSnapshot(
            out Dictionary<string, ModeDefinition> builtInOverrides,
            out List<ModeDefinition> customModes)
        {
            Initialize();
            builtInOverrides = new Dictionary<string, ModeDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _builtInOverrides)
                builtInOverrides[kvp.Key] = kvp.Value.Clone();

            customModes = new List<ModeDefinition>();
            foreach (ModeDefinition mode in _customModes)
                customModes.Add(mode.Clone());
        }

        public static void Save(
            Dictionary<string, ModeDefinition> builtInOverrides,
            List<ModeDefinition> customModes)
        {
            Initialize();
            ValidateForSave(builtInOverrides, customModes);

            _builtInOverrides = CloneOverrideMap(builtInOverrides);
            _customModes = CloneCustomList(customModes);

            WriteToDisk();
            RebuildResolved();
            ToolDefinitions.InvalidateToolDefinitionsCache();
            RaiseModesChanged();
        }

        public static bool IsValidCustomModeId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;
            if (!Regex.IsMatch(id, "^[a-z][a-z0-9_-]*$"))
                return false;
            if (IsBuiltInId(id))
                return false;
            return true;
        }

        public static bool IsBuiltInId(string id)
        {
            foreach (string builtIn in ModeIds.BuiltInOrder)
            {
                if (string.Equals(builtIn, id, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static string GenerateCustomModeId(IEnumerable<ModeDefinition> existing)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existing != null)
            {
                foreach (ModeDefinition mode in existing)
                {
                    if (!string.IsNullOrEmpty(mode.Id))
                        used.Add(mode.Id);
                }
            }

            int n = 1;
            while (true)
            {
                string candidate = "custom_" + n;
                if (!used.Contains(candidate) && !IsBuiltInId(candidate))
                    return candidate;
                n++;
            }
        }

        public static bool HasBuiltInOverride(string id)
        {
            Initialize();
            return _builtInOverrides.ContainsKey(id);
        }

        public static ModeDefinition GetBuiltInResolved(string id)
        {
            Initialize();
            ModeDefinition mode;
            if (_resolved.TryGetValue(id, out mode) && mode.IsBuiltIn)
                return mode.Clone();
            return ModeDefaults.CreateBuiltInDefault(id);
        }

        private static void LoadFromDisk()
        {
            _builtInOverrides = new Dictionary<string, ModeDefinition>(StringComparer.OrdinalIgnoreCase);
            _customModes = new List<ModeDefinition>();

            if (!File.Exists(_modesFilePath))
                return;

            try
            {
                string json = File.ReadAllText(_modesFilePath);
                JObject root = JObject.Parse(json);

                JObject overridesObj = root["builtInOverrides"] as JObject;
                if (overridesObj != null)
                {
                    foreach (JProperty prop in overridesObj.Properties())
                    {
                        ModeDefinition parsed = ParseModeEntry(prop.Name, prop.Value as JObject, isBuiltIn: true, idForDefaults: prop.Name);
                        if (parsed != null)
                            _builtInOverrides[prop.Name] = parsed;
                    }
                }

                JArray customArray = root["customModes"] as JArray;
                if (customArray != null)
                {
                    foreach (JToken token in customArray)
                    {
                        JObject obj = token as JObject;
                        if (obj == null) continue;
                        string id = JsonString(obj, "id");
                        ModeDefinition parsed = ParseModeEntry(id, obj, isBuiltIn: false, idForDefaults: id);
                        if (parsed != null && IsValidCustomModeId(parsed.Id))
                            _customModes.Add(parsed);
                    }
                }
            }
            catch
            {
                _builtInOverrides = new Dictionary<string, ModeDefinition>(StringComparer.OrdinalIgnoreCase);
                _customModes = new List<ModeDefinition>();
            }
        }

        private static ModeDefinition ParseModeEntry(string id, JObject obj, bool isBuiltIn, string idForDefaults)
        {
            if (obj == null || string.IsNullOrEmpty(id))
                return null;

            ModeToolPolicy toolPolicy = obj["toolPolicy"] != null
                ? ParseToolPolicy(JsonString(obj, "toolPolicy"))
                : ModeDefaults.GetDefaultToolPolicy(idForDefaults);

            string[] tools = obj["tools"] != null
                ? ParseToolsArray(obj["tools"])
                : ModeDefaults.GetDefaultTools(idForDefaults);

            return new ModeDefinition
            {
                Id = id,
                DisplayName = JsonString(obj, "displayName"),
                SystemPrompt = JsonString(obj, "systemPrompt"),
                ToolPolicy = toolPolicy,
                Tools = tools,
                IsBuiltIn = isBuiltIn
            };
        }

        private static void WriteToDisk()
        {
            try
            {
                var root = new JObject();
                var overridesObj = new JObject();
                foreach (var kvp in _builtInOverrides)
                    overridesObj[kvp.Key] = SerializeModeEntry(kvp.Value, includeId: false);
                root["builtInOverrides"] = overridesObj;

                var customArray = new JArray();
                foreach (ModeDefinition mode in _customModes)
                    customArray.Add(SerializeModeEntry(mode, includeId: true));
                root["customModes"] = customArray;

                File.WriteAllText(_modesFilePath, root.ToString(Formatting.Indented));
            }
            catch { }
        }

        private static JObject SerializeModeEntry(ModeDefinition mode, bool includeId)
        {
            var obj = new JObject();
            if (includeId)
                obj["id"] = mode.Id ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(mode.DisplayName))
                obj["displayName"] = mode.DisplayName;
            if (!string.IsNullOrWhiteSpace(mode.SystemPrompt))
                obj["systemPrompt"] = mode.SystemPrompt;
            obj["toolPolicy"] = mode.ToolPolicy == ModeToolPolicy.AllowList ? "allowlist" : "all";
            if (mode.Tools != null && mode.Tools.Length > 0)
            {
                var tools = new JArray();
                foreach (string tool in mode.Tools)
                    tools.Add(tool);
                obj["tools"] = tools;
            }
            return obj;
        }

        private static void RebuildResolved()
        {
            _resolved.Clear();

            foreach (string id in ModeIds.BuiltInOrder)
                _resolved[id] = MergeBuiltIn(id);

            foreach (ModeDefinition custom in _customModes)
            {
                ModeDefinition merged = MergeCustom(custom);
                _resolved[merged.Id] = merged;
            }
        }

        private static ModeDefinition MergeBuiltIn(string id)
        {
            ModeDefinition defaults = ModeDefaults.CreateBuiltInDefault(id);
            ModeDefinition overrideDef;
            if (!_builtInOverrides.TryGetValue(id, out overrideDef))
                return defaults;

            return new ModeDefinition
            {
                Id = id,
                DisplayName = defaults.DisplayName,
                SystemPrompt = overrideDef.SystemPrompt ?? string.Empty,
                ToolPolicy = overrideDef.ToolPolicy,
                Tools = overrideDef.ToolPolicy == ModeToolPolicy.AllowList
                    ? GetEffectiveAllowList(overrideDef.Tools, defaults.Tools)
                    : new string[0],
                IsBuiltIn = true
            };
        }

        private static string[] GetEffectiveAllowList(string[] overrideTools, string[] defaultTools)
        {
            if (overrideTools != null && overrideTools.Length > 0)
                return (string[])overrideTools.Clone();
            if (defaultTools != null && defaultTools.Length > 0)
                return (string[])defaultTools.Clone();
            return new string[0];
        }

        private static ModeDefinition MergeCustom(ModeDefinition custom)
        {
            return new ModeDefinition
            {
                Id = custom.Id,
                DisplayName = string.IsNullOrWhiteSpace(custom.DisplayName)
                    ? custom.Id
                    : custom.DisplayName.Trim(),
                SystemPrompt = custom.SystemPrompt ?? string.Empty,
                ToolPolicy = custom.ToolPolicy,
                Tools = custom.Tools != null ? (string[])custom.Tools.Clone() : new string[0],
                IsBuiltIn = false
            };
        }

        private static void ValidateForSave(
            Dictionary<string, ModeDefinition> builtInOverrides,
            List<ModeDefinition> customModes)
        {
            if (builtInOverrides == null)
                builtInOverrides = new Dictionary<string, ModeDefinition>(StringComparer.OrdinalIgnoreCase);

            if (customModes == null)
                customModes = new List<ModeDefinition>();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModeDefinition mode in customModes)
            {
                if (mode == null || string.IsNullOrWhiteSpace(mode.Id))
                    throw new InvalidOperationException("Custom modes must have an id.");

                string id = mode.Id.Trim();
                if (!IsValidCustomModeId(id))
                    throw new InvalidOperationException("Invalid custom mode id: " + id);

                if (!seen.Add(id))
                    throw new InvalidOperationException("Duplicate custom mode id: " + id);

                if (string.IsNullOrWhiteSpace(mode.SystemPrompt))
                    throw new InvalidOperationException("Custom mode '" + id + "' requires a system prompt.");
            }

            foreach (string key in builtInOverrides.Keys)
            {
                if (!IsBuiltInId(key))
                    throw new InvalidOperationException("Unknown built-in mode override: " + key);
            }
        }

        private static Dictionary<string, ModeDefinition> CloneOverrideMap(Dictionary<string, ModeDefinition> source)
        {
            var copy = new Dictionary<string, ModeDefinition>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return copy;
            foreach (var kvp in source)
                copy[kvp.Key] = kvp.Value != null ? kvp.Value.Clone() : new ModeDefinition { Id = kvp.Key, IsBuiltIn = true };
            return copy;
        }

        private static List<ModeDefinition> CloneCustomList(List<ModeDefinition> source)
        {
            var copy = new List<ModeDefinition>();
            if (source == null) return copy;
            foreach (ModeDefinition mode in source)
                copy.Add(mode != null ? mode.Clone() : new ModeDefinition());
            return copy;
        }

        private static ModeToolPolicy ParseToolPolicy(string raw)
        {
            if (!string.IsNullOrEmpty(raw) &&
                string.Equals(raw.Trim(), "allowlist", StringComparison.OrdinalIgnoreCase))
                return ModeToolPolicy.AllowList;
            return ModeToolPolicy.All;
        }

        private static string[] ParseToolsArray(JToken token)
        {
            var list = new List<string>();
            JArray array = token as JArray;
            if (array == null) return list.ToArray();
            foreach (JToken item in array)
            {
                if (item == null) continue;
                string value = item.ToString().Trim();
                if (!string.IsNullOrEmpty(value))
                    list.Add(value);
            }
            return list.ToArray();
        }

        private static string JsonString(JObject obj, string key)
        {
            if (obj == null) return string.Empty;
            JToken token = obj[key];
            return token != null ? token.ToString() : string.Empty;
        }

        private static void RaiseModesChanged()
        {
            Action handler = ModesChanged;
            if (handler != null) handler();
        }
    }
}
