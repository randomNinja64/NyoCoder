using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NyoCoder
{
	public class ConfigHandler
	{
		private static Dictionary<string, string> configMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private static string configFilePath;

		// Cached typed values — kept in sync by setters so callers pay no parsing cost
		private static string _apiKey;
		private static string _llmServer;
		private static string _model;
		private static int _maxReadLines = 500;
		private static int? _contextWindowSize;

		// -------------------------------------------------------------------------
		// Init / Load / Save
		// -------------------------------------------------------------------------

		public static void Initialize()
		{
			if (configFilePath != null)
				return;

			string configFolder = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				"NyoCoder");

			if (!Directory.Exists(configFolder))
				Directory.CreateDirectory(configFolder);

			configFilePath = Path.Combine(configFolder, "NyoCoder.ini");
			LoadFromDisk(createIfMissing: true);
		}

		/// <summary>
		/// Reloads config from disk. Call after an external change to the INI file.
		/// </summary>
		public static void ReloadConfig()
		{
			LoadFromDisk(createIfMissing: false);
		}

		private static void LoadFromDisk(bool createIfMissing)
		{
			configMap = LoadIni(configFilePath);

			if (createIfMissing && configMap.Count == 0 && !File.Exists(configFilePath))
				SaveConfig();

			RefreshCachedValues();
		}

		public static void SaveConfig()
		{
			SaveIni(configFilePath, configMap);
		}

		// -------------------------------------------------------------------------
		// Generic accessors (public — used by ExternalToolRegistry and tools)
		// -------------------------------------------------------------------------

		public static string GetConfigValue(string key, string defaultValue = "")
		{
			string value;
			return configMap.TryGetValue(key, out value) ? value : defaultValue;
		}

		public static void SetConfigValue(string key, string value)
		{
			if (string.IsNullOrEmpty(value))
				configMap.Remove(key);
			else
				configMap[key] = value;
		}

		public static int GetConfigInt(string key, int defaultValue)
		{
			string raw = GetConfigValue(key);
			int result;
			return (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out result)) ? result : defaultValue;
		}

		public static List<string> GetConfigList(string key)
		{
			var list = new List<string>();
			string raw = GetConfigValue(key);
			if (string.IsNullOrEmpty(raw))
				return list;
			foreach (string token in raw.Split(','))
			{
				string trimmed = token.Trim();
				if (!string.IsNullOrEmpty(trimmed))
					list.Add(trimmed);
			}
			return list;
		}

		/// <summary>
		/// Returns all config key/value pairs. Used by ExternalToolRegistry to pass
		/// config to external tool processes via stdin.
		/// </summary>
		public static IEnumerable<KeyValuePair<string, string>> GetAllValues()
		{
			return configMap;
		}

		// -------------------------------------------------------------------------
		// Typed property getters/setters
		// -------------------------------------------------------------------------

		public static string GetApiKey()    { return _apiKey    ?? string.Empty; }
		public static string GetLlmServer() { return _llmServer ?? string.Empty; }
		public static string GetModel()     { return _model     ?? string.Empty; }

		public static int MaxReadLines
		{
			get { return _maxReadLines; }
		}

		public static int? ContextWindowSize
		{
			get { return _contextWindowSize; }
		}

		public static void SetApiKey(string value)
		{
			_apiKey = value ?? string.Empty;
			SetConfigValue("apiKey", value);
		}

		public static void SetLlmServer(string value)
		{
			_llmServer = value ?? string.Empty;
			SetConfigValue("llmserver", value);
		}

		public static void SetModel(string value)
		{
			_model = value ?? string.Empty;
			SetConfigValue("model", value);
		}

		public static void SetMaxReadLines(int value)
		{
			if (value <= 0) return;
			_maxReadLines = value;
			SetConfigValue("maxReadLines", value.ToString());
		}

		public static void SetContextWindowSize(int? value)
		{
			_contextWindowSize = (value.HasValue && value.Value > 0) ? value : (int?)null;
			SetConfigValue("contextWindowSize", _contextWindowSize.HasValue ? _contextWindowSize.Value.ToString() : null);
		}

	// -------------------------------------------------------------------------
	// Tool enable/disable
	// -------------------------------------------------------------------------

	public static List<string> GetDisabledTools()
	{
		return GetConfigList("disabledTools");
	}

	// -------------------------------------------------------------------------
	// Tool approval
	// -------------------------------------------------------------------------

	private static readonly List<string> _defaultToolsRequiringApproval = new List<string>
	{
		"run_shell_command",
		"move_file",
		"delete_file",
		"copy_file",
		"write_file",
		"search_replace"
	};

	/// <summary>
	/// Returns true when the named tool should prompt for approval according to config.
	/// </summary>
	public static bool ToolRequiresApproval(string toolName)
	{
		if (string.IsNullOrEmpty(toolName)) return false;
		foreach (string t in GetToolsRequiringApproval())
		{
			if (string.Equals(t, toolName, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Returns true when the tool should show the generic pre-execution approval prompt.
	/// </summary>
	public static bool RequiresApprovalBeforeExecute(string toolName)
	{
		if (!ToolRequiresApproval(toolName))
			return false;
		return !ToolDefinitions.UsesAfterPreviewApproval(toolName);
	}

	/// <summary>
	/// Returns true when the tool should prompt after building a diff preview.
	/// </summary>
	public static bool RequiresApprovalAfterPreview(string toolName)
	{
		return ToolRequiresApproval(toolName)
			&& ToolDefinitions.UsesAfterPreviewApproval(toolName);
	}

	/// <summary>
	/// Returns the list of tool names that require user approval.
	/// Falls back to the built-in defaults when the key is absent from config.
	/// Approval timing (before execute vs after preview) is defined for built-in tools in <see cref="ToolDefinitions"/>.
	/// Returns an empty list when the key is explicitly set to "null" (user turned off all approvals).
	/// </summary>
	public static List<string> GetToolsRequiringApproval()
	{
		string raw = GetConfigValue("toolsRequiringApproval");
		if (string.IsNullOrEmpty(raw))
			return new List<string>(_defaultToolsRequiringApproval);
		if (string.Equals(raw, "null", StringComparison.OrdinalIgnoreCase))
			return new List<string>();
		return GetConfigList("toolsRequiringApproval");
	}

	/// <summary>
	/// Persists the approval list to the INI file.
	/// Writes "null" when the list is empty so the key is preserved and defaults are not restored on reload.
	/// </summary>
	public static void SetToolsRequiringApproval(List<string> tools)
	{
		string value = (tools != null && tools.Count > 0)
			? string.Join(",", tools.ToArray())
			: "null";
		configMap["toolsRequiringApproval"] = value;
	}

		public static bool IsToolDisabled(string toolName)
		{
			if (string.IsNullOrEmpty(toolName)) return false;
			foreach (string t in GetConfigList("disabledTools"))
				if (string.Equals(t, toolName, StringComparison.OrdinalIgnoreCase))
					return true;
			return false;
		}

		public static void SetDisabledTools(List<string> tools)
		{
			SetConfigValue("disabledTools",
				tools != null && tools.Count > 0 ? string.Join(",", tools.ToArray()) : null);
			ToolDefinitions._toolDefinitionsLength = -1;
		}

		// -------------------------------------------------------------------------
		// INI read/write
		// -------------------------------------------------------------------------

		private static void RefreshCachedValues()
		{
			_apiKey    = GetConfigValue("apiKey");
			_llmServer = GetConfigValue("llmserver");
			_model     = GetConfigValue("model");
			_maxReadLines      = GetConfigInt("maxReadLines", 500);
			int cws            = GetConfigInt("contextWindowSize", 0);
			_contextWindowSize = cws > 0 ? (int?)cws : null;
		}

		private static Dictionary<string, string> LoadIni(string filename)
		{
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			if (!File.Exists(filename))
				return result;

			try
			{
				using (StreamReader reader = new StreamReader(filename, Encoding.UTF8))
				{
					string line;
					while ((line = reader.ReadLine()) != null)
					{
						line = line.Trim();
						if (string.IsNullOrEmpty(line) || line[0] == ';' || line[0] == '#')
							continue;
						int eq = line.IndexOf('=');
						if (eq > 0)
							result[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
					}
				}
			}
			catch { }

			return result;
		}

		private static void SaveIni(string filename, Dictionary<string, string> config)
		{
			try
			{
				using (StreamWriter writer = new StreamWriter(filename, false, Encoding.UTF8))
				{
					foreach (var kvp in config)
						writer.WriteLine("{0}={1}", kvp.Key, kvp.Value);
				}
			}
			catch { }
		}
	}
}

