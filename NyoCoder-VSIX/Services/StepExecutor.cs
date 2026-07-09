using System;
using System.Collections.Generic;
using System.Text;
using EnvDTE80;

namespace NyoCoder
{
    /// <summary>
    /// Orchestrates step-by-step plan execution using separate LLM contexts per step.
    /// </summary>
    internal class StepExecutor
    {
        private readonly StepPlanner _planner;
        private readonly LLMClient _mainClient;
        private readonly Action<string> _appendText;
        private readonly Func<bool> _stopRequested;
        private readonly Func<string> _dequeueSteerMessage;
        private readonly Action _startBlock;

        /// <summary>
        /// Fired when the main conversation token count should be synced.
        /// Parameter is the new character count.
        /// </summary>
        public event Action<int> MainTokenCountChanged;

        /// <summary>
        /// Fired when the step-level token count changes.
        /// Parameter is the new character count.
        /// </summary>
        public event Action<int> StepTokenCountChanged;

        /// <summary>
        /// Fired at the start of execution so the UI can show the step token display.
        /// Parameter is the initial pre-plan character count.
        /// </summary>
        public event Action<int> ExecutionStarted;

        /// <summary>
        /// Fired when execution finishes (whether normally or via stop) so the UI can hide step displays.
        /// Parameter is the final main conversation character count.
        /// </summary>
        public event Action<int> ExecutionFinished;

        public StepExecutor(
            StepPlanner planner,
            LLMClient mainClient,
            Action<string> appendText,
            Func<bool> stopRequested,
            Func<string> dequeueSteerMessage = null,
            Action startBlock = null)
        {
            _planner = planner;
            _mainClient = mainClient;
            _appendText = appendText;
            _stopRequested = stopRequested;
            _dequeueSteerMessage = dequeueSteerMessage;
            _startBlock = startBlock ?? delegate { };
        }

        /// <summary>
        /// Executes all pending steps in the plan. Blocks the calling thread until complete.
        /// </summary>
        public void Execute()
        {
            _planner.PlanRequiresExecution = false;
            _planner.IsExecutingSteps = true;

            // Snapshot pre-plan conversation (user message + assistant plan call + tool result)
            List<LLMClient.ChatMessage> prePlanConversation = new List<LLMClient.ChatMessage>(_mainClient.Conversation);
            int prePlanCharCount = _mainClient.GetConversationCharacterCount(prePlanConversation);

            var handler = ExecutionStarted;
            if (handler != null) handler(prePlanCharCount);

            // Track step-level characters starting from the pre-plan context
            int stepCharacterCount = prePlanCharCount;

            try
            {
                for (int stepIdx = 0; stepIdx < _planner.Steps.Count; stepIdx++)
                {
                    if (_stopRequested())
                    {
                        // Mark remaining steps as skipped
                        for (int j = stepIdx; j < _planner.Steps.Count; j++)
                        {
                            if (_planner.Steps[j].Status != StepStatus.Completed)
                                _planner.SetStepStatus(j, StepStatus.Skipped);
                        }
                        break;
                    }

                    PlanStep step = _planner.Steps[stepIdx];
                    if (step.Status == StepStatus.Completed || step.Status == StepStatus.Skipped)
                        continue;

                    _planner.SetStepStatus(stepIdx, StepStatus.InProgress);

                    try
                    {
                        // Fresh LLM client for this step
                        LLMClient stepClient = LLMClient.CreateFromConfig();
                        if (stepClient == null)
                        {
                            _planner.SetStepStatus(stepIdx, StepStatus.Failed);
                            _startBlock();
                            _appendText("[Step failed: could not create LLM client]\n");
                            continue;
                        }

                        // Seed with pre-plan conversation
                        stepClient.Conversation = new List<LLMClient.ChatMessage>(prePlanConversation);

                        // Reset step token tracking to pre-plan context size BEFORE printing step header
                        stepCharacterCount = prePlanCharCount;
                        RaiseStepTokenCountChanged(stepCharacterCount);

                        _startBlock();
                        _appendText("\u2501\u2501\u2501 Step " + (stepIdx + 1) + "/" + _planner.Steps.Count + ": " + step.Title + " \u2501\u2501\u2501\n");

                        // Build fresh editor context
                        string freshContext = string.Empty;
                        DTE2 dte = EditorService.GetDte();
                        if (dte != null)
                        {
                            ContextEngine contextEngine = new ContextEngine(dte);
                            freshContext = contextEngine.BuildUserPromptContext();
                        }

                        // Build step prompt with plan state + step identity
                        StringBuilder stepPrompt = new StringBuilder();
                        if (!string.IsNullOrWhiteSpace(freshContext))
                        {
                            stepPrompt.Append(freshContext);
                            stepPrompt.Append("\n\n");
                        }
                        stepPrompt.Append(_planner.ReadPlan());
                        stepPrompt.Append("\n\nYou are now working on Step " + (stepIdx + 1) + ": \"" + step.Title + "\"\n");
                        stepPrompt.Append("Focus on completing this step only.");

                        // Add step prompt chars to step tracking
                        stepCharacterCount += stepPrompt.Length;
                        RaiseStepTokenCountChanged(stepCharacterCount);

                        // Capture stepCharacterCount by value for the closure
                        int localStepCharCount = stepCharacterCount;

                        // Execute step with its own context (auto-summarize enabled)
                        stepClient.ProcessConversation(
                            stepPrompt.ToString(),
                            null, // no image for steps
                            ConfigHandler.GetShowToolOutput(),
                            delegate(string text)
                            {
                                _appendText(text);
                            },
                            ToolApprovalService.Request,
                            stopRequested: _stopRequested,
                            onSummarized: delegate(int newCharCount)
                            {
                                localStepCharCount = newCharCount;
                                RaiseStepTokenCountChanged(newCharCount);
                            },
                            dequeueSteerMessage: _dequeueSteerMessage,
                            startBlock: _startBlock
                        );

                        stepCharacterCount = localStepCharCount;

                        // Auto-mark completed if the LLM didn't already update it
                        if (step.Status == StepStatus.InProgress)
                        {
                            _planner.SetStepStatus(stepIdx, StepStatus.Completed);
                        }

                        // Extract the step's final assistant response and carry it into subsequent steps
                        string stepResult = null;
                        for (int i = stepClient.Conversation.Count - 1; i >= 0; i--)
                        {
                            LLMClient.ChatMessage msg = stepClient.Conversation[i];
                            if (msg.Role == "assistant" && !string.IsNullOrWhiteSpace(msg.Content))
                            {
                                stepResult = msg.Content;
                                break;
                            }
                        }

                        // Build a summary of what the step accomplished
                        string stepLabel = "[Step " + (stepIdx + 1) + " completed: " + step.Title + "]";
                        string stepSummary = stepLabel;

                        if (stepResult != null)
                        {
                            stepSummary = stepLabel + "\n" + stepResult;
                        }

                        // Inject into prePlanConversation so subsequent steps see prior results
                        prePlanConversation.Add(new LLMClient.ChatMessage("user", stepLabel));
                        prePlanConversation.Add(new LLMClient.ChatMessage("assistant", stepSummary));
                        prePlanCharCount += stepLabel.Length + stepSummary.Length;

                        // Also record in the main session conversation
                        _mainClient.Conversation.Add(new LLMClient.ChatMessage("user", stepLabel));
                        _mainClient.Conversation.Add(new LLMClient.ChatMessage("assistant", stepSummary));

                        // Sync main token counter to actual main conversation content
                        int mainCharCount = _mainClient.GetConversationCharacterCount(_mainClient.Conversation);
                        RaiseMainTokenCountChanged(mainCharCount);
                    }
                    catch (Exception stepEx)
                    {
                        _planner.SetStepStatus(stepIdx, StepStatus.Failed);
                        _startBlock();
                        _appendText("[Step failed: " + stepEx.Message + "]\n");
                    }
                }

                _startBlock();
                _appendText("\u2501\u2501\u2501 All steps completed \u2501\u2501\u2501\n");
            }
            finally
            {
                _planner.IsExecutingSteps = false;

                // Sync main counter from actual conversation now that steps are done
                int finalMainCharCount = _mainClient.GetConversationCharacterCount(_mainClient.Conversation);

                var finished = ExecutionFinished;
                if (finished != null) finished(finalMainCharCount);
            }
        }

        private void RaiseMainTokenCountChanged(int count)
        {
            var handler = MainTokenCountChanged;
            if (handler != null) handler(count);
        }

        private void RaiseStepTokenCountChanged(int count)
        {
            var handler = StepTokenCountChanged;
            if (handler != null) handler(count);
        }
    }
}
