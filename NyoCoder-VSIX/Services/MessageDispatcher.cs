using System;
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
        private readonly Action<ChatMode> _setMode;
        private readonly Action _onStepsChanged;
        private readonly Action _scrollToBottom;
        private readonly TokenTracker _tokenTracker;
        private readonly InteractionManager _interactionManager;
        private readonly ConversationSteerer _steerer = new ConversationSteerer();
        private readonly Dispatcher _dispatcher;

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
            Action<ChatMode> setMode,
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
        /// Prepends editor context to the user's message for new sessions and accounts
        /// for the hidden extra characters in the token counter.
        /// </summary>
        internal string BuildUserMessage(string rawMessage, bool isNewSession)
        {
            if (!isNewSession)
                return rawMessage;

            EnvDTE80.DTE2 dte = EditorService.GetDte();
            ContextEngine contextEngine = new ContextEngine(dte);
            string context = contextEngine.BuildUserPromptContext();

            if (!string.IsNullOrWhiteSpace(context))
            {
                string full = context + "\n\n---\n\n" + rawMessage;
                int hiddenDelta = full.Length - rawMessage.Length;
                if (hiddenDelta > 0)
                    _addToCharacterCount(hiddenDelta);
                return full;
            }

            return rawMessage;
        }

        /// <summary>
        /// Queues the conversation on a background thread. Returns immediately.
        /// </summary>
        internal void RunConversation(
            string userMessage,
            string attachedImage,
            LLMClient llmClient,
            ChatMode chatMode,
            bool isNewSession,
            NyoCoder_VSIXPackage package)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                ToolApprovalService.Bind(_interactionManager.RequestToolApproval);
                try
                {
                    if (isNewSession)
                    {
                        StepPlanner.Initialize();
                        StepPlanner.Instance.StepsChanged += _onStepsChanged;
                    }

                    llmClient.ProcessConversation(
                        userMessage,
                        attachedImage,
                        ConfigHandler.GetShowToolOutput(),
                        _appendText,
                        ToolApprovalService.Request,
                        stopRequested: _stopRequested,
                        onSummarized: _resetCharacterCount,
                        mode: chatMode,
                        dequeueSteerMessage: _steerer.TryDequeue,
                        startBlock: _startBlock
                    );

                    ApplyMarkdownIfEnabled();

                    StepPlanner planner = StepPlanner.Instance;

                    if (chatMode == ChatMode.Plan)
                    {
                        HandlePlanReview(llmClient, chatMode);
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

        private void HandlePlanReview(LLMClient llmClient, ChatMode planMode)
        {
            while (true)
            {
                if (_stopRequested())
                    break;

                string refineText;
                PlanReviewResult reviewResult = _interactionManager.RequestPlanReview(out refineText);

                if (reviewResult == PlanReviewResult.Execute)
                {
                    _setMode(ChatMode.Agent);

                    _startBlock();
                    _appendLine("[Handing off to Agent for implementation...]");
                    _startBlock();
                    _appendLine("Assistant: ");

                    llmClient.ProcessConversation(
                        "The plan above has been approved. Please implement it now. Use manage_plan to track your progress through the steps if the tool is available.",
                        null,
                        ConfigHandler.GetShowToolOutput(),
                        _appendText,
                        ToolApprovalService.Request,
                        stopRequested: _stopRequested,
                        onSummarized: _resetCharacterCount,
                        mode: ChatMode.Agent,
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
                        ConfigHandler.GetShowToolOutput(),
                        _appendText,
                        ToolApprovalService.Request,
                        stopRequested: _stopRequested,
                        onSummarized: _resetCharacterCount,
                        mode: planMode,
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
    }
}
