using System;
using System.Collections.Generic;
using System.Net;
using System.Windows.Forms;

namespace NyoCoder
{
public partial class LLMClient
{
    private readonly string llmEndpoint;
    private readonly string apiKey;
    private readonly string model;

    /// <summary>
    /// The conversation history for this LLM client instance.
    /// </summary>
    public List<ChatMessage> Conversation { get; set; }

    // Boolean flag that gets set when a file-modifying tool is executed
    public bool FilesModifiedThisTurn { get; private set; }

    public LLMClient(string llmEndpoint, string key, string mdl)
    {
        this.llmEndpoint = llmEndpoint;
        this.apiKey = key;
        this.model = mdl;
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
        OnboardingWizardForm.ShowIfNeeded();

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
        return new LLMClient(llmServer, apiKey, model);
    }

    // Struct for chat messages
    public struct ChatMessage
    {
        public string Role;
        public string Content;
        public string Image;
        public string ImageMime;
        public List<ToolHandler.ToolCall> ToolCalls;
        public string ToolCallId;

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
            ToolCallId = null;
            Image = null;
            ImageMime = null;
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
        Action<string> outputCallback = null,
        Func<string, string, ApprovalResult> approvalCallback = null,
        Func<bool> stopRequested = null,
        Action<int> onSummarized = null,
        string modeId = ModeIds.Agent,
        Func<string> dequeueSteerMessage = null,
        Action startBlock = null)
    {
        FilesModifiedThisTurn = false;

        ChatBlockDisplayMode toolCallDisplay = ConfigHandler.GetToolCallDisplayMode();
        ChatBlockDisplayMode toolOutputDisplay = ConfigHandler.GetToolOutputDisplayMode();

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
                    if (startBlock != null) startBlock();
                    outputCallback("[Session stopped by user]\n");
                }
                return;
            }

            // Check context usage before every LLM call — not just after tool batches.
            // This covers text-only turns, the first call of a turn, and subagent
            // step conversations whose prompts may already exceed the threshold.
            TrySummarizeConversation(outputCallback, onSummarized, startBlock);

            // Stream tool calls with explicit open/close markers for the chat UI.
            // Hidden: announce name only (no argument body); Expander starts expanded in ChatTurn.
            Action<ToolHandler.ToolCall> toolCallStreamCallback = null;
            bool toolCallUiOpen = false;
            if (outputCallback != null)
            {
                toolCallStreamCallback = (toolCall) =>
                {
                    if (!string.IsNullOrEmpty(toolCall.Name) && string.IsNullOrEmpty(toolCall.Arguments))
                    {
                        if (toolCallUiOpen)
                            outputCallback("[/tool call]\n");

                        if (startBlock != null) startBlock();
                        outputCallback("[tool call] " + toolCall.Name + "\n");
                        toolCallUiOpen = true;
                    }
                    else if (!string.IsNullOrEmpty(toolCall.Arguments)
                        && toolCallDisplay != ChatBlockDisplayMode.Hidden)
                    {
                        outputCallback(toolCall.Arguments);
                    }
                };
            }

            LLMCompletionResponse response = sendMessages(this.Conversation, outputCallback, toolCallStreamCallback, stopRequested, modeId, startBlock);

            if (toolCallUiOpen)
                outputCallback("[/tool call]\n");

            if ((stopRequested != null && stopRequested()) || response.FinishReason == "stopped")
            {
                if (outputCallback != null)
                {
                    if (startBlock != null) startBlock();
                    outputCallback("[Session stopped by user]\n");
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
                    string toolImage = null;
                    string toolImageMime = null;
                    ApprovalResult approvalResult = ApprovalResult.Approved;

                    if (stopRequested != null && stopRequested())
                    {
                        if (outputCallback != null)
                        {
                            if (startBlock != null) startBlock();
                            outputCallback("[Session stopped by user]\n");
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
                                if (startBlock != null) startBlock();
                                outputCallback("[Session stopped by user]\n");
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
                            ToolHandler.ExecuteToolCall(call, modeId, out toolContent, out exitCode, out toolImage, out toolImageMime);
                        }
                    }
                    else
                    {
                        // Execute the requested tool and capture its output
                        ToolHandler.ExecuteToolCall(call, modeId, out toolContent, out exitCode, out toolImage, out toolImageMime);
                    }

                    ChatMessage toolMsg = new ChatMessage
                    {
                        Role = "tool",
                        Content = toolContent,
                        ToolCallId = call.Id,
                        Image = toolImage,
                        ImageMime = toolImageMime
                    };
                    this.Conversation.Add(toolMsg);

                    if (exitCode == 0 && ToolDefinitions.IsFileModifyingTool(call.Name))
                        FilesModifiedThisTurn = true;

                    // Output tool result (Hidden = exit-code stub; Shown/Collapsed = full Expander markers)
                    if (outputCallback != null)
                    {
                        if (startBlock != null) startBlock();
                        if (toolOutputDisplay == ChatBlockDisplayMode.Hidden)
                        {
                            outputCallback("[tool output]\nExit Code: " + exitCode + "\n");
                        }
                        else
                        {
                            outputCallback("[tool output]\n" + (toolContent ?? "").TrimEnd() + "\n[/tool output]\n");
                        }
                    }

                }

                if (InjectPendingSteerMessages(dequeueSteerMessage, outputCallback, startBlock))
                {
                    if (outputCallback != null)
                    {
                        if (startBlock != null) startBlock();
                        outputCallback("Assistant: \n");
                    }
                    continue;
                }

                // Check if we need to summarize before the next LLM call
                TrySummarizeConversation(outputCallback, onSummarized, startBlock);

                // Check if a plan was just created — break so the caller can orchestrate step execution
                if (StepPlanner.Instance != null && StepPlanner.Instance.PlanRequiresExecution)
                {
                    if (outputCallback != null)
                    {
                        if (startBlock != null) startBlock();
                        outputCallback("[Plan created — executing steps...]\n");
                    }
                    break;
                }

                // Mid-turn manage_plan advanced/completed the current step — break so
                // StepExecutor can keep the last message and move to the next step.
                if (StepPlanner.Instance != null && StepPlanner.Instance.StepTurnEnded)
                    break;

                // Pad now so the next stream's first output (text or otherwise) starts on a
                // fresh block after the tool output; a following StartBlock becomes a no-op.
                if (outputCallback != null && startBlock != null)
                    startBlock();

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

            if (InjectPendingSteerMessages(dequeueSteerMessage, outputCallback, startBlock))
            {
                if (outputCallback != null)
                {
                    if (startBlock != null) startBlock();
                    outputCallback("Assistant: \n");
                }
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
    private bool InjectPendingSteerMessages(Func<string> dequeueSteer, Action<string> outputCallback, Action startBlock = null)
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
            {
                if (startBlock != null) startBlock();
                outputCallback("[steer] User: " + msg + "\n");
            }
        }

        return injected;
    }

    /// <summary>
    /// Checks context usage and, if over the threshold, summarizes the conversation
    /// in place and notifies the UI via <paramref name="onSummarized"/>.
    /// Safe to call before every LLM request.
    /// </summary>
    private void TrySummarizeConversation(Action<string> outputCallback, Action<int> onSummarized, Action startBlock)
    {
        if (!ShouldSummarize(GetConversationCharacterCount(this.Conversation)))
            return;

        if (outputCallback != null)
        {
            if (startBlock != null) startBlock();
            outputCallback("[Context usage high - summarizing conversation...]\n");
        }

        string summary = SummarizeConversation(this.Conversation);

        if (!string.IsNullOrEmpty(summary))
        {
            // Replace conversation with summary
            this.Conversation.Clear();
            this.Conversation.Add(new ChatMessage("user",
                "[Previous conversation summary]\n" + summary +
                "\n\n[Continue from this context. The user's original request is being processed.]"));

            if (outputCallback != null)
            {
                if (startBlock != null) startBlock();
                outputCallback("[Conversation summarized - continuing...]\n");
            }

            // Notify UI to reset character count
            if (onSummarized != null)
            {
                onSummarized(GetConversationCharacterCount(this.Conversation));
            }
        }
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
    /// Summarizes the current conversation to reduce context usage.
    /// Appends a summary request to the conversation, gets the summary, and returns it.
    /// The summary LLM call is silent (no chat streaming), tool-free, and omits skill/external-tool prompt injections.
    /// </summary>
    public string SummarizeConversation(List<ChatMessage> conversation)
    {
        if (conversation == null || conversation.Count == 0)
            return string.Empty;

        // Add a request for summary to the existing conversation
        List<ChatMessage> summaryConversation = new List<ChatMessage>(conversation);
        summaryConversation.Add(new ChatMessage("user", 
            "Please provide a concise summary of our conversation so far. " +
            "Focus on: what was requested, what actions were taken (files, commands), " +
            "current state, and any pending tasks. Include key details like file paths."));

        LLMCompletionResponse response = sendMessages(
            summaryConversation,
            outputCallback: null,
            toolCallCallback: null,
            stopRequested: null,
            modeId: ModeIds.Agent,
            startBlock: null,
            includeTools: false,
            includeContextInjections: false);

        return response.Content ?? string.Empty;
    }
}
}
