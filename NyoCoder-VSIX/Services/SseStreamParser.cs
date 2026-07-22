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
        /// <param name="startBlock">Optional action that pads the chat output to a blank line before a new block starts.</param>
        public static LLMClient.LLMCompletionResponse Parse(
            TextReader reader,
            Action<string> outputCallback,
            Action<ToolHandler.ToolCall> toolCallCallback,
            Func<bool> stopRequested,
            Action onStop = null,
            Action<string> onReasoningChunk = null,
            Action startBlock = null)
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
            bool inReasoning = false;
            bool reasoningEndedWithNewline = true;
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
                    if (outputCallback != null)
                    {
                        if (startBlock != null) startBlock();
                        outputCallback("[API Error] " + jsonPart.Trim() + "\n");
                    }
                    else
                        Console.Write("\n[API Error] " + jsonPart.Trim() + "\n");
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

                        // Stream reasoning content (e.g. from DeepSeek-R1 or compatible models)
                        string reasoningChunk = delta != null
                            ? ((string)delta["reasoning_content"] ?? (string)delta["reasoning"])
                            : null;
                        if (!string.IsNullOrEmpty(reasoningChunk))
                        {
                            if (onReasoningChunk != null)
                            {
                                if (!inReasoning)
                                {
                                    if (startBlock != null) startBlock();
                                    onReasoningChunk("[thinking]\n");
                                    inReasoning = true;
                                }
                                onReasoningChunk(reasoningChunk);
                                reasoningEndedWithNewline = reasoningChunk.EndsWith("\n");
                            }
                            else if (!inReasoning)
                            {
                                inReasoning = true;
                            }
                        }

                        string content = delta != null ? (string)delta["content"] : null;
                        if (!string.IsNullOrEmpty(content))
                        {
                            if (inReasoning)
                            {
                                if (onReasoningChunk != null)
                                {
                                    // If the last reasoning chunk didn't end with a newline, the
                                    // closing tag needs one of its own so it doesn't glue onto
                                    // the trailing sentence.
                                    onReasoningChunk(reasoningEndedWithNewline ? "[/thinking]\n" : "\n[/thinking]\n");
                                    // Blank line between the thinking block and the text that follows
                                    if (startBlock != null) startBlock();
                                }
                                inReasoning = false;
                            }
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
                            // Close thinking before tool-call UI streams, otherwise the
                            // trailing ")" lands after [/thinking] at end-of-stream.
                            if (inReasoning)
                            {
                                if (onReasoningChunk != null)
                                {
                                    onReasoningChunk(reasoningEndedWithNewline ? "[/thinking]\n" : "\n[/thinking]\n");
                                    if (startBlock != null) startBlock();
                                }
                                inReasoning = false;
                            }

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

            // Close any open reasoning block if the stream ended while still in reasoning
            if (inReasoning && onReasoningChunk != null)
                onReasoningChunk(reasoningEndedWithNewline ? "[/thinking]\n" : "\n[/thinking]\n");

            response.ToolCalls.AddRange(partialToolCalls.Values);
            response.Content = output.ToString();

            if (toolCallCallback != null && partialToolCalls.Count > 0 && outputCallback != null)
                outputCallback(")\n");

            return response;
        }
    }
}
