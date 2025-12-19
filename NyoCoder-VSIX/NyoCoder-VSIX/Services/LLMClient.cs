using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows.Forms;
using NyoCoder;

namespace NyoCoder
{
public class LLMClient
{
    private readonly string llmEndpoint;
    private readonly string apiKey;
    private readonly string model;
    private readonly string systemPrompt;

    public LLMClient(string llmEndpoint, string key, string mdl, string sysprompt)
    {
        this.llmEndpoint = llmEndpoint;
        this.apiKey = key;
        this.model = mdl;
        this.systemPrompt = sysprompt;

        // Enable modern TLS protocols for HTTPS support
        // .NET 4.0 only has named constant for Tls (1.0)
        // Tls11 = 768, Tls12 = 3072 (numeric values used until .NET 4.5+)
        // We use |= to ADD to existing protocols rather than replacing them
        // This ensures fallback to older protocols if newer ones aren't available
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls | (SecurityProtocolType)768 | (SecurityProtocolType)3072;
        }
        catch
        {
            // If setting TLS protocols fails, continue with system defaults
            // This can happen on very old systems without TLS 1.2 support
        }
    }

    // Struct for chat messages
    public struct ChatMessage
    {
        public string Role;
        public string Content;
        public string Image;
        public List<ToolHandler.ToolCall> ToolCalls;
        public string ToolCallId;

        public ChatMessage(string role, string content, string toolCallId = "")
        {
            Role = role;
            Content = content;
            ToolCallId = toolCallId;
            Image = null;
            ToolCalls = new List<ToolHandler.ToolCall>();
        }
    }

    public struct LLMCompletionResponse
    {
        public string Content;
        public List<ToolHandler.ToolCall> ToolCalls;
        public string FinishReason;

        public LLMCompletionResponse(string content, List<ToolHandler.ToolCall> toolCalls, string finishReason)
        {
            Content = content;
            ToolCalls = toolCalls ?? new List<ToolHandler.ToolCall>();
            FinishReason = finishReason;
        }
    }

    public void ProcessConversation(
        List<ChatMessage> conversation,
        string userMessage,
        string image,
        string assistantName,
        List<string> toolsRequiringApproval,
        bool outputOnly,
        bool showToolOutput,
        Action<string> outputCallback = null,
        Func<string, string, ApprovalResult> approvalCallback = null,
        Func<bool> stopRequested = null)
    {
        // Default tools requiring approval if none specified
        if (toolsRequiringApproval == null)
        {
            toolsRequiringApproval = new List<string>
            {
                "run_shell_command",
                "write_file",
                "move_file",
                "delete_file"
            };
        }

        // Add user message
        ChatMessage userMsg = new ChatMessage
        {
            Role = "user",
            Content = userMessage,
            Image = image
        };
        conversation.Add(userMsg);

        while (true)
        {
            if (stopRequested != null && stopRequested())
            {
                if (outputCallback != null)
                {
                    outputCallback("\n[Session stopped by user]\n");
                }
                return;
            }

            if (!outputOnly && outputCallback == null)
            {
                Console.WriteLine();
                Console.Write(assistantName + ": ");
            }

            // Simple callback to stream tool calls as they're generated
            Action<ToolHandler.ToolCall> toolCallStreamCallback = null;
            if (outputCallback != null)
            {
                toolCallStreamCallback = (toolCall) =>
                {
                    if (!string.IsNullOrEmpty(toolCall.Name) && string.IsNullOrEmpty(toolCall.Arguments))
                    {
                        // Show tool name when we first see it (no leading newline - spacing handled by context)
                        outputCallback("\n[tool call] " + toolCall.Name + "(");
                    }
                    else if (!string.IsNullOrEmpty(toolCall.Arguments))
                    {
                        // Stream argument chunks as they come in
                        outputCallback(toolCall.Arguments);
                    }
                };
            }

            LLMCompletionResponse response = sendMessages(conversation, outputCallback, toolCallStreamCallback, stopRequested);

            if ((stopRequested != null && stopRequested()) || response.FinishReason == "stopped")
            {
                if (outputCallback != null)
                {
                    outputCallback("\n[Session stopped by user]\n");
                }
                return;
            }

            if (response.ToolCalls != null && response.ToolCalls.Count > 0)
            {
                // Add assistant tool call message
                ChatMessage assistantCall = new ChatMessage
                {
                    Role = "assistant",
                    Content = string.Empty,
                    ToolCalls = response.ToolCalls
                };
                conversation.Add(assistantCall);

                for (int i = 0; i < response.ToolCalls.Count; i++)
                {
                    ToolHandler.ToolCall call = response.ToolCalls[i];

                    int exitCode = 0;
                    string toolContent;
                    ApprovalResult approvalResult = ApprovalResult.Approved;

                    if (stopRequested != null && stopRequested())
                    {
                        if (outputCallback != null)
                        {
                            outputCallback("\n[Session stopped by user]\n");
                        }
                        return;
                    }

                    // Check if tool requires approval
                    if (toolsRequiringApproval.Contains(call.Name))
                    {
                        // Parse escape sequences for better display formatting
                        string formattedArguments = call.Arguments
                            .Replace("\\n", "\n")
                            .Replace("\\r", "\r")
                            .Replace("\\t", "\t")
                            .Replace("\\\"", "\"")
                            .Replace("\\'", "'")
                            .Replace("\\\\", "\\");
                        
                        // Use approval callback if provided, otherwise fall back to MessageBox
                        if (approvalCallback != null)
                        {
                            approvalResult = approvalCallback(call.Name, formattedArguments);
                        }
                        else
                        {
                            string approvalMessage = "Run tool: " + call.Name + "\n\nWith arguments:\n" + formattedArguments;
                            
                            DialogResult result = MessageBox.Show(
                                approvalMessage,
                                "NyoCoder - Approve Tool?",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question,
                                MessageBoxDefaultButton.Button2
                            );
                            
                            approvalResult = (result == DialogResult.Yes) ? ApprovalResult.Approved : ApprovalResult.Rejected;
                        }

                        if (approvalResult == ApprovalResult.Stopped)
                        {
                            // User stopped the session - break out of the conversation loop
                            if (outputCallback != null)
                            {
                                outputCallback("\n[Session stopped by user]\n");
                            }
                            return; // Exit ProcessConversation
                        }
                        else if (approvalResult == ApprovalResult.Rejected)
                        {
                            // User declined - return cancellation message
                            exitCode = -1;
                            toolContent = ToolHandler.FormatCommandResult(
                                call.Name,
                                "Tool execution was cancelled by the user.",
                                exitCode
                            );
                        }
                    else
                    {
                        // User approved - execute the tool
                        ToolHandler.ExecuteToolCall(call, out toolContent, out exitCode);
                    }
                }
                else
                {
                    // Execute the requested tool and capture its output
                    ToolHandler.ExecuteToolCall(call, out toolContent, out exitCode);
                }

                    ChatMessage toolMsg = new ChatMessage
                    {
                        Role = "tool",
                        Content = toolContent,
                        ToolCallId = call.Id
                    };
                    conversation.Add(toolMsg);

                    // Output tool result
                    if (outputCallback != null && showToolOutput)
                    {
                        outputCallback("\n[tool output]\n" + toolContent + "\n");
                    }
                    else if (!outputOnly)
                    {
                        Console.WriteLine("[tool output]");
                        if (showToolOutput)
                        {
                            Console.Write(toolContent);
                            if (!toolContent.EndsWith("\n"))
                            {
                                Console.WriteLine();
                            }
                        }
                        else
                        {
                            Console.WriteLine("Exit Code: " + exitCode);
                        }
                    }
                }

                // Run loop again so assistant can ingest tool output
                continue;
            }

            // Add assistant message
            ChatMessage assistantMsg = new ChatMessage
            {
                Role = "assistant",
                Content = response.Content
            };
            conversation.Add(assistantMsg);
            
            if (!outputOnly && outputCallback == null)
            {
                Console.WriteLine(); // Add newline after assistant response
            }
            break;
        }
    }

    private JObject BuildMessageObject(ChatMessage msg)
    {
        JObject msgObj = new JObject();
        msgObj["role"] = msg.Role;

        if (!string.IsNullOrEmpty(msg.ToolCallId))
            msgObj["tool_call_id"] = msg.ToolCallId;

        if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
        {
            msgObj["content"] = msg.Content ?? "";
            JArray toolCallsArray = new JArray();

            foreach (var call in msg.ToolCalls)
            {
                JObject toolObj = new JObject();
                toolObj["id"] = call.Id ?? "";
                toolObj["type"] = "function";

                JObject functionObj = new JObject();
                functionObj["name"] = call.Name ?? "";
                functionObj["arguments"] = call.Arguments ?? "";

                toolObj["function"] = functionObj;
                toolCallsArray.Add(toolObj);
            }

            msgObj["tool_calls"] = toolCallsArray;
        }
        else if (msg.Image != null)
        {
            JArray contentArray = new JArray();

            if (!string.IsNullOrEmpty(msg.Content))
            {
                JObject textPart = new JObject();
                textPart["type"] = "text";
                textPart["text"] = msg.Content;
                contentArray.Add(textPart);
            }

            if (!string.IsNullOrEmpty(msg.Image))
            {
                JObject imgPart = new JObject();
                imgPart["type"] = "image_url";
                JObject imageUrl = new JObject();
                imageUrl["url"] = "data:image/png;base64," + msg.Image;
                imgPart["image_url"] = imageUrl;
                contentArray.Add(imgPart);
            }

            if (contentArray.Count == 0)
            {
                JObject emptyText = new JObject();
                emptyText["type"] = "text";
                emptyText["text"] = "";
                contentArray.Add(emptyText);
            }

            msgObj["content"] = contentArray;
        }
        else
        {
            msgObj["content"] = msg.Content ?? "";
        }

        return msgObj;
    }

    LLMCompletionResponse sendMessages(List<ChatMessage> conversation, Action<string> outputCallback = null, Action<ToolHandler.ToolCall> toolCallCallback = null, Func<bool> stopRequested = null)
    {
        // Build payload
        JObject payload = new JObject();
        payload["model"] = model;

        // Messages
        JArray messages = new JArray();

        // System message
        JObject systemMsg = new JObject();
        systemMsg["role"] = "system";
        systemMsg["content"] = systemPrompt;
        messages.Add(systemMsg);

        // Process all user messages in the conversation list
        if (conversation != null)
        {
            foreach (var msg in conversation)
            {
                messages.Add(BuildMessageObject(msg));
            }
        }

        payload["messages"] = messages;

        // Always add all tools
        JArray toolsArray = ToolDefinitions.BuildToolsArray();
        payload["tools"] = toolsArray;

        payload["stream"] = true;

        if (stopRequested != null && stopRequested())
        {
            return new LLMCompletionResponse("", new List<ToolHandler.ToolCall>(), "stopped");
        }

        return SendHttpRequest(payload, outputCallback, toolCallCallback, stopRequested);
    }

    /// <summary>
    /// Sends a simple prompt to the LLM and streams the response to the output callback.
    /// </summary>
    public void SendPrompt(string userPrompt, Action<string> outputCallback)
    {
        if (outputCallback == null)
            throw new ArgumentNullException("outputCallback");

        List<ChatMessage> conversation = new List<ChatMessage>();
        ChatMessage userMsg = new ChatMessage("user", userPrompt);
        conversation.Add(userMsg);

        sendMessages(conversation, outputCallback);
    }

    private LLMCompletionResponse SendHttpRequest(JObject payload, Action<string> outputCallback = null, Action<ToolHandler.ToolCall> toolCallCallback = null, Func<bool> stopRequested = null)
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
                string line;
                StringBuilder output = new StringBuilder();

                // ✅ accumulate tool call argument chunks across deltas
                Dictionary<int, ToolHandler.ToolCall> partialToolCalls = new Dictionary<int, ToolHandler.ToolCall>();
                Dictionary<int, int> toolCallArgumentLength = new Dictionary<int, int>(); // Track how much we've already streamed
                string lastEvent = null; // Track the last event type for error handling

                while ((line = reader.ReadLine()) != null)
                {
                    if (stopRequested != null && stopRequested())
                    {
                        try { request.Abort(); } catch { }
                        completionResponse.FinishReason = "stopped";
                        return completionResponse;
                    }

                    if (line.StartsWith("data: "))
                    {
                        string jsonPart = line.Substring(6);
                        if (jsonPart == "[DONE]") break;

                        // Check if this is an error response (either from event: error or error object in data)
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
                        
                        lastEvent = null; // Reset event type after processing

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
                                    completionResponse.FinishReason = finishReason;

                                JArray toolCalls = delta != null ? (JArray)delta["tool_calls"] : null;
                                if (toolCalls != null)
                                {
                                    foreach (JObject call in toolCalls)
                                    {
                                        JToken indexToken = call["index"];
                                        int index = indexToken != null ? indexToken.Value<int>() : 0;
                                        string id = (string)call["id"];
                                        JObject function = (JObject)call["function"];

                                        if (!partialToolCalls.ContainsKey(index))
                                        {
                                            partialToolCalls[index] = new ToolHandler.ToolCall
                                            {
                                                Id = "",
                                                Name = "",
                                                Arguments = ""
                                            };
                                            toolCallArgumentLength[index] = 0;
                                        }

                                        var temp = partialToolCalls[index];

                                        if (!string.IsNullOrEmpty(id))
                                        {
                                            temp.Id = id;
                                        }

                                        if (function != null)
                                        {
                                            string name = (string)function["name"];
                                            string argsChunk = (string)function["arguments"];

                                            if (!string.IsNullOrEmpty(name))
                                            {
                                                temp.Name = name;
                                                // Show tool call when we first see the name
                                                if (toolCallCallback != null && toolCallArgumentLength[index] == 0)
                                                {
                                                    toolCallCallback(new ToolHandler.ToolCall(name, "", ""));
                                                }
                                            }

                                            if (!string.IsNullOrEmpty(argsChunk))
                                            {
                                                temp.Arguments += argsChunk;
                                                // Stream only new chunks as they arrive
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
                }

                // finalize tool calls after stream ends
                completionResponse.ToolCalls.AddRange(partialToolCalls.Values);
                completionResponse.Content = output.ToString();
                
                // Close any open tool call parentheses
                if (toolCallCallback != null && partialToolCalls.Count > 0)
                {
                    outputCallback(")\n");
                }
            }
        }
        catch (Exception ex)
        {
            if (stopRequested != null && stopRequested())
            {
                completionResponse.FinishReason = "stopped";
                return completionResponse;
            }

            string errorMsg = "Error sending request: " + ex.Message + "\n";
            if (outputCallback != null)
                outputCallback(errorMsg);
            else
                Console.Error.WriteLine(errorMsg);

            return new LLMCompletionResponse("", null, "request_failed");
        }

        return completionResponse;
    }
}
}
