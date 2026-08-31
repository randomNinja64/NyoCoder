using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NyoCoder
{
    internal static class DownloadFileTool
    {
        public static string Download(string filename, string url, out int exitCode)
        {
            exitCode = 0;

            try
            {
                filename = Environment.ExpandEnvironmentVariables(filename);

                string fileExtension = Path.GetExtension(filename).ToLower();
                string[] expectedTypes = GetExpectedMimeTypes(fileExtension);

                string contentType = "";

                if (expectedTypes != null)
                {
                    int headExitCode;
                    contentType = GetContentTypeFromUrl(url, out headExitCode);

                    if (headExitCode == 0 && !string.IsNullOrEmpty(contentType))
                    {
                        if (!contentType.ToLower().Contains("application/octet-stream"))
                        {
                            bool isValidType = false;
                            foreach (string expectedType in expectedTypes)
                            {
                                if (contentType.ToLower().Contains(expectedType.ToLower()))
                                {
                                    isValidType = true;
                                    break;
                                }
                            }

                            if (!isValidType)
                            {
                                exitCode = 1;
                                return "File type mismatch: Expected " + string.Join(" or ", expectedTypes)
                                    + " but got '" + contentType + "'. Download cancelled.";
                            }
                        }
                    }
                }

                string directory = Path.GetDirectoryName(filename);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                    }
                    catch (Exception ex)
                    {
                        exitCode = 1;
                        return "Failed to create directory '" + directory + "': " + ex.Message;
                    }
                }

                string output = CurlExecute(url, out exitCode, "-o \"" + filename + "\"");

                if (exitCode != 0)
                    return "curl exited with code " + exitCode + ": " + output;

                string successMessage = "File downloaded successfully: " + filename;
                if (!string.IsNullOrEmpty(contentType))
                    successMessage += " (Content-Type: " + contentType + ")";

                return successMessage;
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "Error running curl.exe: " + ex.Message;
            }
        }

        private static string CurlExecute(string url, out int exitCode, string extraFlags = "", bool combineErrorOutput = true)
        {
            string userAgent = ConfigHandler.GetConfigValue("webUserAgent", WebSearchTool.DefaultUserAgent);
            StringBuilder args = new StringBuilder();
            args.Append("-s -L");
            if (!string.IsNullOrEmpty(extraFlags))
                args.Append(" ").Append(extraFlags);
            args.Append(" -H \"User-Agent: ").Append(userAgent).Append("\"");
            args.Append(" \"").Append(url).Append("\"");
            return ProcessRunner.RunCommand(CurlClient.GetCurlPath(), args.ToString(), out exitCode, combineErrorOutput);
        }

        private static string GetContentTypeFromUrl(string url, out int exitCode)
        {
            try
            {
                string headers = CurlExecute(url, out exitCode, "-I", combineErrorOutput: false);

                if (exitCode != 0)
                    return "";

                Regex contentTypeRegex = new Regex(
                    @"content-type:\s*([^\r\n;]+)",
                    RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
                Match match = contentTypeRegex.Match(headers);

                if (match.Success)
                    return match.Groups[1].Value.Trim();

                return "";
            }
            catch (Exception)
            {
                exitCode = -1;
                return "";
            }
        }

        private static readonly Dictionary<string, string[]> MimeTypeMap = new Dictionary<string, string[]>()
        {
            { ".jpg", new[] { "image/jpeg" } },
            { ".jpeg", new[] { "image/jpeg" } },
            { ".png", new[] { "image/png" } },
            { ".gif", new[] { "image/gif" } },
            { ".webp", new[] { "image/webp" } },
            { ".bmp", new[] { "image/bmp" } },
            { ".svg", new[] { "image/svg+xml" } },
            { ".ico", new[] { "image/x-icon", "image/vnd.microsoft.icon" } },

            { ".pdf", new[] { "application/pdf" } },
            { ".doc", new[] { "application/msword" } },
            { ".docx", new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" } },
            { ".xls", new[] { "application/vnd.ms-excel" } },
            { ".xlsx", new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } },
            { ".ppt", new[] { "application/vnd.ms-powerpoint" } },
            { ".pptx", new[] { "application/vnd.openxmlformats-officedocument.presentationml.presentation" } },

            { ".txt", new[] { "text/plain" } },
            { ".html", new[] { "text/html" } },
            { ".htm", new[] { "text/html" } },
            { ".css", new[] { "text/css" } },
            { ".js", new[] { "text/javascript", "application/javascript" } },
            { ".json", new[] { "application/json" } },
            { ".xml", new[] { "text/xml", "application/xml" } },
            { ".csv", new[] { "text/csv" } },

            { ".zip", new[] { "application/zip" } },
            { ".rar", new[] { "application/x-rar-compressed" } },
            { ".7z", new[] { "application/x-7z-compressed" } },
            { ".tar", new[] { "application/x-tar" } },
            { ".gz", new[] { "application/gzip" } },

            { ".mp3", new[] { "audio/mpeg" } },
            { ".wav", new[] { "audio/wav" } },
            { ".ogg", new[] { "audio/ogg" } },
            { ".flac", new[] { "audio/flac" } },

            { ".mp4", new[] { "video/mp4" } },
            { ".avi", new[] { "video/x-msvideo" } },
            { ".mkv", new[] { "video/x-matroska" } },
            { ".mov", new[] { "video/quicktime" } },
            { ".webm", new[] { "video/webm" } },

            { ".exe", new[] { "application/x-msdownload", "application/x-msdos-program", "application/octet-stream" } },
            { ".dll", new[] { "application/x-msdownload", "application/x-msdos-program", "application/octet-stream" } },
            { ".bin", new[] { "application/octet-stream" } }
        };

        private static string[] GetExpectedMimeTypes(string fileExtension)
        {
            string[] result;
            if (MimeTypeMap.TryGetValue(fileExtension.ToLower(), out result))
                return result;
            return null;
        }
    }
}
