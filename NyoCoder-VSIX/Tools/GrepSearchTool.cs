using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace NyoCoder
{
    internal static class GrepSearchTool
    {
        internal static string Search(string pattern, string directoryPath, string filePattern, string caseInsensitive, out int exitCode)
        {
            exitCode = 0;

            try
            {
                string exeDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string grepExePath = Path.Combine(exeDirectory, "grep.exe");

                if (!File.Exists(grepExePath))
                {
                    exitCode = 1;
                    return "Error: grep.exe not found in: " + exeDirectory;
                }

                string searchPath = string.IsNullOrEmpty(directoryPath)
                    ? Environment.CurrentDirectory
                    : Environment.ExpandEnvironmentVariables(directoryPath.Trim());

                if (!Directory.Exists(searchPath))
                {
                    exitCode = 1;
                    return "Error: Directory not found: " + searchPath;
                }

                StringBuilder args = new StringBuilder();

                args.Append("-r ");

                if (!string.IsNullOrEmpty(caseInsensitive) &&
                    caseInsensitive.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    args.Append("-i ");
                }

                args.Append("-E ");

                if (!string.IsNullOrEmpty(filePattern) && filePattern.Trim().Length > 0)
                {
                    args.Append("--include=");
                    args.Append("\"" + filePattern.Trim() + "\" ");
                }

                // Directory and file exclusions come from the shared FileScanFilter so grep and
                // the codebase indexer stay consistent about what to ignore.
                foreach (string excludeDir in FileScanFilter.ExcludedDirectoryNames)
                    args.Append("--exclude-dir=").Append(excludeDir).Append(" ");
                foreach (string excludeGlob in FileScanFilter.ExcludedFileGlobs)
                    args.Append("--exclude=").Append(excludeGlob).Append(" ");

                string escapedPattern = pattern.Replace("\"", "\\\"");
                args.Append("\"" + escapedPattern + "\" ");

                args.Append("\"" + searchPath + "\"");

                string output = ProcessRunner.RunCommand(grepExePath, args.ToString(), out exitCode, combineErrorOutput: false, timeoutMilliseconds: 60000);

                if (exitCode == 0 && string.IsNullOrWhiteSpace(output))
                    return "No matches found for pattern: " + pattern;

                string[] lines = output.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                int maxLines = ConfigHandler.MaxReadLines;
                if (lines.Length > maxLines)
                    return string.Join(Environment.NewLine, lines, 0, maxLines) + Environment.NewLine + "truncated..";

                return output;
            }
            catch (Exception ex)
            {
                exitCode = 1;
                return "Error: " + ex.Message;
            }
        }
    }
}
