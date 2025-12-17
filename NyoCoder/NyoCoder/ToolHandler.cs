using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Text;

namespace NyoCoder
{
public static class ToolHandler
{
    public struct ToolCall
    {
        public string Id;
        public string Name;
        public string Arguments;

        public ToolCall(string name, string arguments, string id = "")
        {
            Id = id;
            Name = name;
            Arguments = arguments;
        }
    }

    public static bool ExecuteToolCall(ToolCall call, out string toolContent, out int exitCode)
    {
        toolContent = "";
        exitCode = 0;

        try
        {
            switch (call.Name)
            {
                case "run_shell_command":
                    {
                        string command = GetRequiredArg(call.Arguments, "command");
                        string output = RunShellCommand(command, out exitCode);
                        toolContent = FormatCommandResult(command, output, exitCode);
                        return true;
                    }

                case "read_file":
                    {
                        string filename = GetRequiredArg(call.Arguments, "filename");
                        string offsetStr = JsonExtractString(call.Arguments, "offset");
                        int offset = 0;
                        if (!string.IsNullOrEmpty(offsetStr))
                        {
                            int.TryParse(offsetStr.Trim(), out offset);
                        }
                        string output = FileHandler.ReadFile(filename, out exitCode, offset);
                        toolContent = FormatCommandResult("read file: " + filename, output, exitCode);
                        return true;
                    }

                case "write_file":
                    {
                        string filename = GetRequiredArg(call.Arguments, "filename");
                        string contentStr = JsonExtractString(call.Arguments, "content");
                        string content = string.IsNullOrEmpty(contentStr) ? "" : contentStr.Trim();
                        string output = FileHandler.WriteFile(filename, content, out exitCode);
                        toolContent = FormatCommandResult("write file: " + filename, output, exitCode);
                        return true;
                    }

                case "move_file":
                    {
                        string sourcePath = GetRequiredArg(call.Arguments, "source_path");
                        string destinationPath = GetRequiredArg(call.Arguments, "destination_path");
                        string output = FileHandler.MoveFile(sourcePath, destinationPath, out exitCode);
                        toolContent = FormatCommandResult("move file: " + sourcePath, output, exitCode);
                        return true;
                    }

                case "copy_file":
                    {
                        string sourcePath = GetRequiredArg(call.Arguments, "source_path");
                        string destinationPath = GetRequiredArg(call.Arguments, "destination_path");
                        string output = FileHandler.CopyFile(sourcePath, destinationPath, out exitCode);
                        toolContent = FormatCommandResult("copy file: " + sourcePath, output, exitCode);
                        return true;
                    }

                case "delete_file":
                    {
                        string filePath = GetRequiredArg(call.Arguments, "file_path");
                        string output = FileHandler.DeleteFile(filePath, out exitCode);
                        toolContent = FormatCommandResult("delete file: " + filePath, output, exitCode);
                        return true;
                    }

                case "list_directory":
                    {
                        string directoryPath = GetRequiredArg(call.Arguments, "directory_path");
                        string output = FileHandler.ListDirectory(directoryPath, out exitCode);
                        toolContent = FormatCommandResult("list directory: " + directoryPath, output, exitCode);
                        return true;
                    }

                default:
                    toolContent = "error: unknown tool '" + call.Name + "'.";
                    return false;
            }
        }
        catch (Exception e)
        {
            toolContent = "error: " + e.Message;
            return true;
        }
    }

    private static string GetRequiredArg(string arguments, string argName)
    {
        string valueStr = JsonExtractString(arguments, argName);
        string value = string.IsNullOrEmpty(valueStr) ? "" : valueStr.Trim();
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("missing '" + argName + "' argument.");
        return value;
    }

    // Generic process execution helper (public for use by other handlers)
    public static string ExecuteProcess(string fileName, string arguments, out int exitCode, bool combineErrorOutput = true)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (Process process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
                
                // Combine stdout and stderr if requested
                if (combineErrorOutput && !string.IsNullOrEmpty(error))
                {
                    return output + error;
                }
                return output;
            }
        }
        catch (Exception ex)
        {
            exitCode = -1;
            throw new InvalidOperationException("Failed to execute " + fileName + ": " + ex.Message, ex);
        }
    }

    private static string JsonExtractString(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
        {
            return "";
        }

        try
        {
            string trimmedJson = json.Trim();
            if (trimmedJson.Length == 0)
            {
                return "";
            }

            JToken root = JToken.Parse(trimmedJson);
            if (root.Type == JTokenType.Object)
            {
                JObject obj = (JObject)root;
                JToken token;
                if (!obj.TryGetValue(key, out token))
                {
                    foreach (JProperty property in obj.Properties())
                    {
                        if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                        {
                            token = property.Value;
                            break;
                        }
                    }
                }

                if (token != null && token.Type != JTokenType.Null)
                {
                    return token.Type == JTokenType.String ? token.Value<string>() ?? "" : token.ToString();
                }
            }
        }
        catch
        {
        }

        return "";
    }

    // Runs shell commands on the OS
    private static string RunShellCommand(string command, out int exitCode)
    {
        return ExecuteProcess("cmd.exe", "/c " + command, out exitCode);
    }

    public static string FormatCommandResult(string command, string output, int exitCode)
    {
        return "Exit Code: " + exitCode + "\nOutput:\n" + output;
    }
}
}
