using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NyoCoder
{
    /// <summary>
    /// Result of a tool approval request.
    /// </summary>
    public enum ApprovalResult
    {
        Approved,
        Rejected,
        Stopped
    }

    /// <summary>
    /// Result of a plan review prompt.
    /// </summary>
    internal enum PlanReviewResult
    {
        Execute,
        Refine,
        Cancel
    }

    /// <summary>
    /// Manages blocking user-interaction prompts (tool approval and user questions).
    /// Only one interaction can be pending at a time. Methods block the calling
    /// (background) thread until the user responds on the UI thread.
    /// </summary>
    internal class InteractionManager
    {
        private readonly Panel _buttonPanel;
        private readonly Action<string> _appendText;
        private readonly Action _scrollToBottom;
        private readonly Action _hideInputBar;
        private readonly Action _showInputBar;

        // Shared synchronization — only one interaction pending at a time.
        private ManualResetEvent _pendingWaitHandle;
        private ApprovalResult _approvalResult;

        // User-question state
        private string _questionAnswer;
        private TextBox _questionOtherBox;

        // Plan-review state
        private PlanReviewResult _planReviewResult;
        private string _planRefineText;

        /// <summary>
        /// Raised when the user clicks the Stop button so the caller can set
        /// its own cancellation flag.
        /// </summary>
        public event Action StopRequested;

        public InteractionManager(
            Panel buttonPanel,
            Action<string> appendText,
            Action scrollToBottom = null,
            Action hideInputBar = null,
            Action showInputBar = null)
        {
            _buttonPanel = buttonPanel;
            _appendText = appendText;
            _scrollToBottom = scrollToBottom;
            _hideInputBar = hideInputBar;
            _showInputBar = showInputBar;
        }

        // ── Tool approval ──────────────────────────────────────────────

        /// <summary>
        /// Blocks the calling thread until the user approves, rejects, or stops.
        /// Must be called from a background thread.
        /// </summary>
        public ApprovalResult RequestToolApproval(string toolName, string arguments)
        {
            using (var waitHandle = new ManualResetEvent(false))
            {
                _pendingWaitHandle = waitHandle;
                _approvalResult = ApprovalResult.Rejected;

                EditorService.InvokeOnUIThread(() => ShowApprovalUI(toolName, arguments));

                waitHandle.WaitOne();
                _pendingWaitHandle = null;

                return _approvalResult;
            }
        }

        private void ShowApprovalUI(string toolName, string arguments)
        {
            _appendText("\n[Approval Required] " + toolName);
            _appendText("\n" + arguments + "\n");

            _buttonPanel.Children.Clear();

            _buttonPanel.Children.Add(CreateStandardButton("Approve", OnApprovalYes));
            _buttonPanel.Children.Add(CreateStandardButton("Reject", OnApprovalNo));
            _buttonPanel.Children.Add(CreateStandardButton("Stop", OnStopButton));

            _buttonPanel.Visibility = Visibility.Visible;
            if (_hideInputBar != null) _hideInputBar();
            if (_scrollToBottom != null) _scrollToBottom();
        }

        private void OnApprovalYes(object sender, RoutedEventArgs e) { SetApprovalResult(ApprovalResult.Approved); }
        private void OnApprovalNo(object sender, RoutedEventArgs e) { SetApprovalResult(ApprovalResult.Rejected); }

        private void SetApprovalResult(ApprovalResult result)
        {
            HideInteractionUI();
            _approvalResult = result;
            if (_pendingWaitHandle != null) _pendingWaitHandle.Set();
        }

        // ── User question ──────────────────────────────────────────────

        /// <summary>
        /// Blocks the calling thread until the user picks an option, types an
        /// answer, or stops. Must be called from a background thread.
        /// </summary>
        public string RequestUserQuestion(string question, string[] options)
        {
            using (var waitHandle = new ManualResetEvent(false))
            {
                _pendingWaitHandle = waitHandle;
                _questionAnswer = null;

                EditorService.InvokeOnUIThread(() => ShowQuestionUI(question, options));

                waitHandle.WaitOne();
                _pendingWaitHandle = null;
                return _questionAnswer ?? "";
            }
        }

        private void ShowQuestionUI(string question, string[] options)
        {
            _appendText("\n[Question] " + (question ?? "") + "\n");

            _buttonPanel.Children.Clear();

            if (options != null)
            {
                foreach (string option in options)
                {
                    string captured = option;
                    _buttonPanel.Children.Add(CreateStandardButton(captured, (s, e) => OnQuestionAnswered(captured)));
                }
            }

            _questionOtherBox = new TextBox
            {
                MinWidth = 160,
                MinHeight = 25,
                Margin = new Thickness(2),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Type your own answer..."
            };
            _questionOtherBox.KeyDown += QuestionOtherBox_KeyDown;
            _buttonPanel.Children.Add(_questionOtherBox);
            _buttonPanel.Children.Add(CreateStandardButton("Submit", OnQuestionSubmitOther));
            _buttonPanel.Children.Add(CreateStandardButton("Stop", OnStopButton));

            _buttonPanel.Visibility = Visibility.Visible;
            if (_hideInputBar != null) _hideInputBar();
            if (_scrollToBottom != null) _scrollToBottom();
        }

        private void QuestionOtherBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                OnQuestionSubmitOther(sender, null);
            }
        }

        private void OnQuestionSubmitOther(object sender, RoutedEventArgs e)
        {
            string text = _questionOtherBox != null ? _questionOtherBox.Text.Trim() : "";
            if (string.IsNullOrEmpty(text))
                return;
            OnQuestionAnswered(text);
        }

        private void OnQuestionAnswered(string answer)
        {
            _questionOtherBox = null;
            HideInteractionUI();

            _questionAnswer = answer;
            _appendText("[Answer] " + answer + "\n");

            if (_pendingWaitHandle != null) _pendingWaitHandle.Set();
        }

        // ── Plan review ────────────────────────────────────────────

        /// <summary>
        /// Blocks the calling thread until the user chooses to execute, refine, or cancel the plan.
        /// Returns the result and optional refinement text.
        /// Must be called from a background thread.
        /// </summary>
        public PlanReviewResult RequestPlanReview(out string refineText)
        {
            using (var waitHandle = new ManualResetEvent(false))
            {
                _pendingWaitHandle = waitHandle;
                _planReviewResult = PlanReviewResult.Cancel;
                _planRefineText = null;

                EditorService.InvokeOnUIThread(() => ShowPlanReviewUI());

                waitHandle.WaitOne();
                _pendingWaitHandle = null;

                refineText = _planRefineText;
                return _planReviewResult;
            }
        }

        private void ShowPlanReviewUI()
        {
            _appendText("\n[Review the plan above. Execute, refine, or cancel?]\n");

            _buttonPanel.Children.Clear();

            _buttonPanel.Children.Add(CreateStandardButton("Execute Plan", OnPlanExecute));

            _questionOtherBox = new TextBox
            {
                MinWidth = 160,
                MinHeight = 25,
                Margin = new Thickness(2),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Type refinement feedback..."
            };
            _questionOtherBox.KeyDown += PlanRefineBox_KeyDown;
            _buttonPanel.Children.Add(_questionOtherBox);
            _buttonPanel.Children.Add(CreateStandardButton("Refine", OnPlanRefine));
            _buttonPanel.Children.Add(CreateStandardButton("Cancel", OnPlanCancel));

            _buttonPanel.Visibility = Visibility.Visible;
            if (_hideInputBar != null) _hideInputBar();
            if (_scrollToBottom != null) _scrollToBottom();
        }

        private void PlanRefineBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                OnPlanRefine(sender, null);
            }
        }

        private void OnPlanExecute(object sender, RoutedEventArgs e)
        {
            _questionOtherBox = null;
            HideInteractionUI();
            _planReviewResult = PlanReviewResult.Execute;
            if (_pendingWaitHandle != null) _pendingWaitHandle.Set();
        }

        private void OnPlanRefine(object sender, RoutedEventArgs e)
        {
            string text = _questionOtherBox != null ? _questionOtherBox.Text.Trim() : "";
            if (string.IsNullOrEmpty(text))
                return;
            _questionOtherBox = null;
            HideInteractionUI();
            _planRefineText = text;
            _planReviewResult = PlanReviewResult.Refine;
            _appendText("[Refine] " + text + "\n");
            if (_pendingWaitHandle != null) _pendingWaitHandle.Set();
        }

        private void OnPlanCancel(object sender, RoutedEventArgs e)
        {
            _questionOtherBox = null;
            HideInteractionUI();
            _planReviewResult = PlanReviewResult.Cancel;
            if (_pendingWaitHandle != null) _pendingWaitHandle.Set();
        }

        // ── Stop / shared helpers ──────────────────────────────────────

        private void OnStopButton(object sender, RoutedEventArgs e)
        {
            _questionOtherBox = null;
            _approvalResult = ApprovalResult.Stopped;
            HideInteractionUI();

            var handler = StopRequested;
            if (handler != null) handler();

            if (_pendingWaitHandle != null) _pendingWaitHandle.Set();
        }

        private void HideInteractionUI()
        {
            _buttonPanel.Children.Clear();
            _buttonPanel.Visibility = Visibility.Collapsed;
            if (_showInputBar != null) _showInputBar();
        }

        private static Button CreateStandardButton(string content, RoutedEventHandler clickHandler = null)
        {
            var button = new Button
            {
                Content = content,
                Margin = new Thickness(2),
                Padding = new Thickness(8, 4, 8, 4),
                MinWidth = 75,
                MinHeight = 25
            };

            if (clickHandler != null)
            {
                button.Click += clickHandler;
            }

            return button;
        }
    }
}
