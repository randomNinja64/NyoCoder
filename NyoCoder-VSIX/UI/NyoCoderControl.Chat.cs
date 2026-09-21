using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NyoCoder
{
    public partial class NyoCoderControl
    {
        /// <summary>
        /// Appends text to the output pane. All chat output must use this method.
        /// </summary>
        public void AppendText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            EditorService.InvokeOnUIThread(() => AppendTextInternal(text), Dispatcher);
        }

        /// <summary>
        /// Ends the current list turn so the next write opens a fresh ChatTurn.
        /// </summary>
        private void StartOutputBlock()
        {
            EditorService.InvokeOnUIThread(StartOutputBlockInternal, Dispatcher);
        }

        private void StartOutputBlockInternal()
        {
            if (_currentTurn == null)
                return;

            _currentTurn.Complete();
            _currentTurn = null;
        }

        private void AppendTextInternal(string text)
        {
            _tokenTracker.OnTextAppended(text);

            if (_currentTurn == null)
                _currentTurn = AddTurn();

            _currentTurn.AppendText(text);
            ScrollChatToEnd();
        }

        /// <summary>
        /// Appends a line of text to the output pane.
        /// </summary>
        public void AppendLine(string text)
        {
            AppendText(text + Environment.NewLine);
        }

        /// <summary>
        /// Post-processes each turn's document to render Markdown formatting.
        /// Called after each assistant generation turn completes.
        /// </summary>
        public void ApplyMarkdown()
        {
            if (!ConfigHandler.GetMarkdownParsing())
                return;

            EditorService.InvokeOnUIThread(() =>
            {
                foreach (ChatTurn turn in _chatTurns)
                {
                    turn.ProcessMarkdown();
                }
            }, Dispatcher);
        }

        /// <summary>
        /// Clears all text from the output pane.
        /// </summary>
        public void ClearOutput()
        {
            EditorService.InvokeOnUIThread(() =>
            {
                StartOutputBlockInternal();
                _chatTurns.Clear();
                _currentTurn = null;
                _tokenTracker.Reset();

                // Reset step planner display
                if (StepPlanner.Instance != null)
                {
                    StepPlanner.Instance.Reset();
                }
                CollapseStepStatus();
            }, Dispatcher);
        }

        /// <summary>
        /// Updates the step progress indicator in the status bar.
        /// Must be called on the UI thread.
        /// </summary>
        private void RefreshStepDisplay()
        {
            StepPlanner planner = StepPlanner.Instance;
            if (planner == null || planner.Steps.Count == 0)
            {
                CollapseStepStatus();
                return;
            }

            StepStatusText.Text = planner.GetStepIndicator();
            StepStatusText.ToolTip = planner.GetDetailedTooltip();
            StepStatusText.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Hides the step progress indicator.
        /// </summary>
        private void HideStepDisplay()
        {
            EditorService.BeginInvokeOnUIThread(CollapseStepStatus, Dispatcher);
        }

        private void CollapseStepStatus()
        {
            StepStatusText.Visibility = Visibility.Collapsed;
            StepStatusText.ToolTip = null;
        }

        private ChatTurn AddTurn()
        {
            ChatTurn turn = new ChatTurn();
            if (ChatList.FontSize > 0)
                turn.ApplyFontSize(ChatList.FontSize);
            ApplyDocumentPageWidth(turn);
            _chatTurns.Add(turn);
            return turn;
        }

        private void ShowWelcomeTurn()
        {
            ChatTurn welcome = AddTurn();
            welcome.AppendText(WelcomeMessage);
            welcome.Complete();
            // Welcome is not an open streaming turn — next StartBlock/Write opens a fresh one.
            _currentTurn = null;
        }

        private void ChatList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            foreach (ChatTurn turn in _chatTurns)
                ApplyDocumentPageWidth(turn);
        }

        private void ApplyDocumentPageWidth(ChatTurn turn)
        {
            if (turn == null || ChatList == null)
                return;

            double width = ChatList.ActualWidth
                - SystemParameters.VerticalScrollBarWidth
                - 16;
            if (width > 50)
                turn.SetPageWidth(width);
        }

        private void ScrollChatToEnd()
        {
            if (_chatTurns.Count == 0)
                return;

            ChatList.ScrollIntoView(_chatTurns[_chatTurns.Count - 1]);

            if (_chatScrollViewer == null)
                _chatScrollViewer = FindScrollViewer(ChatList);
            if (_chatScrollViewer != null)
                _chatScrollViewer.ScrollToEnd();
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            ScrollViewer viewer = root as ScrollViewer;
            if (viewer != null)
                return viewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                ScrollViewer child = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (child != null)
                    return child;
            }
            return null;
        }

        /// <summary>
        /// Scrolls the output list to the bottom.
        /// </summary>
        public void ScrollToBottom()
        {
            EditorService.BeginInvokeOnUIThread(ScrollChatToEnd, Dispatcher, DispatcherPriority.Background);
        }

    }
}
