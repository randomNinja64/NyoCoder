using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NyoCoder
{
    internal static class SseStreamParser
    {
        /// <summary>
        /// Parses an SSE stream into an LLMCompletionResponse.
        /// Handles content streaming, tool call accumulation, error events, and stop requests.
        /// </summary>
        /// <param name="reader">The stream to read SSE data from.</param>
        /// <param name="outputCallback">Called with each content chunk as it arrives.</param>
        /// <param name="toolCallCallback">Called when a tool call name or argument chunk is seen.</param>
        /// <param name="stopRequested">Returns true when the caller wants to abort early.</param>
        /// <param name="onStop">Optional action invoked when a stop is detected mid-stream (e.g. abort the HTTP request).</param>
        public static LLMClient.LLMCompletionResponse Parse(
            TextReader reader,
            Action<string> outputCallback,
            Action<ToolHandler.ToolCall> toolCallCallback,
            Func<bool> stopRequested,
            Action onStop = null)
        {
            LLMClient.LLMCompletionResponse response = new LLMClient.LLMCompletionResponse
            {
                Content = string.Empty,
                ToolCalls = new List<ToolHandler.ToolCall>(),
                FinishReason = string.Empty
            };

            StringBuilder output = new StringBuilder();
            Dictionary<int, ToolHandler.ToolCall> partialToolCalls = new Dictionary<int, ToolHandler.ToolCall>();
            Dictionary<int, int> toolCallArgumentLength = new Dictionary<int, int>();
            string lastEvent = null;
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                if (stopRequested != null && stopRequested())
                {
                    if (onStop != null) onStop();
                    response.FinishReason = "stopped";
                    response.Content = output.ToString();
                    response.ToolCalls.AddRange(partialToolCalls.Values);
                    return response;
                }

                if (line.StartsWith("event: "))
                {
                    lastEvent = line.Substring(7).Trim();
                    continue;
                }

                if (!line.StartsWith("data: ")) continue;

                string jsonPart = line.Substring(6);
                if (jsonPart == "[DONE]") break;

                if (lastEvent == "error" || jsonPart.Contains("\"error\""))
                {
                    string errorMsg = "[API Error] " + jsonPart.Trim() + "\n";
                    if (outputCallback != null)
                        outputCallback(errorMsg);
                    else
                        Console.Write(errorMsg);
                    lastEvent = null;
                    continue;
                }

                lastEvent = null;

                try
                {
                    JObject obj = JObject.Parse(jsonPart);
                    JArray choices = (JArray)obj["choices"];
                    if (choices == null) continue;

                    foreach (JObject choice in choices)
                    {
                        JObject delta = (JObject)choice["delta"];
                        string content = delta != null ? (string)delta["content"] : null;
                        if (!string.IsNullOrEmpty(content))
                        {
                            if (outputCallback != null)
                                outputCallback(content);
                            else
                                Console.Write(content);
                            output.Append(content);
                        }

                        string finishReason = (string)choice["finish_reason"];
                        if (!string.IsNullOrEmpty(finishReason))
                            response.FinishReason = finishReason;

                        JArray toolCalls = delta != null ? (JArray)delta["tool_calls"] : null;
                        if (toolCalls != null)
                        {
                            foreach (JObject call in toolCalls)
                            {
                                int index = call["index"] != null ? call["index"].Value<int>() : 0;
                                string id = (string)call["id"];
                                JObject function = (JObject)call["function"];

                                if (!partialToolCalls.ContainsKey(index))
                                {
                                    partialToolCalls[index] = new ToolHandler.ToolCall { Id = "", Name = "", Arguments = "" };
                                    toolCallArgumentLength[index] = 0;
                                }

                                ToolHandler.ToolCall temp = partialToolCalls[index];

                                if (!string.IsNullOrEmpty(id))
                                    temp.Id = id;

                                if (function != null)
                                {
                                    string name = (string)function["name"];
                                    string argsChunk = (string)function["arguments"];

                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        temp.Name = name;
                                        if (toolCallCallback != null && toolCallArgumentLength[index] == 0)
                                            toolCallCallback(new ToolHandler.ToolCall(name, "", ""));
                                    }

                                    if (!string.IsNullOrEmpty(argsChunk))
                                    {
                                        temp.Arguments += argsChunk;
                                        if (toolCallCallback != null && !string.IsNullOrEmpty(temp.Name))
                                        {
                                            int alreadyStreamed = toolCallArgumentLength[index];
                                            if (temp.Arguments.Length > alreadyStreamed)
                                            {
                                                string newChunk = temp.Arguments.Substring(alreadyStreamed);
                                                toolCallCallback(new ToolHandler.ToolCall(temp.Name, newChunk, temp.Id));
                                                toolCallArgumentLength[index] = temp.Arguments.Length;
                                            }
                                        }
                                    }
                                }

                                partialToolCalls[index] = temp;
                            }
                        }
                    }
                }
                catch
                {
                    // ignore malformed JSON fragments
                }
            }

            response.ToolCalls.AddRange(partialToolCalls.Values);
            response.Content = output.ToString();

            if (toolCallCallback != null && partialToolCalls.Count > 0 && outputCallback != null)
                outputCallback(")\n");

            return response;
        }
    }
}
