using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace NyoCoder
{
    /// <summary>
    /// Owns the background conversation loop, plan execution, and plan review.
    /// All UI interaction is performed through callbacks supplied at construction.
    /// </summary>
    internal class MessageDispatcher
    {
        private readonly Action<string> _appendText;
        private readonly Action _startBlock;
        private readonly Action<string> _appendLine;
        private readonly Action _applyMarkdown;
        private readonly Func<bool> _stopRequested;
        private readonly Action<int> _resetCharacterCount;
        private readonly Action<int> _addToCharacterCount;
        private readonly Action _showInputBar;
        private readonly Action _hideStepDisplay;
        private readonly Action<string> _setMode;
        private readonly Action _onStepsChanged;
        private readonly Action _scrollToBottom;
        private readonly TokenTracker _tokenTracker;
        private readonly InteractionManager _interactionManager;
        private readonly ConversationSteerer _steerer = new ConversationSteerer();
        private readonly Dispatcher _dispatcher;
        private bool _stepsChangedSubscribed;

        internal MessageDispatcher(
            Action<string> appendText,
            Action startBlock,
            Action<string> appendLine,
            Action applyMarkdown,
            Func<bool> stopRequested,
            Action<int> resetCharacterCount,
            Action<int> addToCharacterCount,
            Action showInputBar,
            Action hideStepDisplay,
            Action<string> setMode,
            Action onStepsChanged,
            Action scrollToBottom,
            TokenTracker tokenTracker,
            InteractionManager interactionManager,
            Dispatcher dispatcher)
        {
            _appendText = appendText;
            _startBlock = startBlock;
            _appendLine = appendLine;
            _applyMarkdown = applyMarkdown;
            _stopRequested = stopRequested;
            _resetCharacterCount = resetCharacterCount;
            _addToCharacterCount = addToCharacterCount;
            _showInputBar = showInputBar;
            _hideStepDisplay = hideStepDisplay;
            _setMode = setMode;
            _onStepsChanged = onStepsChanged;
            _scrollToBottom = scrollToBottom;
            _tokenTracker = tokenTracker;
            _interactionManager = interactionManager;
            _dispatcher = dispatcher;
        }

        /// <summary>
        /// Queues a steering message for injection at the next safe point in the active conversation.
        /// </summary>
        internal void QueueSteer(string message)
        {
            _steerer.Queue(message);
            _startBlock();
            _appendLine("[steering queued] " + message);
        }

        internal void ClearSteerQueue()
        {
            _steerer.Clear();
        }

        /// <summary>
        /// Prompt text to send to the LLM, plus the raw user query for Auto-RAG search.
        /// </summary>
        internal sealed class BuiltUserMessage
        {
            public string Prompt;
            public string Query;

            public BuiltUserMessage(string prompt, string query)
            {
                Prompt = prompt ?? string.Empty;
                Query = query ?? string.Empty;
            }
        }

        /// <summary>
        /// Prepends editor context to the user's message for new sessions and accounts
        /// for the hidden extra characters in the token counter.
        /// </summary>
        internal BuiltUserMessage BuildUserMessage(string rawMessage, bool isNewSession)
        {
            string query = rawMessage ?? string.Empty;

            if (!isNewSession)
                return new BuiltUserMessage(query, query);

            EnvDTE80.DTE2 dte = EditorService.GetDte();
            ContextEngine contextEngine = new ContextEngine(dte);
            string context = contextEngine.BuildUserPromptContext();

            if (!string.IsNullOrWhiteSpace(context))
            {
                string prompt = context + "\n\n---\n\n" + query;
                int hiddenDelta = prompt.Length - query.Length;
                if (hiddenDelta > 0)
                    _addToCharacterCount(hiddenDelta);
                return new BuiltUserMessage(prompt, query);
            }

            return new BuiltUserMessage(query, query);
        }

        /// <summary>
        /// Runs Auto-RAG when enabled: may notify the user and/or inject a retrieved block into the prompt.
        /// </summary>
        private string ApplyAutoRag(string prompt, string query)
        {
            AutoRagContext.Result rag = AutoRagContext.TryRetrieve(query);
            if (rag == null || rag.Outcome == AutoRagContext.Status.Skipped
                || rag.Outcome == AutoRagContext.Status.NoHits)
                return prompt;

            if (!string.IsNullOrEmpty(rag.UserStatusLine))
            {
                _startBlock();
                _appendText(rag.UserStatusLine + "\n");
            }

            if (rag.Outcome == AutoRagContext.Status.Success
                && !string.IsNullOrWhiteSpace(rag.PromptBlock))
            {
                string merged = AutoRagContext.MergeIntoPrompt(prompt, rag.PromptBlock);
                int delta = merged.Length - (prompt != null ? prompt.Length : 0);
                if (delta > 0)
                    _addToCharacterCount(delta);
                return merged;
            }

            return prompt;
        }

        /// <summary>
        /// Queues the conversation on a background thread. Returns immediately.
        /// </summary>
        internal void RunConversation(
            BuiltUserMessage builtMessage,
            string attachedImage,
            LLMClient llmClient,
            string modeId,
            bool isNewSession,
            NyoCoder_VSIXPackage package)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                ToolApprovalService.Bind(_interactionManager.RequestToolApproval);
                try
                {
                    string userMessage = builtMessage != null ? builtMessage.Prompt : string.Empty;
                    string autoRagQuery = builtMessage != null ? builtMessage.Query : string.Empty;

                    if (isNewSession)
                    {
                        StepPlanner.Initialize();
                        if (!_stepsChangedSubscribed)
                        {
                            StepPlanner.Instance.StepsChanged += _onStepsChanged;
                            _stepsChangedSubscribed = true;
                        }

                        userMessage = ApplyAutoRag(userMessage, autoRagQuery);
                    }

                    // After any Auto-RAG status line, start the assistant block cleanly
                    _startBlock();
                    _appendText("Assistant: \n");

                    llmClient.ProcessConversation(
                        userMessage,
                        attachedImage,
                        _appendText,
                        ToolApprovalService.Request,
                        stopRequested: _stopRequested,
                        onSummarized: _resetCharacterCount,
                        modeId: modeId,
                        dequeueSteerMessage: _steerer.TryDequeue,
                        startBlock: _startBlock
                    );

                    ApplyMarkdownIfEnabled();

                    StepPlanner planner = StepPlanner.Instance;

                    if (string.Equals(modeId, ModeIds.Plan, StringComparison.OrdinalIgnoreCase))
                    {
                        HandlePlanReview(llmClient, modeId);
                    }
                    else if (planner != null && !planner.PlanRequiresExecution && !isNewSession)
                    {
                        _hideStepDisplay();
                    }

                    bool planExecuted = planner != null && planner.PlanRequiresExecution;
                    if (planExecuted)
                        ExecutePlan(planner, llmClient);

                    // planExecuted is captured before ExecutePlan runs, since Execute() resets
                    // PlanRequiresExecution to false; plan steps use separate clients, so it isn't
                    // reflected in llmClient.FilesModifiedThisTurn.
                    bool filesModified = llmClient.FilesModifiedThisTurn || planExecuted;
                    if (filesModified)
                    {
                        BuildErrorFixLoop.RunIfNeeded(
                            llmClient,
                            _stopRequested,
                            _appendText,
                            _steerer.TryDequeue,
                            _resetCharacterCount,
                            _startBlock);
                        ApplyMarkdownIfEnabled();
                    }

                    _appendText(Environment.NewLine);
                    _showInputBar();
                    _scrollToBottom();
                }
                catch (Exception ex)
                {
                    _startBlock();
                    _appendLine("Error: " + ex.Message);
                    EditorService.InvokeOnUIThread(() =>
                    {
                        MessageBox.Show(
                            "Error communicating with LLM: " + ex.Message,
                            "NyoCoder",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }, _dispatcher);
                    _showInputBar();
                    _scrollToBottom();
                }
                finally
                {
                    ToolApprovalService.Clear();
                    Interlocked.Exchange(ref package._isAiRunning, 0);
                }
            });
        }

        private void ExecutePlan(StepPlanner planner, LLMClient llmClient)
        {
            var executor = new StepExecutor(
                planner,
                llmClient,
                _appendText,
                _stopRequested,
                _steerer.TryDequeue,
                _startBlock);

            executor.ExecutionStarted += _tokenTracker.BeginStepTracking;
            executor.MainTokenCountChanged += _tokenTracker.SyncMainCount;
            executor.StepTokenCountChanged += _tokenTracker.SyncStepCount;
            executor.ExecutionFinished += _tokenTracker.EndStepTracking;

            executor.Execute();
            ApplyMarkdownIfEnabled();
        }

        private void ApplyMarkdownIfEnabled()
        {
            if (_applyMarkdown != null)
                _applyMarkdown();
        }

        private void HandlePlanReview(LLMClient llmClient, string planModeId)
        {
            while (true)
            {
                if (_stopRequested())
                    break;

                string refineText;
                PlanReviewResult reviewResult = _interactionManager.RequestPlanReview(out refineText);

                if (reviewResult == PlanReviewResult.Execute)
                {
                    _setMode(ModeIds.Agent);

                    // Prefer the plan file on disk (the source of truth); fall back to
                    // scraping the transcript if PLAN.md was never written.
                    string planText = PlanFile.ReadAll();
                    if (string.IsNullOrWhiteSpace(planText))
                        planText = ExtractApprovedPlan(llmClient.Conversation);

                    // Drop planning transcript; agent starts with a fresh context seeded by the plan.
                    llmClient.Conversation.Clear();
                    _resetCharacterCount(0);
                    if (StepPlanner.Instance != null)
                        StepPlanner.Instance.Reset();

                    string handoffBody = BuildPlanHandoffMessage(planText);
                    BuiltUserMessage built = BuildUserMessage(handoffBody, isNewSession: true);

                    _startBlock();
                    _appendLine("[Handing off to Agent for implementation...]");
                    _startBlock();
                    _appendLine("Assistant: ");

                    llmClient.ProcessConversation(
                        built.Prompt,
                        null,
                        _appendText,
                        ToolApprovalService.Request,
                        stopRequested: _stopRequested,
                        onSummarized: _resetCharacterCount,
                        modeId: ModeIds.Agent,
                        dequeueSteerMessage: _steerer.TryDequeue,
                        startBlock: _startBlock
                    );

                    ApplyMarkdownIfEnabled();

                    break;
                }
                else if (reviewResult == PlanReviewResult.Refine)
                {
                    _startBlock();
                    _appendLine("Assistant: ");

                    llmClient.ProcessConversation(
                        refineText,
                        null,
                        _appendText,
                        ToolApprovalService.Request,
                        stopRequested: _stopRequested,
                        onSummarized: _resetCharacterCount,
                        modeId: planModeId,
                        dequeueSteerMessage: _steerer.TryDequeue,
                        startBlock: _startBlock
                    );

                    ApplyMarkdownIfEnabled();

                    continue;
                }
                else // Cancel
                {
                    _startBlock();
                    _appendLine("[Plan cancelled]");
                    _hideStepDisplay();
                    break;
                }
            }
        }

        /// <summary>
        /// Builds the user message that seeds the Agent after plan approval.
        /// </summary>
        private static string BuildPlanHandoffMessage(string planText)
        {
            if (string.IsNullOrWhiteSpace(planText))
            {
                return "The approved plan could not be recovered from the planning conversation. "
                    + "Ask the user to restate the plan, then implement it. "
                    + "Use manage_plan to track your progress through the steps if the tool is available.";
            }

            return "The following plan has been approved. Implement it now. "
                + "Use manage_plan to track your progress through the steps if the tool is available.\n\n"
                + planText.Trim();
        }

        /// <summary>
        /// Pulls the approved plan out of the planning transcript: prefers the latest
        /// assistant message that contains a "## Plan:" heading, otherwise the latest
        /// non-empty assistant text.
        /// </summary>
        private static string ExtractApprovedPlan(List<LLMClient.ChatMessage> conversation)
        {
            if (conversation == null || conversation.Count == 0)
                return string.Empty;

            string lastAssistant = null;

            for (int i = conversation.Count - 1; i >= 0; i--)
            {
                LLMClient.ChatMessage msg = conversation[i];
                if (!string.Equals(msg.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrWhiteSpace(msg.Content))
                    continue;

                if (lastAssistant == null)
                    lastAssistant = msg.Content.Trim();

                if (msg.Content.IndexOf("## Plan:", StringComparison.OrdinalIgnoreCase) >= 0)
                    return msg.Content.Trim();
            }

            return lastAssistant ?? string.Empty;
        }
    }
}
