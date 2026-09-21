using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace NyoCoder
{
public partial class LLMClient
{
    private LLMCompletionResponse SendHttpRequest(JObject payload, Action<string> outputCallback = null, Action<ToolHandler.ToolCall> toolCallCallback = null, Func<bool> stopRequested = null, Action startBlock = null)
    {
        LLMCompletionResponse completionResponse = new LLMCompletionResponse
        {
            Content = string.Empty,
            ToolCalls = new List<ToolHandler.ToolCall>(),
            FinishReason = string.Empty
        };

        try
        {
            if (stopRequested != null && stopRequested())
            {
                completionResponse.FinishReason = "stopped";
                return completionResponse;
            }

            var request = (HttpWebRequest)WebRequest.Create(llmEndpoint + "/v1/chat/completions");
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", "Bearer " + apiKey);

            byte[] payloadBytes = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            request.ContentLength = payloadBytes.Length;

            using (var stream = request.GetRequestStream())
            {
                stream.Write(payloadBytes, 0, payloadBytes.Length);
            }

            using (var httpResponse = (HttpWebResponse)request.GetResponse())
            using (var responseStream = httpResponse.GetResponseStream())
            using (var reader = new StreamReader(responseStream, Encoding.UTF8))
            {
                Action<string> onReasoningChunk;
                Action<int> onReasoningSummary;
                SseStreamParser.CreateReasoningCallbacks(outputCallback, startBlock, out onReasoningChunk, out onReasoningSummary);

                completionResponse = SseStreamParser.Parse(
                    reader, outputCallback, toolCallCallback, stopRequested,
                    () => { try { request.Abort(); } catch { } },
                    onReasoningChunk: onReasoningChunk,
                    onReasoningSummary: onReasoningSummary,
                    startBlock: startBlock);
            }
        }
        catch (Exception ex)
        {
            if (stopRequested != null && stopRequested())
            {
                completionResponse.FinishReason = "stopped";
                return completionResponse;
            }

            // Try curl fallback for HTTPS connection errors
            if (TlsCurlFallback.CanAttempt(llmEndpoint, CurlClient.GetCurlPath(), ex))
            {
                return CurlClient.SendRequest(llmEndpoint, apiKey, payload, outputCallback, toolCallCallback, stopRequested, startBlock);
            }

            string errorMsg = "Error sending request: " + ex.Message;
            if (outputCallback != null)
            {
                if (startBlock != null) startBlock();
                outputCallback(errorMsg);
            }
            else
                Console.Error.WriteLine(errorMsg);

            return new LLMCompletionResponse("", null, "request_failed");
        }

        return completionResponse;
    }
}
}
