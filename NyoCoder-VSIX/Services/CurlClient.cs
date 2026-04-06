using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

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
        /// Sends the LLM request using curl.exe via a Windows named pipe to avoid writing
        /// the JSON body to disk. Used as a fallback when .NET's HttpWebRequest fails on HTTPS.
        /// </summary>
        public static LLMClient.LLMCompletionResponse SendRequest(
            string serverUrl, string apiKey, JObject payload,
            Action<string> outputCallback,
            Action<ToolHandler.ToolCall> toolCallCallback,
            Func<bool> stopRequested)
        {
            // Use a Windows named pipe so curl can read the JSON body from memory
            // without touching disk, avoiding stdin pipe deadlock issues.
            string pipeName = "nyocoder_" + Guid.NewGuid().ToString("N");
            byte[] jsonBytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));

            try
            {
                using (NamedPipeServerStream pipeServer = new NamedPipeServerStream(
                    pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte))
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = GetCurlPath(),
                        Arguments = "-s -N -X POST"
                            + " -H \"Content-Type: application/json\""
                            + " -H \"Authorization: Bearer " + apiKey + "\""
                            + " --data-binary \"@\\\\.\\pipe\\" + pipeName + "\""
                            + " \"" + serverUrl + "/v1/chat/completions\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    using (Process process = Process.Start(psi))
                    {
                        // Serve the JSON body via the named pipe on a background thread
                        Thread pipeThread = new Thread(() =>
                        {
                            try
                            {
                                pipeServer.WaitForConnection();
                                pipeServer.Write(jsonBytes, 0, jsonBytes.Length);
                                pipeServer.Flush();
                                pipeServer.Close();
                            }
                            catch { }
                        });
                        pipeThread.IsBackground = true;
                        pipeThread.Start();

                        LLMClient.LLMCompletionResponse result = SseStreamParser.Parse(
                            process.StandardOutput, outputCallback, toolCallCallback, stopRequested);

                        pipeThread.Join(5000);
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            string stderr = process.StandardError.ReadToEnd();
                            return new LLMClient.LLMCompletionResponse(
                                "cURL failed (exit " + process.ExitCode + "): " + stderr,
                                null, "request_failed");
                        }

                        if (string.IsNullOrEmpty(result.Content) &&
                            string.IsNullOrEmpty(result.FinishReason) &&
                            result.ToolCalls.Count == 0)
                        {
                            string stderr = process.StandardError.ReadToEnd();
                            string detail = string.IsNullOrEmpty(stderr) ? "no response data" : stderr.Trim();
                            return new LLMClient.LLMCompletionResponse(
                                "cURL returned no response: " + detail,
                                null, "request_failed");
                        }

                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return new LLMClient.LLMCompletionResponse(
                    "cURL fallback failed: " + ex.Message, null, "request_failed");
            }
        }

    }
}
