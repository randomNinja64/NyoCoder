using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NyoCoder
{
	public class ConfigHandler
	{
		private Dictionary<string, string> configMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private string configFilePath;

		public ConfigHandler()
		{
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

		private void LoadConfig()
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
		}

		public void SaveConfig()
		{
			SaveIni(configFilePath, configMap);
		}

		// Generic helper methods to reduce repetition
		private string GetConfigValue(string key, string defaultValue = "")
		{
			return configMap.ContainsKey(key) ? configMap[key] : defaultValue;
		}

		private void SetConfigValue(string key, string value)
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

		// Public getter methods
		public string GetApiKey()
		{
			return GetConfigValue("apiKey");
		}

		public string GetLlmServer()
		{
			return GetConfigValue("llmserver");
		}

	public string GetModel()
	{
		return GetConfigValue("model");
	}

	// Static accessor for MaxContentLength - used by tools and options
	public static int MaxContentLength
	{
		get
		{
			ConfigHandler config = new ConfigHandler();
			string value = config.GetConfigValue("maxContentLength", "");
			int result;
			return (int.TryParse(value, out result) && result > 0) ? result : 8000;
		}
	}

	// Public setter methods
	public void SetApiKey(string value)
	{
		SetConfigValue("apiKey", value);
	}

	public void SetLlmServer(string value)
	{
		SetConfigValue("llmserver", value);
	}

	public void SetModel(string value)
	{
		SetConfigValue("model", value);
	}

	public void SetMaxContentLength(int value)
	{
		if (value > 0)
		{
			SetConfigValue("maxContentLength", value.ToString());
		}
	}

		// Simple INI file loader - returns Dictionary of key=value pairs
		private Dictionary<string, string> LoadIni(string filename)
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
		private void SaveIni(string filename, Dictionary<string, string> config)
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
