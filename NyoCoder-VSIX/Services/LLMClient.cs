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

namespace NyoCoder
{
public class LLMClient
{
    private readonly string llmEndpoint;
    private readonly string apiKey;
    private readonly string model;
    private readonly string systemPrompt;

    /// <summary>
    /// The conversation history for this LLM client instance.
    /// </summary>
    public List<ChatMessage> Conversation { get; set; }

    public LLMClient(string llmEndpoint, string key, string mdl, string sysprompt)
    {
        this.llmEndpoint = llmEndpoint;
        this.apiKey = key;
        this.model = mdl;
        this.systemPrompt = sysprompt;
        this.Conversation = new List<ChatMessage>();

        // Enable modern TLS protocols for HTTPS support
        // .NET 4.0 only has named constant for Tls (1.0)
        // Tls11 = 768, Tls12 = 3072 (numeric values used until .NET 4.5+)
        // We use |= to ADD to existing protocols rather than replacing them
        // This ensures fallback to older protocols if newer ones aren't available
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls | (SecurityProtocolType)768 | (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
        }
        catch
        {
            // If setting TLS protocols fails, continue with system defaults
            // This can happen on very old systems without TLS 1.2 support
        }
    }

    /// <summary>
    /// Creates a new LLMClient instance from configuration.
    /// Validates that the LLM Server is configured and shows a message box if invalid.
    /// </summary>
    /// <returns>New LLMClient instance if configuration is valid, null otherwise.</returns>
    public static LLMClient CreateFromConfig()
    {
        // Get configuration
        string apiKey = ConfigHandler.GetApiKey();
        string llmServer = ConfigHandler.GetLlmServer();
        string model = ConfigHandler.GetModel();

        // Validate configuration - only LLM Server is required
        if (string.IsNullOrWhiteSpace(llmServer))
        {
            MessageBox.Show(
                "Please configure the LLM Server in Tools > NyoCoder Options...",
                "NyoCoder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }

        // Use empty string for optional values if not provided
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = "";
        }
        if (string.IsNullOrWhiteSpace(model))
        {
            model = "";
        }

        // Create and return new LLM client
        return new LLMClient(llmServer, apiKey, model, ContextEngine.AgentSystemPrompt);
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
        string userMessage,
        string image,
        string assistantName,
        bool showToolOutput,
        Action<string> outputCallback = null,
        Func<string, string, ApprovalResult> approvalCallback = null,
        Func<bool> stopRequested = null,
        Action<int> onSummarized = null,
        ChatMode mode = ChatMode.Agent,
        Func<string> dequeueSteerMessage = null)
    {
        // Add user message
        ChatMessage userMsg = new ChatMessage
        {
            Role = "user",
            Content = userMessage,
            Image = image
        };
        this.Conversation.Add(userMsg);

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

            LLMCompletionResponse response = sendMessages(this.Conversation, outputCallback, toolCallStreamCallback, stopRequested, mode);

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
                this.Conversation.Add(assistantCall);

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

                    // Pre-execution approval (file-edit tools approve after diff preview instead)
                    if (ConfigHandler.RequiresApprovalBeforeExecute(call.Name))
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
                    this.Conversation.Add(toolMsg);

                    // Output tool result
                    if (outputCallback != null && showToolOutput)
                    {
                        outputCallback("\n[tool output]\n" + toolContent + "\n");
                    }

                }

                if (InjectPendingSteerMessages(dequeueSteerMessage, outputCallback))
                {
                    if (outputCallback != null)
                        outputCallback("\n\nAssistant: ");
                    continue;
                }

                // Check if we need to summarize before the next LLM call
                if (ShouldSummarize(GetConversationCharacterCount(this.Conversation)))
                {
                    if (outputCallback != null)
                    {
                        outputCallback("\n[Context usage high - summarizing conversation...]\n");
                    }
                    
                    string summary = SummarizeConversation(this.Conversation, outputCallback);
                    
                    if (!string.IsNullOrEmpty(summary))
                    {
                        // Replace conversation with summary
                        this.Conversation.Clear();
                        this.Conversation.Add(new ChatMessage("user", 
                            "[Previous conversation summary]\n" + summary + 
                            "\n\n[Continue from this context. The user's original request is being processed.]"));
                        
                        if (outputCallback != null)
                        {
                            outputCallback("\n[Conversation summarized - continuing...]\n\n");
                        }
                        
                        // Notify UI to reset character count
                        if (onSummarized != null)
                        {
                            onSummarized(GetConversationCharacterCount(this.Conversation));
                        }
                    }
                }

                // Check if a plan was just created — break so the caller can orchestrate step execution
                if (StepPlanner.Instance != null && StepPlanner.Instance.PlanRequiresExecution)
                {
                    if (outputCallback != null)
                    {
                        outputCallback("\n[Plan created — executing steps...]\n");
                    }
                    break;
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
            this.Conversation.Add(assistantMsg);

            if (InjectPendingSteerMessages(dequeueSteerMessage, outputCallback))
            {
                if (outputCallback != null)
                    outputCallback("\n\nAssistant: ");
                continue;
            }

            break;
        }
    }

    /// <summary>
    /// Injects queued steering messages as user turns. Returns true if any were injected.
    /// Must only be called when the conversation is in a valid state (after a full tool
    /// batch or after an assistant text message — never between tool_call and tool results).
    /// </summary>
    private bool InjectPendingSteerMessages(Func<string> dequeueSteer, Action<string> outputCallback)
    {
        if (dequeueSteer == null)
            return false;

        bool injected = false;
        string msg;
        while (!string.IsNullOrEmpty(msg = dequeueSteer()))
        {
            injected = true;
            this.Conversation.Add(new ChatMessage("user", msg));
            if (outputCallback != null)
                outputCallback("\n[steer] User: " + msg + "\n");
        }

        return injected;
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

    LLMCompletionResponse sendMessages(List<ChatMessage> conversation, Action<string> outputCallback = null, Action<ToolHandler.ToolCall> toolCallCallback = null, Func<bool> stopRequested = null, ChatMode mode = ChatMode.Agent)
    {
        // Build payload
        JObject payload = new JObject();
        payload["model"] = model;

        // Messages
        JArray messages = new JArray();

        // System message — includes mode-specific instructions
        JObject systemMsg = new JObject();
        systemMsg["role"] = "system";
        systemMsg["content"] = ContextEngine.GetSystemPrompt(mode);
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

        // Add tools filtered by mode (Plan mode = read-only tools only)
        JArray toolsArray = ToolDefinitions.BuildToolsArray(mode);
        payload["tools"] = toolsArray;

        payload["stream"] = true;

        if (stopRequested != null && stopRequested())
        {
            return new LLMCompletionResponse("", new List<ToolHandler.ToolCall>(), "stopped");
        }

        return SendHttpRequest(payload, outputCallback, toolCallCallback, stopRequested);
    }

    /// <summary>
    /// Checks if summarization is needed based on total character count and context window size.
    /// Returns true if usage exceeds 90% and context window is configured.
    /// </summary>
    /// <param name="characterCount">Total character count (excluding base overhead).</param>
    /// <returns>True if summarization is needed, false otherwise.</returns>
    private static bool ShouldSummarize(int characterCount)
    {
        int? contextWindowSize = ConfigHandler.ContextWindowSize;
        if (!contextWindowSize.HasValue || contextWindowSize.Value <= 0)
            return false;

        int approximateTokens = ContextEngine.ApproximateTokens(characterCount);
        double usage = (double)approximateTokens / contextWindowSize.Value;
        
        return usage >= 0.90; // 90% threshold
    }

    /// <summary>
    /// Calculates the total character count of a conversation.
    /// </summary>
    internal int GetConversationCharacterCount(List<ChatMessage> conversation)
    {
        int count = 0;
        foreach (var msg in conversation)
        {
            if (!string.IsNullOrEmpty(msg.Content))
            {
                count += msg.Content.Length;
            }
            if (msg.ToolCalls != null)
            {
                foreach (var toolCall in msg.ToolCalls)
                {
                    if (!string.IsNullOrEmpty(toolCall.Name))
                        count += toolCall.Name.Length;
                    if (!string.IsNullOrEmpty(toolCall.Arguments))
                        count += toolCall.Arguments.Length;
                }
            }
        }
        return count;
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

    /// <summary>
    /// Summarizes the current conversation to reduce context usage.
    /// Appends a summary request to the conversation, gets the summary, and returns it.
    /// </summary>
    public string SummarizeConversation(List<ChatMessage> conversation, Action<string> outputCallback = null)
    {
        if (conversation == null || conversation.Count == 0)
            return string.Empty;

        // Add a request for summary to the existing conversation
        List<ChatMessage> summaryConversation = new List<ChatMessage>(conversation);
        summaryConversation.Add(new ChatMessage("user", 
            "Please provide a concise summary of our conversation so far. " +
            "Focus on: what was requested, what actions were taken (files, commands), " +
            "current state, and any pending tasks. Include key details like file paths."));

        StringBuilder summary = new StringBuilder();
        
        // Use regular sendMessages - we just want the text content, ignore any tool calls
        sendMessages(summaryConversation, (text) =>
        {
            summary.Append(text);
            if (outputCallback != null)
            {
                outputCallback(text);
            }
        }, null, null);

        return summary.ToString();
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
                completionResponse = SseStreamParser.Parse(
                    reader, outputCallback, toolCallCallback, stopRequested,
                    () => { try { request.Abort(); } catch { } },
                    onReasoningChunk: outputCallback);
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
            string curlPath = CurlClient.GetCurlPath();
            if (llmEndpoint.StartsWith("https:", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(curlPath) && ShouldFallbackToCurl(ex))
            {
                return CurlClient.SendRequest(llmEndpoint, apiKey, payload, outputCallback, toolCallCallback, stopRequested);
            }

            string errorMsg = "Error sending request: " + ex.Message;
            if (outputCallback != null)
                outputCallback(errorMsg);
            else
                Console.Error.WriteLine(errorMsg);

            return new LLMCompletionResponse("", null, "request_failed");
        }

        return completionResponse;
    }

    private static bool ShouldFallbackToCurl(Exception ex)
    {
        WebException webEx = ex as WebException;
        if (webEx != null)
            return webEx.Status == WebExceptionStatus.SecureChannelFailure
                || webEx.Status == WebExceptionStatus.TrustFailure
                || webEx.Status == WebExceptionStatus.ConnectFailure
                || webEx.Status == WebExceptionStatus.ConnectionClosed
                || webEx.Status == WebExceptionStatus.SendFailure
                || webEx.Status == WebExceptionStatus.ReceiveFailure
                || webEx.Status == WebExceptionStatus.Timeout
                || webEx.Status == WebExceptionStatus.ServerProtocolViolation
                || (webEx.InnerException != null &&
                    webEx.InnerException.GetType().Name.Contains("Authentication"));

        return ex.GetType().Name.Contains("Authentication")
            || ex.GetType().Name.Contains("Security")
            || ex.GetType().Name.Contains("IOException")
            || (ex.Message != null && ex.Message.Contains("connection"));
    }
}
}
