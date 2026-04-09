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

                string escapedPattern = pattern.Replace("\"", "\\\"");
                args.Append("\"" + escapedPattern + "\" ");

                args.Append("\"" + searchPath + "\"");

                string output = ToolHandler.ExecuteProcess(grepExePath, args.ToString(), out exitCode, combineErrorOutput: false, timeoutMilliseconds: 60000);

                if (exitCode == 0 && string.IsNullOrWhiteSpace(output))
                    return "No matches found for pattern: " + pattern;

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
