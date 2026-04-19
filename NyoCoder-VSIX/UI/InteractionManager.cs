using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NyoCoder
{
    /// <summary>
    /// Manages blocking user-interaction prompts (tool approval and user questions).
    /// Only one interaction can be pending at a time. Methods block the calling
    /// (background) thread until the user responds on the UI thread.
    /// </summary>
    internal class InteractionManager
    {
        private readonly Panel _buttonPanel;
        private readonly Action<string> _appendText;

        // Shared synchronization — only one interaction pending at a time.
        private ManualResetEvent _pendingWaitHandle;
        private ApprovalResult _approvalResult;

        // User-question state
        private string _questionAnswer;
        private TextBox _questionOtherBox;

        /// <summary>
        /// Raised when the user clicks the Stop button so the caller can set
        /// its own cancellation flag.
        /// </summary>
        public event Action StopRequested;

        public InteractionManager(Panel buttonPanel, Action<string> appendText)
        {
            _buttonPanel = buttonPanel;
            _appendText = appendText;
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
