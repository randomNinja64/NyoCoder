using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;

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
        private readonly Action _startBlock;

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
            Action showInputBar = null,
            Action startBlock = null)
        {
            _buttonPanel = buttonPanel;
            _appendText = appendText;
            _scrollToBottom = scrollToBottom;
            _hideInputBar = hideInputBar;
            _showInputBar = showInputBar;
            _startBlock = startBlock ?? delegate { };
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
            _startBlock();
            _appendText("[Approval Required] " + toolName);
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
            _startBlock();
            _appendText("[Question] " + (question ?? "") + "\n");

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
                MinHeight = 23,
                Margin = new Thickness(2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _questionOtherBox.KeyDown += QuestionOtherBox_KeyDown;
            AddTextBoxRow(_questionOtherBox, "Type your own answer...", CreateStandardButton("Submit", OnQuestionSubmitOther));
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
            _startBlock();
            _appendText("[Review the plan above. Execute, refine, or cancel?]\n");

            _buttonPanel.Children.Clear();

            _buttonPanel.Children.Add(CreateStandardButton("Execute Plan", OnPlanExecute));

            _questionOtherBox = new TextBox
            {
                MinHeight = 23,
                Margin = new Thickness(2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _questionOtherBox.KeyDown += PlanRefineBox_KeyDown;
            AddTextBoxRow(_questionOtherBox, "Suggest plan changes...", CreateStandardButton("Refine", OnPlanRefine));
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

        private void AddTextBoxRow(TextBox textBox, string hint, Button button)
        {
            textBox.SetResourceReference(Control.BackgroundProperty, VsBrushes.WindowKey);
            textBox.SetResourceReference(Control.ForegroundProperty, VsBrushes.WindowTextKey);
            textBox.SetResourceReference(TextBox.CaretBrushProperty, VsBrushes.WindowTextKey);

            // Grey hint text overlaid on the textbox, hidden once the user types.
            var hintLabel = new TextBlock
            {
                Text = hint,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            hintLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            textBox.TextChanged += (s, e) =>
                hintLabel.Visibility = textBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            var textHost = new Grid();
            textHost.Children.Add(textBox);
            textHost.Children.Add(hintLabel);

            var row = new DockPanel();
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
            row.Children.Add(textHost);
            _buttonPanel.Children.Add(row);
        }

        private static Button CreateStandardButton(string content, RoutedEventHandler clickHandler = null)
        {
            var label = new TextBlock { Text = content, TextTrimming = TextTrimming.CharacterEllipsis };
            var button = new Button
            {
                Content = label,
                Margin = new Thickness(2),
                Padding = new Thickness(8, 2, 8, 2),
                MinHeight = 23,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                ToolTip = content
            };

            // Only show the tooltip when the label is cut off: measure an unconstrained copy.
            button.ToolTipOpening += (s, e) =>
            {
                var probe = new TextBlock { Text = content, FontFamily = label.FontFamily, FontSize = label.FontSize };
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (probe.DesiredSize.Width <= label.ActualWidth)
                    e.Handled = true;
            };

            if (clickHandler != null)
            {
                button.Click += clickHandler;
            }

            return button;
        }
    }
}
