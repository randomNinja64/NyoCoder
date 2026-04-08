using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace NyoCoder
{
    internal static class WebSearchTool
    {
        internal const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.5993.90 Safari/537.36";

        // Executes curl for a given URL and returns the response body
        private static string CurlExecute(string url, out int exitCode, bool combineErrorOutput = false, params string[] extraHeaders)
        {
            string userAgent = ConfigHandler.GetConfigValue("webUserAgent", DefaultUserAgent);
            StringBuilder args = new StringBuilder();
            args.Append("-s -L");
            args.Append(" -H \"User-Agent: " + userAgent + "\"");
            foreach (string header in extraHeaders)
                args.Append(" -H \"" + header + "\"");
            args.Append(" \"" + url + "\"");
            return ToolHandler.ExecuteProcess(CurlClient.GetCurlPath(), args.ToString(), out exitCode, combineErrorOutput);
        }

        // --- read_website ---
        public static string ReadWebsite(string url, int maxContentLength, out int exitCode)
        {
            string html;
            try
            {
                html = CurlExecute(url, out exitCode, combineErrorOutput: false);

                // Strip DOCTYPE, script, style, svg, nav, header blocks
                html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"<html\b[^>]*>", "<html>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"<style\b[^<]*(?:(?!<\/style>)<[^<]*)*<\/style>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"<path\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"<svg\b[^<]*(?:(?!<\/svg>)<[^<]*)*<\/svg>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"<nav\b[^<]*(?:(?!<\/nav>)<[^<]*)*<\/nav>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"<header\b[^<]*(?:(?!<\/header>)<[^<]*)*<\/header>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"<meta\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"<link\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"<form\b[^<]*(?:(?!<\/form>)<[^<]*)*<\/form>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"<input\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                // Strip all attributes from img tags except src
                html = Regex.Replace(html, @"<img\b[^>]*\bsrc\s*=\s*(['""])([^'""]*)\1[^>]*>", "<img src=\"$2\">", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                // Strip all attributes from anchor tags except href
                html = Regex.Replace(html, @"<a\b[^>]*\bhref\s*=\s*(['""])([^'""]*)\1[^>]*>", "<a href=\"$2\">", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                // Remove inline event handlers and presentation attributes
                html = Regex.Replace(html, @"\s(on\w+|style|class|id|method|role)\s*=\s*(['""]).*?\2", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                html = Regex.Replace(html, @"<!--.*?-->", "", RegexOptions.Singleline);
                html = Regex.Replace(html, @"^\s*$[\r\n]*", "", RegexOptions.Multiline);
                html = Regex.Replace(html, @"<head\b[^>]*>.*?</head>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"<html>", "", RegexOptions.IgnoreCase);
                html = Regex.Replace(html, @"</html>", "", RegexOptions.IgnoreCase);
                html = Regex.Replace(html, @"<body\b[^>]*>", "", RegexOptions.IgnoreCase);
                html = Regex.Replace(html, @"</body>", "", RegexOptions.IgnoreCase);

                // Remove common inline/block tags but keep their content
                html = Regex.Replace(html, @"</?[pibPIB]\b[^>]*>", "", RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?u\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?ul\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?ol\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?li\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
                html = Regex.Replace(html, @"</?div\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?strong\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?span\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?pre\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?table\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?thead\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?tbody\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?tfoot\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?tr\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?td\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                html = Regex.Replace(html, @"</?th\b[^>]*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                // Clean up whitespace
                html = Regex.Replace(html, @">\s+<", "><", RegexOptions.Singleline);
                html = Regex.Replace(html, @"[ \t]+", " ", RegexOptions.Multiline);
                html = Regex.Replace(html, @"^\s+|\s+$", "", RegexOptions.Multiline);

                if (html.Length > maxContentLength)
                    html = html.Substring(0, maxContentLength);
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "Error running curl.exe: " + ex.Message;
            }

            return html + "\n";
        }

        // --- run_web_search ---
        public static string RunWebSearch(string query, string searxngInstance, int maxSearchResults, out int exitCode)
        {
            string output = "";
            exitCode = 0;

            // Try SearXNG if configured
            if (!string.IsNullOrWhiteSpace(searxngInstance))
                output = RunSearXNGSearch(query, searxngInstance, maxSearchResults, out exitCode);

            // Fallback to DuckDuckGo
            if (string.IsNullOrWhiteSpace(output))
                output = RunDDGSearch(query, maxSearchResults, out exitCode);

            // Fallback to Wiby
            if (string.IsNullOrWhiteSpace(output))
                output = RunWibySearch(query, maxSearchResults, out exitCode);

            if (string.IsNullOrWhiteSpace(output))
                output = "No results found.";

            return output;
        }

        private static string RunDDGSearch(string query, int maxSearchResults, out int exitCode)
        {
            string url = "https://duckduckgo.com/html/?q=" + Uri.EscapeDataString(query);
            string response;
            try
            {
                response = CurlExecute(url, out exitCode, false,
                    "Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                    "Accept-Language: en-US,en;q=0.5");
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "Error running curl.exe for search: " + ex.Message;
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                exitCode = -1;
                return "";
            }

            try
            {
                return TruncateResults(ParseDDGResults(response), maxSearchResults);
            }
            catch
            {
                exitCode = -1;
                return "";
            }
        }

        private static string ParseDDGResults(string html)
        {
            Regex snippetRegex = new Regex("<a class=\"result__snippet\" href=\"([^\"]+)\">(.+?)</a>", RegexOptions.IgnoreCase);
            Regex htmlTagRegex = new Regex("<[^>]+>");
            Regex uddgRegex = new Regex("uddg=([^&]+)");

            MatchCollection matches = snippetRegex.Matches(html);
            StringBuilder results = new StringBuilder();

            foreach (Match match in matches)
            {
                string href = match.Groups[1].Value;
                string snippet = htmlTagRegex.Replace(match.Groups[2].Value, "");

                Match urlMatch = uddgRegex.Match(href);
                if (urlMatch.Success)
                {
                    string fixedUrl = Uri.UnescapeDataString(urlMatch.Groups[1].Value);
                    if (!fixedUrl.Contains("duckduckgo.com/y.js"))
                        results.AppendLine(fixedUrl + " : " + snippet);
                }
            }

            return results.ToString();
        }

        private static string RunWibySearch(string query, int maxSearchResults, out int exitCode)
        {
            string url = "https://wiby.me/json/?q=" + Uri.EscapeDataString(query);
            string response;
            try
            {
                response = CurlExecute(url, out exitCode, combineErrorOutput: false);
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "Error running curl.exe for search: " + ex.Message;
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                exitCode = -1;
                return "";
            }

            try
            {
                return TruncateResults(ParseWibyResults(response), maxSearchResults);
            }
            catch
            {
                exitCode = -1;
                return "";
            }
        }

        private static string ParseWibyResults(string json)
        {
            JArray resultsArray = JArray.Parse(json);
            if (resultsArray == null || resultsArray.Count == 0)
                return "";

            StringBuilder results = new StringBuilder();
            foreach (JToken result in resultsArray)
            {
                string url = result["URL"] != null ? result["URL"].ToString() : "";
                string title = result["Title"] != null ? result["Title"].ToString() : "";
                string snippet = result["Snippet"] != null ? result["Snippet"].ToString() : "";
                if (!string.IsNullOrEmpty(url))
                    results.AppendLine(url + " : " + title + " - " + snippet);
            }

            return results.ToString();
        }

        private static string RunSearXNGSearch(string query, string searxngInstance, int maxSearchResults, out int exitCode)
        {
            string url = searxngInstance.TrimEnd('/') + "/search?q=" + Uri.EscapeDataString(query) + "&format=json";
            string response;
            try
            {
                response = CurlExecute(url, out exitCode, combineErrorOutput: false);
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "Error running curl.exe for search: " + ex.Message;
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                exitCode = -1;
                return "";
            }

            try
            {
                return TruncateResults(ParseSearXNGResults(response), maxSearchResults);
            }
            catch
            {
                exitCode = -1;
                return "";
            }
        }

        private static string ParseSearXNGResults(string json)
        {
            JArray sngResults = JToken.Parse(json)["results"] as JArray;
            if (sngResults == null)
                return "";

            StringBuilder results = new StringBuilder();
            foreach (JToken result in sngResults)
            {
                string title = result["title"] != null ? result["title"].ToString() : "";
                string url = result["url"] != null ? result["url"].ToString() : "";
                string content = result["content"] != null ? result["content"].ToString() : "";
                if (!string.IsNullOrEmpty(url))
                    results.AppendLine(url + " : " + title + " - " + content);
            }

            return results.ToString();
        }

        private static string TruncateResults(string results, int maxResults)
        {
            if (string.IsNullOrEmpty(results))
                return results;
            string[] lines = results.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > maxResults)
                return string.Join("\n", lines, 0, maxResults) + "\n";
            return results;
        }
    }
}
