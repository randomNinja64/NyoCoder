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

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = GetCurlPath(),
                    Arguments = "-s -X GET"
                        + " -H \"Accept: application/json\""
                        + authHeader
                        + " \"" + fullUrl + "\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process process = Process.Start(psi))
                {
                    string output = "";
                    string error = "";
                    Thread errThread = new Thread(() => { try { error = process.StandardError.ReadToEnd(); } catch { } });
                    errThread.IsBackground = true;
                    errThread.Start();

                    output = process.StandardOutput.ReadToEnd();
                    errThread.Join(5000);
                    process.WaitForExit();
                    exitCode = process.ExitCode;

                    if (exitCode != 0 && string.IsNullOrEmpty(output))
                        return "cURL failed (exit " + exitCode + "): " + error;
                    return output;
                }
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
                using (NamedPipeServerStream pipeServer = new NamedPipeServerStream(
                    pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte))
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = GetCurlPath(),
                        Arguments = "-s -X POST"
                            + " -H \"Content-Type: application/json\""
                            + " -H \"Authorization: Bearer " + apiKey + "\""
                            + " --data-binary \"@\\\\.\\pipe\\" + pipeName + "\""
                            + " \"" + fullUrl + "\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    using (Process process = Process.Start(psi))
                    {
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

                        // Read stdout and stderr concurrently to avoid pipe-buffer deadlocks.
                        string output = "";
                        string error = "";
                        Thread errThread = new Thread(() => { try { error = process.StandardError.ReadToEnd(); } catch { } });
                        errThread.IsBackground = true;
                        errThread.Start();

                        output = process.StandardOutput.ReadToEnd();

                        pipeThread.Join(5000);
                        errThread.Join(5000);
                        process.WaitForExit();
                        exitCode = process.ExitCode;

                        if (exitCode != 0 && string.IsNullOrEmpty(output))
                            return "cURL failed (exit " + exitCode + "): " + error;
                        return output;
                    }
                }
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

                        Action<string> onReasoningChunk;
                        Action<int> onReasoningSummary;
                        SseStreamParser.CreateReasoningCallbacks(outputCallback, startBlock, out onReasoningChunk, out onReasoningSummary);

                        LLMClient.LLMCompletionResponse result = SseStreamParser.Parse(
                            process.StandardOutput, outputCallback, toolCallCallback, stopRequested,
                            onReasoningChunk: onReasoningChunk,
                            onReasoningSummary: onReasoningSummary,
                            startBlock: startBlock);

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
