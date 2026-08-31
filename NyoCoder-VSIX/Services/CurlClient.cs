using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;

namespace NyoCoder
{
    internal static class CurlClient
    {
        /// <summary>
        /// Returns the path to curl.exe in the Dependencies folder next to this assembly.
        /// </summary>
        public static string GetCurlPath()
        {
            return Path.Combine(
                Path.GetDirectoryName(typeof(CurlClient).Assembly.Location),
                "curl.exe");
        }

        /// <summary>
        /// Sends a GET request using curl.exe and returns the response body. Used as an HTTPS/TLS
        /// fallback for endpoints (e.g. /v1/models) where .NET's HttpWebRequest fails on legacy
        /// .NET 4.0. On failure sets <paramref name="exitCode"/> non-zero and returns diagnostic text.
        /// </summary>
        public static string GetJson(string fullUrl, string apiKey, out int exitCode)
        {
            exitCode = -1;
            try
            {
                string authHeader = string.IsNullOrEmpty(apiKey)
                    ? ""
                    : " -H \"Authorization: Bearer " + apiKey + "\"";

                string arguments = "-s -X GET"
                    + " -H \"Accept: application/json\""
                    + authHeader
                    + " \"" + fullUrl + "\"";

                ProcessRunResult result = ProcessRunner.Run(GetCurlPath(), arguments);
                exitCode = result.ExitCode;
                return FormatCurlFailure(result);
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "cURL fallback failed: " + ex.Message;
            }
        }

        /// <summary>
        /// Sends a non-streaming JSON POST using curl.exe via a Windows named pipe and returns
        /// the full response body. Used as an HTTPS/TLS fallback for endpoints (e.g. embeddings)
        /// where .NET's HttpWebRequest fails on legacy .NET 4.0. Returns the raw body on success;
        /// on failure sets <paramref name="exitCode"/> non-zero and returns diagnostic text.
        /// </summary>
        public static string PostJson(string fullUrl, string apiKey, JObject payload, out int exitCode)
        {
            exitCode = -1;
            string pipeName = "nyocoder_" + Guid.NewGuid().ToString("N");
            byte[] jsonBytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));

            try
            {
                string arguments = "-s -X POST"
                    + " -H \"Content-Type: application/json\""
                    + " -H \"Authorization: Bearer " + apiKey + "\""
                    + " --data-binary \"@\\\\.\\pipe\\" + pipeName + "\""
                    + " \"" + fullUrl + "\"";

                ProcessRunResult result = ProcessRunner.RunWithNamedPipeStdin(
                    GetCurlPath(), arguments, pipeName, jsonBytes);

                exitCode = result.ExitCode;
                return FormatCurlFailure(result);
            }
            catch (Exception ex)
            {
                exitCode = -1;
                return "cURL fallback failed: " + ex.Message;
            }
        }

        /// <summary>
        /// Sends the LLM request using curl.exe via a Windows named pipe to avoid writing
        /// the JSON body to disk. Used as a fallback when .NET's HttpWebRequest fails on HTTPS.
        /// </summary>
        public static LLMClient.LLMCompletionResponse SendRequest(
            string serverUrl, string apiKey, JObject payload,
            Action<string> outputCallback,
            Action<ToolHandler.ToolCall> toolCallCallback,
            Func<bool> stopRequested,
            Action startBlock = null)
        {
            string pipeName = "nyocoder_" + Guid.NewGuid().ToString("N");
            byte[] jsonBytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));

            try
            {
                string arguments = "-s -N -X POST"
                    + " -H \"Content-Type: application/json\""
                    + " -H \"Authorization: Bearer " + apiKey + "\""
                    + " --data-binary \"@\\\\.\\pipe\\" + pipeName + "\""
                    + " \"" + serverUrl + "/v1/chat/completions\"";

                LLMClient.LLMCompletionResponse parsed = default(LLMClient.LLMCompletionResponse);
                bool parsedReceived = false;
                ProcessRunResult result = ProcessRunner.RunWithNamedPipeStdin(
                    GetCurlPath(),
                    arguments,
                    pipeName,
                    jsonBytes,
                    stdoutConsumer: reader =>
                    {
                        Action<string> onReasoningChunk;
                        Action<int> onReasoningSummary;
                        SseStreamParser.CreateReasoningCallbacks(
                            outputCallback, startBlock, out onReasoningChunk, out onReasoningSummary);

                        parsed = SseStreamParser.Parse(
                            reader, outputCallback, toolCallCallback, stopRequested,
                            onReasoningChunk: onReasoningChunk,
                            onReasoningSummary: onReasoningSummary,
                            startBlock: startBlock);
                        parsedReceived = true;
                    });

                if (result.ExitCode != 0)
                {
                    return new LLMClient.LLMCompletionResponse(
                        "cURL failed (exit " + result.ExitCode + "): " + result.StdErr,
                        null, "request_failed");
                }

                int toolCallCount = parsed.ToolCalls != null ? parsed.ToolCalls.Count : 0;
                if (!parsedReceived ||
                    (string.IsNullOrEmpty(parsed.Content) &&
                     string.IsNullOrEmpty(parsed.FinishReason) &&
                     toolCallCount == 0))
                {
                    string detail = string.IsNullOrEmpty(result.StdErr) ? "no response data" : result.StdErr.Trim();
                    return new LLMClient.LLMCompletionResponse(
                        "cURL returned no response: " + detail,
                        null, "request_failed");
                }

                return parsed;
            }
            catch (Exception ex)
            {
                return new LLMClient.LLMCompletionResponse(
                    "cURL fallback failed: " + ex.Message, null, "request_failed");
            }
        }

        private static string FormatCurlFailure(ProcessRunResult result)
        {
            if (result.ExitCode != 0 && string.IsNullOrEmpty(result.StdOut))
                return "cURL failed (exit " + result.ExitCode + "): " + result.StdErr;
            return result.StdOut;
        }
    }
}
