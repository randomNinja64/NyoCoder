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
		
		// Static cached values - updated when config loads/saves
		private static string _apiKey;
		private static string _llmServer;
		private static string _model;
		private static int _maxReadLines = 500; // default (line-based reading)
		private static int? _contextWindowSize;

		/// <summary>
		/// Initializes and loads config from disk.
		/// Call this during extension startup to preload configuration.
		/// </summary>
		public static void Initialize()
		{
			if (configFilePath != null)
				return;

			// For VSIX, store config in user's AppData folder
			string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			string configFolder = Path.Combine(appDataFolder, "NyoCoder");
			if (!Directory.Exists(configFolder))
			{
				Directory.CreateDirectory(configFolder);
			}
			configFilePath = Path.Combine(configFolder, "NyoCoder.ini");
			LoadConfig();
		}

		private static void LoadConfig()
		{
			configMap = LoadIni(configFilePath);
			
			if (configMap.Count == 0 && File.Exists(configFilePath))
			{
				// File exists but is empty or has no valid entries
				// This is not necessarily an error, so we don't log it
			}
			else if (configMap.Count == 0)
			{
				// File doesn't exist - create it with default empty values
				SaveConfig();
			}
			
			// Cache values in static fields
			RefreshCachedValues();
		}

		/// <summary>
		/// Reloads config from disk and refreshes cached values.
		/// Call this after saving config to ensure all instances see the latest values.
		/// </summary>
		public static void ReloadConfig()
		{
			configMap = LoadIni(configFilePath);

			if (configMap.Count == 0 && !File.Exists(configFilePath))
				SaveConfig();

			RefreshCachedValues();
		}

		private static void RefreshCachedValues()
		{
			_apiKey = GetConfigValue("apiKey");
			_llmServer = GetConfigValue("llmserver");
			_model = GetConfigValue("model");
			
			string maxReadLinesStr = GetConfigValue("maxReadLines", "");
			int parsed;
			if (int.TryParse(maxReadLinesStr, out parsed) && parsed > 0)
				_maxReadLines = parsed;
			
			string contextWindowSizeStr = GetConfigValue("contextWindowSize", "");
			if (string.IsNullOrEmpty(contextWindowSizeStr))
			{
				_contextWindowSize = null;
			}
			else
			{
				int contextWindowSize;
				if (int.TryParse(contextWindowSizeStr, out contextWindowSize) && contextWindowSize > 0)
				{
					_contextWindowSize = contextWindowSize;
				}
				else
				{
					_contextWindowSize = null;
				}
			}
		}

		public static void SaveConfig()
		{
			SaveIni(configFilePath, configMap);
			// Refresh cache after saving to ensure consistency
			RefreshCachedValues();
		}

		// Generic helper methods to reduce repetition
		private static string GetConfigValue(string key, string defaultValue = "")
		{
			return configMap.ContainsKey(key) ? configMap[key] : defaultValue;
		}

		private static void SetConfigValue(string key, string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				if (configMap.ContainsKey(key))
					configMap.Remove(key);
			}
			else
			{
				configMap[key] = value;
			}
		}

		// Public getter methods - use static cached values
		public static string GetApiKey()
		{
			return _apiKey ?? string.Empty;
		}

		public static string GetLlmServer()
		{
			return _llmServer ?? string.Empty;
		}

		public static string GetModel()
		{
			return _model ?? string.Empty;
		}

		// Static accessor for MaxReadLines - used by tools and options
		public static int MaxReadLines
		{
			get { return _maxReadLines; }
		}

		// Static accessor for ContextWindowSize - used by UI token display
		public static int? ContextWindowSize
		{
			get { return _contextWindowSize; }
		}

	// Public setter methods - update both static cache and configMap
	// Note: Setters are only called from UI thread, so no locking needed
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
		if (value > 0)
		{
			_maxReadLines = value;
			SetConfigValue("maxReadLines", value.ToString());
		}
	}

	public static void SetContextWindowSize(int? value)
	{
		if (value.HasValue && value.Value > 0)
		{
			_contextWindowSize = value;
			SetConfigValue("contextWindowSize", value.Value.ToString());
		}
		else
		{
			_contextWindowSize = null;
			SetConfigValue("contextWindowSize", null);
		}
	}

		// Simple INI file loader - returns Dictionary of key=value pairs
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
						
						// Skip empty lines and comments
						if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#"))
							continue;

						// Parse key=value pairs
						int equalsIndex = line.IndexOf('=');
						if (equalsIndex > 0)
						{
							string key = line.Substring(0, equalsIndex).Trim();
							string value = line.Substring(equalsIndex + 1).Trim();
							result[key] = value;
						}
					}
				}
			}
			catch
			{
				// If we can't read the file, return empty dictionary
			}

			return result;
		}

		// Simple INI file saver
		private static void SaveIni(string filename, Dictionary<string, string> config)
		{
			try
			{
				using (StreamWriter writer = new StreamWriter(filename, false, Encoding.UTF8))
				{
					// Write each key-value pair
					foreach (var kvp in config)
					{
						writer.WriteLine("{0}={1}", kvp.Key, kvp.Value);
					}
				}
			}
			catch
			{
				// If we can't write the file, silently fail
			}
		}
	}
}
