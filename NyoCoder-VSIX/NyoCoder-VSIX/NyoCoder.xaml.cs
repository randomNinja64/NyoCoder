using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

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
    /// UserControl that hosts the NyoCoder output pane content.
    /// This control is hosted inside a Visual Studio tool window.
    /// </summary>
    public partial class NyoCoderControl : UserControl
    {
        // Synchronization for tool approval
        private ManualResetEvent _approvalWaitHandle;
        private ApprovalResult _approvalResult;
        
        private volatile bool _stopRequested;
        private Button _stopButton;
        private volatile bool _isRunning;

        public NyoCoderControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Appends text to the output pane.
        /// </summary>
        public void AppendText(string text)
        {
            if (Dispatcher.CheckAccess())
            {
                AppendTextInternal(text);
            }
            else
            {
                Dispatcher.Invoke(new Action(() => AppendTextInternal(text)));
            }
        }

        private void AppendTextInternal(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Get the last paragraph, or create one if none exists
            Paragraph lastParagraph = null;
            if (OutputTextBox.Document.Blocks.Count > 0)
            {
                lastParagraph = OutputTextBox.Document.Blocks.LastBlock as Paragraph;
            }

            // If no paragraph exists or last block is not a paragraph, create a new one
            if (lastParagraph == null)
            {
                lastParagraph = new Paragraph { Margin = new Thickness(0), Padding = new Thickness(0) };
                OutputTextBox.Document.Blocks.Add(lastParagraph);
            }

            // Check if text contains newlines
            if (text.Contains("\n") || text.Contains("\r"))
            {
                // Split by newlines and handle each part
                string[] parts = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0)
                    {
                        // Create a new paragraph for each newline (except the first)
                        lastParagraph = new Paragraph();
                        OutputTextBox.Document.Blocks.Add(lastParagraph);
                    }
                    
                    if (!string.IsNullOrEmpty(parts[i]))
                    {
                        // Append text to the current paragraph
                        lastParagraph.Inlines.Add(new Run(parts[i]));
                    }
                }
            }
            else
            {
                // No newlines, just append directly to the last paragraph
                lastParagraph.Inlines.Add(new Run(text));
            }
            
            // Scroll to end
            OutputTextBox.ScrollToEnd();
        }

        /// <summary>
        /// Appends a line of text to the output pane.
        /// </summary>
        public void AppendLine(string text)
        {
            AppendText(text + Environment.NewLine);
        }

        /// <summary>
        /// Clears all text from the output pane.
        /// </summary>
        public void ClearOutput()
        {
            if (Dispatcher.CheckAccess())
            {
                OutputTextBox.Document.Blocks.Clear();
            }
            else
            {
                Dispatcher.Invoke(new Action(ClearOutput));
            }
        }

        /// <summary>
        /// Sets the output text, replacing any existing content.
        /// </summary>
        public void SetOutput(string text)
        {
            if (Dispatcher.CheckAccess())
            {
                OutputTextBox.Document.Blocks.Clear();
                var paragraph = new Paragraph(new Run(text)) { Margin = new Thickness(0), Padding = new Thickness(0) };
                OutputTextBox.Document.Blocks.Add(paragraph);
                OutputTextBox.ScrollToEnd();
            }
            else
            {
                Dispatcher.Invoke(new Action(() => SetOutput(text)));
            }
        }

        /// <summary>
        /// Adds an action button to the button panel.
        /// </summary>
        public Button AddButton(string text, RoutedEventHandler clickHandler)
        {
            if (Dispatcher.CheckAccess())
            {
                return AddButtonInternal(text, clickHandler);
            }
            else
            {
                return (Button)Dispatcher.Invoke(new Func<Button>(() => AddButtonInternal(text, clickHandler)));
            }
        }

        /// <summary>
        /// Adds an action button to the button panel (EventHandler overload for compatibility).
        /// </summary>
        public Button AddButton(string text, EventHandler clickHandler)
        {
            RoutedEventHandler routedHandler = null;
            if (clickHandler != null)
            {
                routedHandler = (sender, e) => clickHandler(sender, e);
            }
            return AddButton(text, routedHandler);
        }

        private Button AddButtonInternal(string text, RoutedEventHandler clickHandler)
        {
            var button = new Button
            {
                Content = text,
                Margin = new Thickness(2),
                Padding = new Thickness(8, 4, 8, 4),
                MinWidth = 75,
                MinHeight = 25
            };

            if (clickHandler != null)
            {
                button.Click += clickHandler;
            }

            ButtonPanel.Children.Add(button);
            ButtonPanel.Visibility = Visibility.Visible;

            return button;
        }

        /// <summary>
        /// Clears all buttons from the button panel.
        /// </summary>
        public void ClearButtons()
        {
            if (Dispatcher.CheckAccess())
            {
                ButtonPanel.Children.Clear();
            }
            else
            {
                Dispatcher.Invoke(new Action(ClearButtons));
            }
        }

        /// <summary>
        /// Shows or hides the button panel.
        /// </summary>
        public void SetButtonPanelVisible(bool visible)
        {
            if (Dispatcher.CheckAccess())
            {
                ButtonPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                Dispatcher.Invoke(new Action(() => SetButtonPanelVisible(visible)));
            }
        }

        /// <summary>
        /// Resets the stop flag for a new session.
        /// </summary>
        public void ResetStopRequested()
        {
            _stopRequested = false;
        }

        /// <summary>
        /// Returns true if a stop has been requested.
        /// </summary>
        public bool IsStopRequested()
        {
            return _stopRequested;
        }

        /// <summary>
        /// Requests user approval for a tool execution using Yes/No/Stop buttons.
        /// This method blocks until the user clicks Yes, No, or Stop.
        /// Thread-safe: can be called from background threads.
        /// </summary>
        /// <param name="toolName">Name of the tool requesting approval</param>
        /// <param name="arguments">Arguments to display to the user</param>
        /// <returns>ApprovalResult indicating the user's choice</returns>
        public ApprovalResult RequestToolApproval(string toolName, string arguments)
        {
            _approvalWaitHandle = new ManualResetEvent(false);
            _approvalResult = ApprovalResult.Rejected;

            // Show approval UI on the UI thread
            if (Dispatcher.CheckAccess())
            {
                ShowApprovalUI(toolName, arguments);
            }
            else
            {
                Dispatcher.Invoke(new Action(() => ShowApprovalUI(toolName, arguments)));
            }

            // Block until user responds
            _approvalWaitHandle.WaitOne();

            return _approvalResult;
        }

        private void ShowApprovalUI(string toolName, string arguments)
        {
            // Display the approval request in the output
            AppendText("\n[Approval Required] " + toolName);
            AppendText("\n" + arguments + "\n");

            // Clear any existing buttons and add Yes/No/Stop
            ClearButtons();

            var yesButton = new Button
            {
                Content = "Yes",
                Margin = new Thickness(2),
                Padding = new Thickness(8, 4, 8, 4),
                MinWidth = 75,
                MinHeight = 25
            };
            yesButton.Click += OnApprovalYes;

            var noButton = new Button
            {
                Content = "No",
                Margin = new Thickness(2),
                Padding = new Thickness(8, 4, 8, 4),
                MinWidth = 75,
                MinHeight = 25
            };
            noButton.Click += OnApprovalNo;

            var stopButton = new Button
            {
                Content = "Stop",
                Margin = new Thickness(2),
                Padding = new Thickness(8, 4, 8, 4),
                MinWidth = 75,
                MinHeight = 25
            };
            stopButton.Click += OnApprovalStop;

            ButtonPanel.Children.Add(yesButton);
            ButtonPanel.Children.Add(noButton);
            ButtonPanel.Children.Add(stopButton);
            ButtonPanel.Visibility = Visibility.Visible;
        }

        private void OnApprovalYes(object sender, RoutedEventArgs e)
        {
            HideApprovalUI();
            _approvalResult = ApprovalResult.Approved;
            if (_approvalWaitHandle != null)
            {
                _approvalWaitHandle.Set();
            }
        }

        private void OnApprovalNo(object sender, RoutedEventArgs e)
        {
            HideApprovalUI();
            _approvalResult = ApprovalResult.Rejected;
            if (_approvalWaitHandle != null)
            {
                _approvalWaitHandle.Set();
            }
        }

        private void OnApprovalStop(object sender, RoutedEventArgs e)
        {
            HideApprovalUI();
            _approvalResult = ApprovalResult.Stopped;
            _stopRequested = true;
            if (_approvalWaitHandle != null)
            {
                _approvalWaitHandle.Set();
            }
        }

        private void HideApprovalUI()
        {
            if (Dispatcher.CheckAccess())
            {
                ClearButtons();
                ButtonPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                Dispatcher.Invoke(new Action(HideApprovalUI));
            }
        }
    }
}