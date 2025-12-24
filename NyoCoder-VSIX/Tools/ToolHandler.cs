using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Reflection;
using EnvDTE;
using NyoCoder;

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
                        int lineOffset = 0;
                        if (!string.IsNullOrEmpty(offsetStr))
                        {
                            int.TryParse(offsetStr.Trim(), out lineOffset);
                        }
                        string output = FileHandler.ReadFile(filename, out exitCode, lineOffset);
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

                case "grep_search":
                    {
                        string pattern = GetRequiredArg(call.Arguments, "pattern");
                        string directoryPath = JsonExtractString(call.Arguments, "directory_path");
                        string filePattern = JsonExtractString(call.Arguments, "file_pattern");
                        string caseInsensitive = JsonExtractString(call.Arguments, "case_insensitive");
                        string output = GrepSearch(pattern, directoryPath, filePattern, caseInsensitive, out exitCode);
                        string searchDesc = "grep '" + pattern + "'" + (string.IsNullOrEmpty(directoryPath) ? "" : " in " + directoryPath);
                        toolContent = FormatCommandResult(searchDesc, output, exitCode);
                        return true;
                    }

                case "search_replace":
                    {
                        string filePath = GetRequiredArg(call.Arguments, "file_path");
                        string content = GetRequiredArg(call.Arguments, "content");
                        string output = SearchReplace(filePath, content, out exitCode);
                        toolContent = FormatCommandResult("search_replace: " + filePath, output, exitCode);
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
    public static string ExecuteProcess(string fileName, string arguments, out int exitCode, bool combineErrorOutput = true, int timeoutMilliseconds = -1)
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

            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi))
            {
                if (timeoutMilliseconds > 0)
                {
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        // Process timed out - kill it
                        process.Kill();
                        process.WaitForExit(); // Wait for kill to complete
                        exitCode = -1;
                        throw new TimeoutException(string.Format("Process '{0}' timed out after {1} seconds.", fileName, timeoutMilliseconds / 1000));
                    }
                }
                else
                {
                    process.WaitForExit();
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
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

    // Recursively search for a regex pattern in files using grep.exe
    private static string GrepSearch(string pattern, string directoryPath, string filePattern, string caseInsensitive, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Get the directory where the executable is located
            string exeDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string grepExePath = System.IO.Path.Combine(exeDirectory, "grep.exe");

            if (!System.IO.File.Exists(grepExePath))
            {
                exitCode = 1;
                return "Error: grep.exe not found in: " + exeDirectory;
            }

            // Expand environment variables and determine search directory
            string searchPath = string.IsNullOrEmpty(directoryPath) 
                ? Environment.CurrentDirectory 
                : Environment.ExpandEnvironmentVariables(directoryPath.Trim());

            if (!System.IO.Directory.Exists(searchPath))
            {
                exitCode = 1;
                return "Error: Directory not found: " + searchPath;
            }

            // Build grep command arguments
            StringBuilder args = new StringBuilder();
            
            // Recursive search
            args.Append("-r ");
            
            // Case insensitive flag
            if (!string.IsNullOrEmpty(caseInsensitive) && 
                caseInsensitive.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                args.Append("-i ");
            }
            
            // Extended regex (supports more regex features)
            args.Append("-E ");
            
            // Include file pattern if specified
            if (!string.IsNullOrEmpty(filePattern) && filePattern.Trim().Length > 0)
            {
                args.Append("--include=");
                args.Append("\"" + filePattern.Trim() + "\" ");
            }
            
            // Exclude common directories and files that should be ignored
            args.Append("--exclude-dir=.git --exclude-dir=.svn --exclude-dir=.hg ");
            args.Append("--exclude-dir=.venv --exclude-dir=venv --exclude-dir=__pycache__ ");
            args.Append("--exclude-dir=node_modules --exclude-dir=bin --exclude-dir=obj ");
            args.Append("--exclude-dir=.vs --exclude-dir=packages --exclude-dir=dist ");
            args.Append("--exclude-dir=build --exclude-dir=.idea --exclude-dir=.vscode ");
            args.Append("--exclude-dir=target --exclude-dir=vendor --exclude-dir=bower_components ");
            args.Append("--exclude-dir=.nuget --exclude-dir=TestResults ");
            args.Append("--exclude=*.pyc --exclude=*.pyo --exclude=*.exe --exclude=*.dll ");
            args.Append("--exclude=*.so --exclude=*.dylib --exclude=*.obj --exclude=*.o ");
            args.Append("--exclude=*.a --exclude=*.lib --exclude=*.pdb --exclude=*.ilk ");
            args.Append("--exclude=*.class --exclude=*.jar --exclude=*.war --exclude=*.ear ");
            args.Append("--exclude=*.zip --exclude=*.tar --exclude=*.gz --exclude=*.rar ");
            args.Append("--exclude=*.png --exclude=*.jpg --exclude=*.jpeg --exclude=*.gif ");
            args.Append("--exclude=*.bmp --exclude=*.ico --exclude=*.svg --exclude=*.pdf ");
            args.Append("--exclude=*.mp3 --exclude=*.mp4 --exclude=*.avi --exclude=*.mov ");
            args.Append("--exclude=*.ttf --exclude=*.woff --exclude=*.woff2 --exclude=*.eot ");
            args.Append("--exclude=*.min.js --exclude=*.min.css --exclude=*.map ");
            args.Append("--exclude=*.lock --exclude=*.cache ");
            
            // Pattern (escape quotes if needed)
            string escapedPattern = pattern.Replace("\"", "\\\"");
            args.Append("\"" + escapedPattern + "\" ");
            
            // Search directory
            args.Append("\"" + searchPath + "\"");

            // Execute grep.exe with 60 second timeout
            string output = ExecuteProcess(grepExePath, args.ToString(), out exitCode, combineErrorOutput: false, timeoutMilliseconds: 60000);

            if (exitCode == 0 && string.IsNullOrWhiteSpace(output))
            {
                return "No matches found for pattern: " + pattern;
            }

            return output;
        }
        catch (Exception ex)
        {
            exitCode = 1;
            return "Error: " + ex.Message;
        }
    }

    public static string FormatCommandResult(string command, string output, int exitCode)
    {
        return "Exit Code: " + exitCode + "\nOutput:\n" + output;
    }

    #region SearchReplace

    public enum DiffChangeType
    {
        Addition,
        Deletion,
        Modification
    }

    public sealed class DiffChange
    {
        // Start index in the CURRENT buffer content (for preview)
        public int StartIndex;
        public int Length;
        public DiffChangeType Type;
    }

    // Preview events (show adornments BEFORE applying)
    public static event Action<string, List<DiffChange>> OnDiffChangesPreview;
    public static event Action<string> OnDiffPreviewCleared;


    // Parses SEARCH/REPLACE blocks and performs replacements in a file
    private static string SearchReplace(string filePath, string content, out int exitCode)
    {
        try
        {
            // 1) Preview only (no changes applied yet)
            SearchReplaceTool.ApplyResult preview = SearchReplaceTool.Preview(filePath, content);

            exitCode = preview.Errors.Count > 0 ? 1 : 0;

            StringBuilder sb = new StringBuilder();
            Action addSpacer = () =>
            {
                if (sb.Length > 0) sb.AppendLine();
            };

            if (preview.Errors.Count > 0)
            {
                addSpacer();
                sb.AppendLine("Errors:");
                foreach (string err in preview.Errors) sb.AppendLine(err);
                return sb.ToString();
            }

            if (string.Equals(preview.NewContent, preview.OriginalContent, StringComparison.Ordinal))
            {
                addSpacer();
                sb.AppendLine("No changes were necessary (file already matches).");
                return sb.ToString();
            }

            // 1b) Build an inline preview buffer (old + new right next to each other)
            SearchReplaceTool.InlinePreview inline = SearchReplaceTool.BuildInlinePreview(preview);

            // Try to apply the inline preview to the open document (no save) so it shows inline in the editor.
            bool previewShownInline = false;
            if (!string.IsNullOrEmpty(preview.NormalizedFilePath))
            {
                previewShownInline = EditorService.TrySetOpenDocumentContent(preview.NormalizedFilePath, inline.Content, false);
            }

            // Show inline highlight adornments (background + strikethrough) for preview spans
            if (previewShownInline && inline.Spans.Count > 0 && OnDiffChangesPreview != null)
            {
                List<DiffChange> changes = new List<DiffChange>();
                foreach (SearchReplaceTool.InlineSpan s in inline.Spans)
                {
                    changes.Add(new DiffChange
                    {
                        StartIndex = s.Start,
                        Length = s.Length,
                        Type = s.Type == SearchReplaceTool.ChangeType.Addition ? DiffChangeType.Addition : DiffChangeType.Deletion
                    });
                }

                string p = string.IsNullOrEmpty(preview.NormalizedFilePath) 
                    ? EditorService.NormalizeFilePath(filePath) 
                    : preview.NormalizedFilePath;
                OnDiffChangesPreview(p, changes);
            }

            // 2) Ask user to approve/reject using the bottom bar in the NyoCoder panel
            NyoCoderControl toolWindowControl = null;
            try
            {
                toolWindowControl = NyoCoder_VSIXPackage.Instance != null ? NyoCoder_VSIXPackage.Instance.ToolWindowControl : null;
            }
            catch { }

            // Fail-closed: do not apply changes unless explicitly approved via UI.
            ApprovalResult approvalResult = ApprovalResult.Rejected;
            string notApprovedMessage = "Rejected by user. No changes applied.";
            if (toolWindowControl != null)
            {
                StringBuilder approvalArgs = new StringBuilder();
                approvalArgs.AppendLine("Apply these changes?");
                approvalArgs.AppendLine("File: " + (string.IsNullOrEmpty(preview.NormalizedFilePath) ? filePath : preview.NormalizedFilePath));
                approvalArgs.AppendLine();
                if (!string.IsNullOrEmpty(preview.PreviewDiff))
                {
                    // PreviewDiff already ends with a newline, so use Append instead of AppendLine
                    approvalArgs.Append(preview.PreviewDiff);
                }
                approvalResult = toolWindowControl.RequestToolApproval("search_replace", approvalArgs.ToString());
                
                // If user stopped, treat as rejected (session will be stopped by LLMClient)
                if (approvalResult == ApprovalResult.Stopped)
                {
                    notApprovedMessage = "Session stopped by user. No changes applied.";
                }
            }
            else
            {
                // No approval UI available: treat as not approved.
                // This prevents unexpected file modifications when running headless / without the tool window.
                exitCode = 1;
                notApprovedMessage = "Error: Approval UI unavailable. No changes applied.";
            }

            if (approvalResult != ApprovalResult.Approved)
            {
                // Clear preview adornments
                if (OnDiffPreviewCleared != null)
                {
                    string p = string.IsNullOrEmpty(preview.NormalizedFilePath) 
                        ? EditorService.NormalizeFilePath(filePath) 
                        : preview.NormalizedFilePath;
                    OnDiffPreviewCleared(p);
                }

                // Restore original content if we showed an inline preview
                if (previewShownInline && !string.IsNullOrEmpty(preview.NormalizedFilePath))
                {
                    EditorService.TrySetOpenDocumentContent(preview.NormalizedFilePath, preview.OriginalContent ?? "", false);
                }
                addSpacer();
                sb.AppendLine(notApprovedMessage);
                return sb.ToString();
            }

            // 3) Clear preview adornments, then apply changes
            if (OnDiffPreviewCleared != null)
            {
                string p = string.IsNullOrEmpty(preview.NormalizedFilePath) 
                    ? EditorService.NormalizeFilePath(filePath) 
                    : preview.NormalizedFilePath;
                OnDiffPreviewCleared(p);
            }

            bool appliedOk = false;

            // If the doc is open (inline preview path), set final content in the editor and save.
            if (previewShownInline && !string.IsNullOrEmpty(preview.NormalizedFilePath))
            {
                appliedOk = EditorService.TrySetOpenDocumentContent(preview.NormalizedFilePath, preview.NewContent ?? "", true);
            }

            // Fallback: apply via file write / open-doc apply
            if (!appliedOk)
            {
                appliedOk = SearchReplaceTool.ApplyPreview(preview);
            }

            if (!appliedOk)
            {
                exitCode = 1;
                addSpacer();
                sb.AppendLine("Error: Failed to apply changes.");
                return sb.ToString();
            }

            exitCode = 0;
            sb.AppendLine("Approved and applied " + preview.Changes.Count + " block(s).");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            exitCode = 1;
            return "Error: " + ex.Message;
        }
    }
    #endregion
}
}
