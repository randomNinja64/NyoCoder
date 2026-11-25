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
        this.llmEndpoint = (llmEndpoint ?? string.Empty).Trim().TrimEnd('/');
        this.apiKey = key;
        this.model = mdl;
        this.Conversation = new List<ChatMessage>();

        // Enable modern TLS protocols for HTTPS support
        TlsConfig.EnsureModernProtocols();
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
        if (TextNormalization.IsNullOrWhiteSpace(llmServer))
        {
            MessageBox.Show(
                "Please configure the LLM Server in Tools > NyoCoder Options...",
                "NyoCoder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }

        // Use empty string for optional values if not provided
        if (TextNormalization.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = "";
        }
        if (TextNormalization.IsNullOrWhiteSpace(model))
        {
            model = "";
        }

        // Create and return new LLM client
        return new LLMClient(llmServer, apiKey, model);
    }

    /// <summary>Between a user message's hidden context and the text the user typed.</summary>
    internal const string ContextSeparator = "\n\n---\n\n";

    // Struct for chat messages
    public struct ChatMessage
    {
        public string Role;
        public string Content;

        // Hidden context sent ahead of a user message (editor state, Auto-RAG snippets),
        // kept apart so a saved chat shows only what was typed. Null when there is none.
        public string Context;
        public string Image;
        public string ImageMime;
        public List<ToolHandler.ToolCall> ToolCalls;
        public string ToolCallId;

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
            Context = null;
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

    /// <summary>The text a message is sent with: its hidden context, if any, ahead of its content.</summary>
    internal static string SentContent(ChatMessage message)
    {
        if (string.IsNullOrEmpty(message.Context))
            return message.Content;
        return message.Context + ContextSeparator + message.Content;
    }

    public void ProcessConversation(
        string userMessage,
        string image,
        Action<string> outputCallback = null,
        Func<bool> stopRequested = null,
        Action<int> onSummarized = null,
        string modeId = ModeIds.Agent,
        Func<string> dequeueSteerMessage = null,
        Action startBlock = null,
        string context = null)
    {
        FilesModifiedThisTurn = false;

        ChatBlockDisplayMode toolCallDisplay = ConfigHandler.GetToolCallDisplayMode();
        ChatBlockDisplayMode toolOutputDisplay = ConfigHandler.GetToolOutputDisplayMode();
        int maxConsecutiveFailedToolCalls = ConfigHandler.GetMaxConsecutiveFailedToolCalls();
        int consecutiveFailedToolCalls = 0;

        // Add user message
        ChatMessage userMsg = new ChatMessage
        {
            Role = "user",
            Content = userMessage,
            Context = context,
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
            TrySummarizeConversation(outputCallback, onSummarized, startBlock, modeId);

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
                    // Keep any text the model sent alongside the calls so it stays in history.
                    Content = response.Content ?? string.Empty,
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
                    bool ran = false;
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

                    bool autoApproval = ConfigHandler.IsAutoToolApproval(call.Name);
                    try
                    {
                        if (autoApproval)
                        {
                            JevGateDecision decision = JevDecisionClient.Decide(
                                call.Name,
                                call.Arguments,
                                ToolDefinitions.GetToolDescription(call.Name),
                                RecentMessagesForDecision(this.Conversation));
                            JevGate.Begin(decision, ConfigHandler.GetShowJevConfidence());
                            if (JevGate.Allowed && outputCallback != null)
                            {
                                if (startBlock != null) startBlock();
                                outputCallback(JevGate.AutoAllowedLine(call.Name) + "\n");
                            }
                        }

                        // Pre-execution approval (file-edit tools approve after diff preview instead).
                        // Automatic tools ask Jev first; confirm falls through to this prompt.
                        if (ConfigHandler.RequiresApprovalBeforeExecute(call.Name))
                        {
                            string formattedArguments = call.Arguments
                                .Replace("\\n", "\n")
                                .Replace("\\r", "\r")
                                .Replace("\\t", "\t")
                                .Replace("\\\"", "\"")
                                .Replace("\\'", "'")
                                .Replace("\\\\", "\\");

                            approvalResult = ToolApprovalService.Request(call.Name, formattedArguments);

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
                                ran = ToolHandler.ExecuteToolCall(call, modeId, out toolContent, out exitCode, out toolImage, out toolImageMime);
                            }
                        }
                        else
                        {
                            // Execute the requested tool and capture its output
                            ran = ToolHandler.ExecuteToolCall(call, modeId, out toolContent, out exitCode, out toolImage, out toolImageMime);
                        }

                        ConsecutiveToolFailures.Record(
                            ref consecutiveFailedToolCalls, maxConsecutiveFailedToolCalls, ran, exitCode);

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
                    finally
                    {
                        if (autoApproval)
                            JevGate.Clear();
                    }

                }

                if (maxConsecutiveFailedToolCalls > 0
                    && consecutiveFailedToolCalls >= maxConsecutiveFailedToolCalls)
                {
                    if (outputCallback != null)
                    {
                        if (startBlock != null) startBlock();
                        outputCallback("[Stopped: " + consecutiveFailedToolCalls + " consecutive tool calls failed]\n");
                    }
                    break;
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
                TrySummarizeConversation(outputCallback, onSummarized, startBlock, modeId);

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
    /// Checks context usage and, if over the threshold, first clears old tool output
    /// (see ClearOldToolOutputs); if that is not enough, summarizes the conversation
    /// in place. Either way the UI is notified via <paramref name="onSummarized"/>.
    /// Safe to call before every LLM request.
    /// </summary>
    private void TrySummarizeConversation(Action<string> outputCallback, Action<int> onSummarized, Action startBlock, string modeId)
    {
        int startChars = GetConversationCharacterCount(this.Conversation);
        if (!ShouldSummarize(startChars, modeId))
            return;

        // Clearing alone is silent: the token counter shows it.
        if (ClearOldToolOutputs(this.Conversation, modeId))
        {
            if (onSummarized != null)
                onSummarized(GetConversationCharacterCount(this.Conversation));
            return;
        }

        if (outputCallback != null)
        {
            if (startBlock != null) startBlock();
            outputCallback("[Context usage high - summarizing conversation...]\n");
        }

        int keepFrom = RecentKeepStart(this.Conversation, KeepRecentChars());
        string summary = SummarizeConversation(this.Conversation.GetRange(0, keepFrom));
        if (string.IsNullOrEmpty(summary))
        {
            int dropped = DropOldestMessagesUntilFit(this.Conversation, modeId);

            // Last resort: this turn's own tool output, all but the newest result.
            if (ShouldSummarize(GetConversationCharacterCount(this.Conversation), modeId))
                ClearCurrentTurnToolOutputs(this.Conversation);

            if (outputCallback != null)
            {
                if (dropped > 0)
                {
                    if (startBlock != null) startBlock();
                    outputCallback("[Summarization failed - dropped older messages]\n");
                }
                if (ShouldSummarize(GetConversationCharacterCount(this.Conversation), modeId))
                {
                    if (startBlock != null) startBlock();
                    outputCallback("[Summarization failed - current turn still exceeds the context window]\n");
                }
            }
            int endChars = GetConversationCharacterCount(this.Conversation);
            if (endChars != startChars && onSummarized != null)
                onSummarized(endChars);
            return;
        }

        ReplaceWithSummary(this.Conversation, keepFrom, summary);

        if (outputCallback != null)
        {
            if (startBlock != null) startBlock();
            outputCallback("[Conversation summarized - continuing...]\n");
        }

        // Notify UI to reset character count
        if (onSummarized != null)
            onSummarized(GetConversationCharacterCount(this.Conversation));
    }

    /// <summary>
    /// Clearing old tool output is enough to skip summarizing once usage is at or
    /// under this share of the context window.
    /// </summary>
    private const double ClearToolOutputTarget = 0.60;

    /// <summary>
    /// Share of the context window kept verbatim after summarizing: the most recent
    /// messages up to this size stay, and only the older ones are summarized.
    /// </summary>
    private const double KeepRecentShare = 0.10;

    /// <summary>The kept-recent budget in characters; 0 with no window size set.</summary>
    private static int KeepRecentChars()
    {
        int? contextWindowSize = ConfigHandler.ContextWindowSize;
        if (!contextWindowSize.HasValue || contextWindowSize.Value <= 0)
            return 0;
        return (int)(contextWindowSize.Value * KeepRecentShare) * ContextEngine.CharsPerToken;
    }

    private const string SummaryHeader = "[Previous conversation summary]\n";

    /// <summary>
    /// Where the messages kept verbatim after summarizing start: the earliest index
    /// whose messages from there to the end fit in <paramref name="budgetChars"/>, at
    /// a boundary the summary joins cleanly. A user message takes the summary as its
    /// hidden context; an assistant message after a tool result or another assistant
    /// message (mid-turn) follows a summary message. An assistant message right after
    /// a user message is not a boundary: the summary request would then follow that
    /// user message, and some chat templates reject two user messages in a row.
    /// Index 0 is never chosen, so something is always summarized. Returns
    /// conversation.Count when nothing fits, which summarizes everything.
    /// </summary>
    private static int RecentKeepStart(List<ChatMessage> conversation, int budgetChars)
    {
        int keepFrom = conversation.Count;
        int tail = 0;
        for (int i = conversation.Count - 1; i > 0; i--)
        {
            tail += GetMessageCharacterCount(conversation[i]);
            if (tail > budgetChars)
                break;
            if (IsRole(conversation[i], "user")
                || (IsRole(conversation[i], "assistant") && !IsRole(conversation[i - 1], "user")))
                keepFrom = i;
        }
        return keepFrom;
    }

    /// <summary>
    /// Replaces the messages before <paramref name="keepFrom"/> with the summary. When
    /// the kept messages start with a user message, the summary goes ahead of that
    /// message's hidden context, so what the user typed stays as it was; otherwise a
    /// summary message comes first.
    /// </summary>
    private static void ReplaceWithSummary(List<ChatMessage> conversation, int keepFrom, string summary)
    {
        List<ChatMessage> recent = conversation.GetRange(keepFrom, conversation.Count - keepFrom);
        conversation.Clear();

        if (recent.Count > 0 && IsRole(recent[0], "user"))
        {
            ChatMessage first = recent[0];
            string block = SummaryHeader + summary + "\n\n[End of summary. The conversation continues from here.]";
            first.Context = string.IsNullOrEmpty(first.Context) ? block : block + "\n\n" + first.Context;
            recent[0] = first;
        }
        else
        {
            conversation.Add(new ChatMessage("user",
                SummaryHeader + summary + (recent.Count > 0
                    ? "\n\n[Continue from this context. The most recent messages follow.]"
                    : "\n\n[Continue from this context. The user's original request is being processed.]")));
        }
        conversation.AddRange(recent);
    }

    private const string ToolOutputHeaderEnd = "\nOutput:\n";
    private const string ClearedToolOutput = "[Output cleared to save context]";

    /// <summary>
    /// Clears tool results before the latest user message, oldest first, until usage
    /// is at or under ClearToolOutputTarget. The tool calls stay, so the model still
    /// sees what it ran; results already cleared are skipped. Returns true when usage
    /// ended under the target.
    /// </summary>
    private bool ClearOldToolOutputs(List<ChatMessage> conversation, string modeId)
    {
        int end = LatestUserIndex(conversation);
        int chars = GetConversationCharacterCount(conversation);

        for (int i = 0; i < end && !IsUnderClearTarget(chars, modeId); i++)
        {
            int saved;
            if (TryClearToolOutput(conversation, i, out saved))
                chars -= saved;
        }
        return IsUnderClearTarget(chars, modeId);
    }

    /// <summary>
    /// Clears this turn's tool results (after the latest user message), all but the
    /// newest, which the model is acting on.
    /// </summary>
    private static void ClearCurrentTurnToolOutputs(List<ChatMessage> conversation)
    {
        int newest = -1;
        for (int i = conversation.Count - 1; i >= 0 && newest < 0; i--)
        {
            if (IsRole(conversation[i], "tool"))
                newest = i;
        }

        int saved;
        for (int i = LatestUserIndex(conversation) + 1; i < newest; i++)
            TryClearToolOutput(conversation, i, out saved);
    }

    /// <summary>
    /// Replaces a tool result's output (and any image) with a short note, keeping the
    /// "Exit Code:" header. False when it is not a tool result or would not get
    /// smaller. ChatMessage is a struct, so the change is stored back.
    /// </summary>
    private static bool TryClearToolOutput(List<ChatMessage> conversation, int index, out int savedChars)
    {
        savedChars = 0;
        ChatMessage message = conversation[index];
        if (!IsRole(message, "tool"))
            return false;

        string content = message.Content ?? string.Empty;
        int header = content.IndexOf(ToolOutputHeaderEnd, StringComparison.Ordinal);
        string replacement = header >= 0
            ? content.Substring(0, header + ToolOutputHeaderEnd.Length) + ClearedToolOutput
            : ClearedToolOutput;

        int before = GetMessageCharacterCount(message);
        if (replacement.Length >= before)
            return false;

        message.Content = replacement;
        message.Image = null;
        message.ImageMime = null;
        conversation[index] = message;
        savedChars = before - GetMessageCharacterCount(message);
        return true;
    }

    /// <summary>True when usage is at or under the tool-clearing target (false with no window size).</summary>
    private static bool IsUnderClearTarget(int characterCount, string modeId)
    {
        int? contextWindowSize = ConfigHandler.ContextWindowSize;
        if (!contextWindowSize.HasValue || contextWindowSize.Value <= 0)
            return false;

        return ContextEngine.ApproximateTokens(characterCount, modeId) <= contextWindowSize.Value * ClearToolOutputTarget;
    }

    private static bool IsRole(ChatMessage message, string role)
    {
        return string.Equals(message.Role, role, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Index of the latest user message, or 0 when there is none.</summary>
    private static int LatestUserIndex(List<ChatMessage> conversation)
    {
        for (int i = conversation.Count - 1; i >= 0; i--)
        {
            if (IsRole(conversation[i], "user"))
                return i;
        }
        return 0;
    }

    /// <summary>
    /// Drops messages older than the latest user turn until usage is under the
    /// summarize threshold. The latest user message and everything after it stay.
    /// </summary>
    private int DropOldestMessagesUntilFit(List<ChatMessage> conversation, string modeId)
    {
        int keepFrom = LatestUserIndex(conversation);

        // Keep dropping past any tool results at the front: a tool result whose
        // assistant tool call was dropped is rejected by strict servers.
        int dropped = 0;
        while (keepFrom > 0
            && (ShouldSummarize(GetConversationCharacterCount(conversation), modeId)
                || string.Equals(conversation[0].Role, "tool", StringComparison.OrdinalIgnoreCase)))
        {
            conversation.RemoveAt(0);
            keepFrom--;
            dropped++;
        }
        return dropped;
    }

    /// <summary>
    /// Checks if summarization is needed based on total character count and context window size.
    /// Returns true if usage exceeds 90% and context window is configured.
    /// </summary>
    /// <param name="characterCount">Conversation character count; the mode's system prompt and tools are added.</param>
    /// <param name="modeId">The chat mode whose system prompt and tools are sent with the request.</param>
    /// <returns>True if summarization is needed, false otherwise.</returns>
    private static bool ShouldSummarize(int characterCount, string modeId)
    {
        int? contextWindowSize = ConfigHandler.ContextWindowSize;
        if (!contextWindowSize.HasValue || contextWindowSize.Value <= 0)
            return false;

        int approximateTokens = ContextEngine.ApproximateTokens(characterCount, modeId);
        double usage = (double)approximateTokens / contextWindowSize.Value;
        
        return usage >= 0.90; // 90% threshold
    }

    /// <summary>
    /// Sum of message content, a flat estimate per image, and tool-call name/arguments.
    /// </summary>
    internal int GetConversationCharacterCount(List<ChatMessage> conversation)
    {
        int count = 0;
        foreach (var msg in conversation)
            count += GetMessageCharacterCount(msg);
        return count;
    }

    private static int GetMessageCharacterCount(ChatMessage msg)
    {
        int count = 0;
        string content = SentContent(msg);
        if (!string.IsNullOrEmpty(content))
            count += content.Length;

        if (!string.IsNullOrEmpty(msg.Image))
            count += ContextEngine.ImageCharEstimate;

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

    /// <summary>
    /// Asks the model for a short chat title from the first exchange. Null when the
    /// request fails. Silent and tool-free, like the summary.
    /// </summary>
    public string GenerateTitle(string prompt, string reply)
    {
        const int excerpt = 1000;
        Func<string, string> cut = text =>
            text == null ? string.Empty : text.Length <= excerpt ? text : text.Substring(0, excerpt);

        List<ChatMessage> request = new List<ChatMessage>
        {
            new ChatMessage("user",
                "Write a short title (3 to 6 words) for a chat that begins with the exchange below. " +
                "Reply with the title only, no quotes or punctuation at the end.\n\n" +
                "User: " + cut(prompt) + "\n\nAssistant: " + cut(reply))
        };

        LLMCompletionResponse response = sendMessages(
            request,
            outputCallback: null,
            toolCallCallback: null,
            stopRequested: null,
            modeId: ModeIds.Agent,
            startBlock: null,
            includeTools: false,
            includeContextInjections: false);
        if (response.FinishReason == "request_failed")
            return null;
        return response.Content;
    }

    private static List<JevContextMessage> RecentMessagesForDecision(List<ChatMessage> conversation)
    {
        var selected = new List<JevContextMessage>();
        if (conversation == null || conversation.Count == 0)
            return selected;

        int start = conversation.Count - JevDecisionClient.RecentMessageCount;
        if (start < 0)
            start = 0;
        for (int i = start; i < conversation.Count; i++)
            selected.Add(ToJevContextMessage(conversation[i]));
        return selected;
    }

    private static JevContextMessage ToJevContextMessage(ChatMessage message)
    {
        string text = message.Content ?? string.Empty;
        if (message.ToolCalls != null)
        {
            for (int i = 0; i < message.ToolCalls.Count; i++)
            {
                if (text.Length > 0)
                    text += "\n";
                text += (message.ToolCalls[i].Name ?? string.Empty)
                    + " "
                    + (message.ToolCalls[i].Arguments ?? string.Empty);
            }
        }

        return new JevContextMessage
        {
            Role = message.Role ?? string.Empty,
            Text = text
        };
    }
}
}
