using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;
using System.IO;

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
            // Parse arguments once; all cases reuse this object.
            JObject args = ParseArguments(call.Arguments);

            switch (call.Name)
            {
                case "run_shell_command":
                    {
                        string command = GetRequiredArg(args, "command");
                        string output = RunShellCommand(command, out exitCode);
                        toolContent = FormatCommandResult(command, output, exitCode);
                        return true;
                    }

                case "read_file":
                    {
                        string filename = GetRequiredArg(args, "filename");
                        string offsetStr = JsonExtractString(args, "offset");
                        int lineOffset = 0;
                        if (!string.IsNullOrEmpty(offsetStr))
                            int.TryParse(offsetStr.Trim(), out lineOffset);
                        string output = FileHandler.ReadFile(filename, out exitCode, lineOffset);
                        toolContent = FormatCommandResult("read file: " + filename, output, exitCode);
                        return true;
                    }

                case "write_file":
                    {
                        string filename = GetRequiredArg(args, "filename");
                        string contentStr = JsonExtractString(args, "content");
                        string content = string.IsNullOrEmpty(contentStr) ? "" : contentStr.Trim();
                        string output = WriteFileTool.Write(filename, content, out exitCode);
                        if (exitCode == 0)
                            CodebaseIndexer.RequestIndexFile(filename);
                        toolContent = FormatCommandResult("write file: " + filename, output, exitCode);
                        return true;
                    }

                case "move_file":
                    {
                        string sourcePath = GetRequiredArg(args, "source_path");
                        string destinationPath = GetRequiredArg(args, "destination_path");
                        string output = FileHandler.MoveFile(sourcePath, destinationPath, out exitCode);
                        if (exitCode == 0)
                            CodebaseIndexer.RequestRenameFile(sourcePath, destinationPath);
                        toolContent = FormatCommandResult("move file: " + sourcePath, output, exitCode);
                        return true;
                    }

                case "copy_file":
                    {
                        string sourcePath = GetRequiredArg(args, "source_path");
                        string destinationPath = GetRequiredArg(args, "destination_path");
                        string output = FileHandler.CopyFile(sourcePath, destinationPath, out exitCode);
                        if (exitCode == 0)
                            CodebaseIndexer.RequestIndexFile(destinationPath);
                        toolContent = FormatCommandResult("copy file: " + sourcePath, output, exitCode);
                        return true;
                    }

                case "delete_file":
                    {
                        string filePath = GetRequiredArg(args, "file_path");
                        string output = FileHandler.DeleteFile(filePath, out exitCode);
                        if (exitCode == 0)
                            CodebaseIndexer.RequestRemoveFile(filePath);
                        toolContent = FormatCommandResult("delete file: " + filePath, output, exitCode);
                        return true;
                    }

                case "list_directory":
                    {
                        string directoryPath = GetRequiredArg(args, "directory_path");
                        string output = FileHandler.ListDirectory(directoryPath, out exitCode);
                        toolContent = FormatCommandResult("list directory: " + directoryPath, output, exitCode);
                        return true;
                    }

                case "grep_search":
                    {
                        string pattern = GetRequiredArg(args, "pattern");
                        string directoryPath = JsonExtractString(args, "directory_path");
                        string filePattern = JsonExtractString(args, "file_pattern");
                        string caseInsensitive = JsonExtractString(args, "case_insensitive");
                        string output = GrepSearchTool.Search(pattern, directoryPath, filePattern, caseInsensitive, out exitCode);
                        string searchDesc = "grep '" + pattern + "'" + (string.IsNullOrEmpty(directoryPath) ? "" : " in " + directoryPath);
                        toolContent = FormatCommandResult(searchDesc, output, exitCode);
                        return true;
                    }

                case "codebase_search":
                    {
                        string query = GetRequiredArg(args, "query");
                        string output = CodebaseSearchTool.Search(query, out exitCode);
                        toolContent = FormatCommandResult("codebase_search: " + query, output, exitCode);
                        return true;
                    }

                case "search_replace":
                    {
                        string filePath = GetRequiredArg(args, "file_path");
                        string content = GetRequiredArg(args, "content");
                        string output = SearchReplaceHandler.Apply(filePath, content, out exitCode);
                        if (exitCode == 0)
                            CodebaseIndexer.RequestIndexFile(filePath);
                        toolContent = FormatCommandResult("search_replace: " + filePath, output, exitCode);
                        return true;
                    }

                case "run_web_search":
                    {
                        string query = GetRequiredArg(args, "query");
                        string searxngInstance = ConfigHandler.GetConfigValue("searxngInstance");
                        int maxSearchResults = ConfigHandler.GetConfigInt("maxSearchResults", 20);
                        string output = WebSearchTool.RunWebSearch(query, searxngInstance, maxSearchResults, out exitCode);
                        toolContent = FormatCommandResult("web search: " + query, output, exitCode);
                        return true;
                    }

                case "read_website":
                    {
                        string url = GetRequiredArg(args, "url");
                        int maxContentLength = ConfigHandler.GetConfigInt("maxWebContentLength", 10000);
                        int maxLinks = ConfigHandler.GetConfigInt("maxLinks", 40);
                        string output = WebSearchTool.ReadWebsite(url, maxContentLength, maxLinks, out exitCode);
                        toolContent = FormatCommandResult("read website: " + url, output, exitCode);
                        return true;
                    }

                case "view_skill":
                    {
                        string name = GetRequiredArg(args, "name");
                        string relativePath = JsonExtractString(args, "relative_path");
                        string output = SkillHandler.ViewSkill(name, relativePath);
                        toolContent = FormatCommandResult("view_skill: " + name, output, exitCode);
                        return true;
                    }

                case "create_skill":
                    {
                        string name = GetRequiredArg(args, "name");
                        string description = GetRequiredArg(args, "description");
                        string instructions = GetRequiredArg(args, "instructions");
                        string output = SkillHandler.CreateSkill(name, description, instructions);
                        toolContent = FormatCommandResult("create_skill: " + name, output, exitCode);
                        return true;
                    }

                case "edit_skill":
                    {
                        string name = GetRequiredArg(args, "name");
                        string description = JsonExtractString(args, "description");
                        string instructions = JsonExtractString(args, "instructions");
                        string output = SkillHandler.EditSkill(name, description, instructions);
                        toolContent = FormatCommandResult("edit_skill: " + name, output, exitCode);
                        return true;
                    }

                case "edit_skill_file":
                    {
                        string name = GetRequiredArg(args, "name");
                        string relativePath = GetRequiredArg(args, "relative_path");
                        string content = JsonExtractString(args, "content");
                        string output = SkillHandler.EditSkillFile(name, relativePath, content);
                        toolContent = FormatCommandResult("edit_skill_file: " + name, output, exitCode);
                        return true;
                    }

                case "remove_skill":
                    {
                        string name = GetRequiredArg(args, "name");
                        string output = SkillHandler.RemoveSkill(name);
                        toolContent = FormatCommandResult("remove_skill: " + name, output, exitCode);
                        return true;
                    }

                case "ask_user_question":
                    {
                        string question = GetRequiredArg(args, "question");
                        JArray optionsArray = args != null ? args["options"] as JArray : null;
                        string output = AskUserQuestionTool.Ask(question, optionsArray, out exitCode);
                        toolContent = FormatCommandResult("ask user: " + question, output, exitCode);
                        return true;
                    }

                case "manage_plan":
                    {
                        string action = GetRequiredArg(args, "action");
                        JArray stepsArray = args != null ? args["steps"] as JArray : null;
                        string output = ManagePlanTool.Execute(action, stepsArray, out exitCode);
                        toolContent = FormatCommandResult("manage_plan (" + action + ")", output, exitCode);
                        return true;
                    }

                default:
                    // Fall through to external tool registry (SimpleLLMChat-compatible packages)
                    if (ExternalToolRegistry.HasTool(call.Name))
                    {
                        ExternalToolRegistry.ExecuteToolCall(call.Name, call.Arguments, out toolContent, out exitCode);
                        return true;
                    }
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

    // Parses a raw JSON string into a JObject once. Returns null on empty/invalid input.
    private static JObject ParseArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return null;
        try
        {
            JToken root = JToken.Parse(arguments.Trim());
            return root as JObject;
        }
        catch { return null; }
    }

    private static string GetRequiredArg(JObject args, string argName)
    {
        string value = JsonExtractString(args, argName);
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("missing '" + argName + "' argument.");
        return value;
    }

    public static string ExecuteProcess(string fileName, string arguments, out int exitCode, bool combineErrorOutput = true, int timeoutMilliseconds = -1, string stdinData = null, string workingDirectory = null)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdinData != null,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrEmpty(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi))
            {
                // Write stdin synchronously before reading; the payload is small so it fits
                // in the pipe buffer without risk of deadlock.
                if (stdinData != null)
                {
                    process.StandardInput.Write(stdinData);
                    process.StandardInput.Close();
                }

                // Read stdout and stderr concurrently to avoid pipe-buffer deadlocks
                // on processes that write large amounts of output.
                string output = "";
                string error = "";

                System.Threading.Thread outThread = new System.Threading.Thread(() => { output = process.StandardOutput.ReadToEnd(); });
                System.Threading.Thread errThread = new System.Threading.Thread(() => { error = process.StandardError.ReadToEnd(); });
                outThread.IsBackground = true;
                errThread.IsBackground = true;
                outThread.Start();
                errThread.Start();

                if (timeoutMilliseconds > 0)
                {
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        try { process.Kill(); } catch { }
                        process.WaitForExit();
                        exitCode = -1;
                        throw new TimeoutException(string.Format("Process '{0}' timed out after {1} seconds.", fileName, timeoutMilliseconds / 1000));
                    }
                }
                else
                {
                    process.WaitForExit();
                }

                outThread.Join();
                errThread.Join();

                exitCode = process.ExitCode;
                if (combineErrorOutput && !string.IsNullOrEmpty(error))
                    return output + error;
                return output;
            }
        }
        catch (Exception ex)
        {
            exitCode = -1;
            throw new InvalidOperationException("Failed to execute " + fileName + ": " + ex.Message, ex);
        }
    }

    // Extracts a string value from a pre-parsed JObject.
    private static string JsonExtractString(JObject obj, string key)
    {
        if (obj == null || string.IsNullOrEmpty(key))
            return "";

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
            return token.Type == JTokenType.String ? token.Value<string>() ?? "" : token.ToString();

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

    // Preview events — raised by SearchReplaceHandler and WriteFileTool via the helpers below.
    public static event Action<string, List<SearchReplaceTool.InlineSpan>> OnDiffChangesPreview;
    public static event Action<string> OnDiffPreviewCleared;

    internal static void RaiseDiffChangesPreview(string filePath, List<SearchReplaceTool.InlineSpan> spans)
    {
        if (OnDiffChangesPreview != null)
            OnDiffChangesPreview(filePath, spans);
    }

    internal static void RaiseDiffPreviewCleared(string filePath)
    {
        if (OnDiffPreviewCleared != null)
            OnDiffPreviewCleared(filePath);
    }
}
}
