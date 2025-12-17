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

            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi))
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

            // Execute grep.exe
            string output = ExecuteProcess(grepExePath, args.ToString(), out exitCode);

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

    // Parses SEARCH/REPLACE blocks and performs replacements in a file
    private static string SearchReplace(string filePath, string content, out int exitCode)
    {
        exitCode = 0;

        try
        {
            // Expand environment variables in file path
            filePath = Environment.ExpandEnvironmentVariables(filePath);

            if (!File.Exists(filePath))
            {
                exitCode = 1;
                return "File not found: " + filePath;
            }

            // Read the file content
            string fileContent = File.ReadAllText(filePath, Encoding.UTF8);
            string originalContent = fileContent;

            // Parse SEARCH/REPLACE blocks
            List<SearchReplaceBlock> blocks = ParseSearchReplaceBlocks(content);
            
            if (blocks.Count == 0)
            {
                exitCode = 1;
                return "Error: No valid SEARCH/REPLACE blocks found. Expected format:\n<<<<<<< SEARCH\n[text to find]\n=======\n[text to replace with]\n>>>>>>> REPLACE";
            }

            StringBuilder result = new StringBuilder();
            int totalReplacements = 0;

            // Apply each block in order
            foreach (var block in blocks)
            {
                int occurrences = 0;

                // Count occurrences first
                int searchIndex = 0;
                while ((searchIndex = fileContent.IndexOf(block.SearchText, searchIndex, StringComparison.Ordinal)) != -1)
                {
                    occurrences++;
                    searchIndex += block.SearchText.Length;
                }

                if (occurrences == 0)
                {
                    exitCode = 1;
                    result.AppendLine("Error: Search text not found in file:\n" + 
                        (block.SearchText.Length > 100 ? block.SearchText.Substring(0, 100) + "..." : block.SearchText));
                    return result.ToString();
                }

                if (occurrences > 1)
                {
                    exitCode = 1;
                    result.AppendLine("Error: Search text appears " + occurrences + " times in the file. The SEARCH text must appear exactly once.");
                    result.AppendLine("Search text:\n" + 
                        (block.SearchText.Length > 200 ? block.SearchText.Substring(0, 200) + "..." : block.SearchText));
                    return result.ToString();
                }

                // Perform replacement (we know there's exactly one occurrence)
                int index = fileContent.IndexOf(block.SearchText, StringComparison.Ordinal);
                if (index != -1)
                {
                    fileContent = fileContent.Substring(0, index) + 
                                 block.ReplaceText + 
                                 fileContent.Substring(index + block.SearchText.Length);
                    totalReplacements++;
                }
            }

            // Check if there are any changes
            if (fileContent != originalContent)
            {
                // Show approval dialog with preview of changes
                bool approved = ShowSearchReplaceApproval(filePath, blocks, originalContent, fileContent);
                
                if (!approved)
                {
                    // User declined - don't modify the file, return cancellation message
                    exitCode = -1;
                    return "Tool execution was cancelled by the user. File was not modified.";
                }

                // User approved - write the modified content back to the file
                File.WriteAllText(filePath, fileContent, Encoding.UTF8);
                result.AppendLine("File updated successfully: " + filePath);
                result.AppendLine("Total replacements: " + totalReplacements);

                // Try to open the file in Visual Studio and highlight changes
                TryOpenFileInVisualStudio(filePath, blocks);
            }
            else
            {
                result.AppendLine("No changes were made to the file.");
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "Error performing search_replace: " + ex.Message;
        }
    }

    // Structure to hold a single SEARCH/REPLACE block
    private struct SearchReplaceBlock
    {
        public string SearchText;
        public string ReplaceText;
    }

    // Parses SEARCH/REPLACE blocks from the content string
    private static List<SearchReplaceBlock> ParseSearchReplaceBlocks(string content)
    {
        List<SearchReplaceBlock> blocks = new List<SearchReplaceBlock>();

        // Pattern to match SEARCH/REPLACE blocks
        // Matches: <<<<<<< SEARCH\n...\n=======\n...\n>>>>>>> REPLACE
        // Requires at least 5 equals signs between SEARCH and REPLACE sections
        string pattern = @"<<<<<<<\s*SEARCH\s*\r?\n(.*?)\r?\n={5,}\r?\n(.*?)\r?\n>>>>>>>\s*REPLACE";
        
        MatchCollection matches = Regex.Matches(content, pattern, RegexOptions.Singleline | RegexOptions.Multiline);

        foreach (Match match in matches)
        {
            if (match.Groups.Count >= 3)
            {
                SearchReplaceBlock block = new SearchReplaceBlock
                {
                    SearchText = match.Groups[1].Value,
                    ReplaceText = match.Groups[2].Value
                };
                blocks.Add(block);
            }
        }

        return blocks;
    }

    // Shows an approval dialog with preview of changes for search_replace using the tool window button bar
    private static bool ShowSearchReplaceApproval(string filePath, List<SearchReplaceBlock> blocks, string originalContent, string modifiedContent)
    {
        try
        {
            // Get the tool window through Connect instance
            Connect connectInstance = Connect.Instance;
            if (connectInstance == null || connectInstance.ToolWindowControl == null)
            {
                // Fallback to MessageBox if tool window is not available
                return ShowSearchReplaceApprovalFallback(filePath, blocks);
            }

            NyoCoderToolWindow toolWindow = connectInstance.ToolWindowControl;

            StringBuilder preview = new StringBuilder();
            preview.AppendLine("File: " + filePath);
            preview.AppendLine();
            preview.AppendLine("The following changes will be made:");
            preview.AppendLine();

            // Build a preview showing each change
            string currentContent = originalContent;
            int changeNumber = 1;

            foreach (var block in blocks)
            {
                int index = currentContent.IndexOf(block.SearchText, StringComparison.Ordinal);
                if (index != -1)
                {
                    // Find the line numbers for context
                    string before = currentContent.Substring(0, index);
                    string[] lines = before.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                    int startLine = lines.Length;
                    
                    preview.AppendLine("--- Change " + changeNumber + " ---");
                    preview.AppendLine("Search text (line " + startLine + "):");
                    
                    // Show search text (truncated if too long)
                    string searchPreview = block.SearchText;
                    if (searchPreview.Length > 500)
                    {
                        searchPreview = searchPreview.Substring(0, 500) + "\n...[truncated, " + block.SearchText.Length + " characters total]";
                    }
                    preview.AppendLine(searchPreview);
                    
                    preview.AppendLine();
                    preview.AppendLine("Replace with:");
                    string replacePreview = block.ReplaceText;
                    if (replacePreview.Length > 500)
                    {
                        replacePreview = replacePreview.Substring(0, 500) + "\n...[truncated, " + block.ReplaceText.Length + " characters total]";
                    }
                    preview.AppendLine(replacePreview);
                    preview.AppendLine();

                    // Update currentContent for next iteration
                    currentContent = currentContent.Substring(0, index) + 
                                   block.ReplaceText + 
                                   currentContent.Substring(index + block.SearchText.Length);
                    changeNumber++;
                }
            }

            preview.AppendLine("---");
            preview.AppendLine();
            preview.AppendLine("Do you want to apply these changes?");

            // Use the tool window's RequestToolApproval method which uses the button bar
            return toolWindow.RequestToolApproval("search_replace", preview.ToString());
        }
        catch (Exception ex)
        {
            // If anything fails, fall back to MessageBox
            return ShowSearchReplaceApprovalFallback(filePath, blocks);
        }
    }

    // Fallback approval method using MessageBox (used when tool window is not available)
    private static bool ShowSearchReplaceApprovalFallback(string filePath, List<SearchReplaceBlock> blocks)
    {
        string simpleMessage = "Apply search_replace to file: " + filePath + "?\n\n" + 
                             blocks.Count + " replacement(s) will be made.";
        
        DialogResult result = MessageBox.Show(
            simpleMessage,
            "NyoCoder - Approve search_replace?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2
        );

        return (result == DialogResult.Yes);
    }

    // Attempts to open a file in Visual Studio and highlight the changed regions
    private static void TryOpenFileInVisualStudio(string filePath, List<SearchReplaceBlock> blocks)
    {
        try
        {
            // Get the Connect instance to access Visual Studio DTE
            Connect connectInstance = Connect.Instance;
            if (connectInstance == null || connectInstance.ApplicationObject == null)
            {
                return; // Not running in Visual Studio context
            }

            // Open the file in Visual Studio on a background thread
            System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                try
                {
                    var dte = connectInstance.ApplicationObject;
                    
                    // Open the file in Visual Studio
                    Window window = dte.ItemOperations.OpenFile(filePath, 
                        EnvDTE.Constants.vsViewKindCode);
                    
                    if (window != null && window.Document != null && window.Document.Type == "Text")
                    {
                        // Get the text document
                        EnvDTE.TextDocument textDoc = null;
                        try
                        {
                            // Access the Object property through COM interop
                            // Note: Object is a property in EnvDTE, accessed as a property getter
                            EnvDTE.Document doc = window.Document;
                            if (doc != null)
                            {
                            // In some COM interop scenarios, properties may need special handling
                            // Use reflection to access the Object property to avoid method group errors
                            System.Reflection.PropertyInfo objProp = doc.GetType().GetProperty("Object");
                            if (objProp != null)
                            {
                                object docObj = objProp.GetValue(doc, null);
                                textDoc = docObj as EnvDTE.TextDocument;
                            }
                            // If reflection fails, textDoc remains null and we skip text document operations
                            }
                        }
                        catch
                        {
                            // If accessing Object fails, skip text document operations
                            textDoc = null;
                        }
                        if (textDoc != null)
                        {
                            // Read the file to find the changed regions
                            string currentContent = File.ReadAllText(filePath, Encoding.UTF8);
                            
                            // For each block, try to highlight the changed region
                            // Note: Full text adornment highlighting requires VSIX extensions with MEF
                            // For an add-in, we can at least select the changed text to make it visible
                            foreach (var block in blocks)
                            {
                                int index = currentContent.IndexOf(block.ReplaceText, StringComparison.Ordinal);
                                if (index != -1)
                                {
                                    // Convert character index to line/column (1-based)
                                    string beforeReplace = currentContent.Substring(0, index);
                                    string[] lines = beforeReplace.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                                    int lineNumber = lines.Length; // 1-based line number
                                    int columnNumber = lines.Length > 0 ? lines[lines.Length - 1].Length + 1 : 1; // 1-based column
                                    
                                    // Try to select the changed text
                                    try
                                    {
                                        EnvDTE.EditPoint startPoint = textDoc.StartPoint.CreateEditPoint();
                                        startPoint.MoveToLineAndOffset(lineNumber, columnNumber);
                                        
                                        // Calculate end position
                                        string replaceText = block.ReplaceText;
                                        string[] replaceLines = replaceText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                                        int endLineNumber = lineNumber + replaceLines.Length - 1;
                                        int endColumnNumber = replaceLines.Length > 1 
                                            ? replaceLines[replaceLines.Length - 1].Length + 1
                                            : columnNumber + replaceText.Length;
                                        
                                        EnvDTE.EditPoint endPoint = startPoint.CreateEditPoint();
                                        endPoint.MoveToLineAndOffset(endLineNumber, endColumnNumber);
                                        
                                        textDoc.Selection.MoveToPoint(startPoint);
                                        textDoc.Selection.MoveToPoint(endPoint, true);
                                        
                                        // Scroll to make the selection visible
                                        textDoc.Selection.AnchorPoint.TryToShow();
                                    }
                                    catch
                                    {
                                        // If selection fails, at least the file is open
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Silently fail if VS integration is not available
                }
            });
        }
        catch
        {
            // Silently fail if VS integration is not available
        }
    }
}
}
