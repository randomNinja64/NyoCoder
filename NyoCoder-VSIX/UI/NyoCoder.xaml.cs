using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using EnvDTE80;

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
    /// </summary>
    public partial class NyoCoderControl : UserControl
    {
        // Synchronization for tool approval
        private ManualResetEvent _approvalWaitHandle;
        private ApprovalResult _approvalResult;
        
        private volatile bool _stopRequested;
        
        // Token tracking
        private int _totalCharacterCount;
        
        // Image attachment
        private string _attachedImageBase64;

        public NyoCoderControl()
        {
            InitializeComponent();
        }


        /// <summary>
        /// Gets the current approximate token count including overhead.
        /// </summary>
        public int GetApproximateTokenCount()
        {
            return ContextEngine.ApproximateTokens(_totalCharacterCount);
        }

        /// <summary>
        /// Resets the character count to a specific value (used after summarization).
        /// </summary>
        public void ResetCharacterCount(int newCount = 0)
        {
            EditorService.InvokeOnUIThread(() =>
            {
                _totalCharacterCount = newCount;
                RefreshTokenDisplay();
            }, Dispatcher);
        }

        /// <summary>
        /// Adds characters to the token counter without printing them.
        /// </summary>
        public void AddToCharacterCount(int delta)
        {
            if (delta == 0)
                return;

            EditorService.BeginInvokeOnUIThread(() =>
            {
                _totalCharacterCount = Math.Max(0, _totalCharacterCount + delta);
                RefreshTokenDisplay();
            }, Dispatcher);
        }

        /// <summary>
        /// Appends text to the output pane.
        /// </summary>
        public void AppendText(string text)
        {
            EditorService.InvokeOnUIThread(() => AppendTextInternal(text), Dispatcher);
        }

        private void AppendTextInternal(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Track character count for token estimation
            _totalCharacterCount += text.Length;
            RefreshTokenDisplay();

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
            EditorService.InvokeOnUIThread(() =>
            {
                OutputTextBox.Document.Blocks.Clear();
                _totalCharacterCount = 0;
                RefreshTokenDisplay();
            }, Dispatcher);
        }

        /// <summary>
        /// Refreshes the token display based on current character count.
        /// Must be called on the UI thread.
        /// </summary>
        private void RefreshTokenDisplay()
        {
            // Calculate approximate tokens including base overhead (system prompt + tools)
            int approximateTokens = ContextEngine.ApproximateTokens(_totalCharacterCount);
            int? contextWindowSize = ConfigHandler.ContextWindowSize;

            string statusText;
            if (contextWindowSize.HasValue && contextWindowSize.Value > 0)
            {
                double percentage = (double)approximateTokens / contextWindowSize.Value * 100;
                statusText = string.Format("Tokens: ~{0:N0} / {1:N0} ({2:F1}%)", 
                    approximateTokens, contextWindowSize.Value, percentage);
            }
            else
            {
                statusText = string.Format("Tokens: ~{0:N0}", approximateTokens);
            }

            TokenStatusText.Text = statusText;
        }

        /// <summary>
        /// Sets the output text, replacing any existing content.
        /// </summary>
        public void SetOutput(string text)
        {
            EditorService.InvokeOnUIThread(() =>
            {
                OutputTextBox.Document.Blocks.Clear();
                _totalCharacterCount = text != null ? text.Length : 0;
                RefreshTokenDisplay();
                var paragraph = new Paragraph(new Run(text)) { Margin = new Thickness(0), Padding = new Thickness(0) };
                OutputTextBox.Document.Blocks.Add(paragraph);
                OutputTextBox.ScrollToEnd();
            }, Dispatcher);
        }

        /// <summary>
        /// Creates a button with standard styling.
        /// </summary>
        private Button CreateStandardButton(string content, RoutedEventHandler clickHandler = null)
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

        /// <summary>
        /// Adds an action button to the button panel.
        /// </summary>
        public Button AddButton(string text, RoutedEventHandler clickHandler)
        {
            return EditorService.InvokeOnUIThread(() =>
            {
                var button = CreateStandardButton(text, clickHandler);
                ButtonPanel.Children.Add(button);
                ButtonPanel.Visibility = Visibility.Visible;
                return button;
            }, Dispatcher);
        }

        /// <summary>
        /// Adds an action button to the button panel.
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

        /// <summary>
        /// Clears all buttons from the button panel.
        /// </summary>
        public void ClearButtons()
        {
            EditorService.InvokeOnUIThread(() => ButtonPanel.Children.Clear(), Dispatcher);
        }

        /// <summary>
        /// Shows or hides the button panel.
        /// </summary>
        public void SetButtonPanelVisible(bool visible)
        {
            EditorService.InvokeOnUIThread(() => ButtonPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed, Dispatcher);
        }

        /// <summary>
        /// Resets the stop flag.
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
        /// Requests user approval for a tool execution.
        /// </summary>
        /// <param name="toolName">Name of the tool requesting approval</param>
        /// <param name="arguments">Arguments to display to the user</param>
        /// <returns>ApprovalResult indicating the user's choice</returns>
        public ApprovalResult RequestToolApproval(string toolName, string arguments)
        {
            _approvalWaitHandle = new ManualResetEvent(false);
            _approvalResult = ApprovalResult.Rejected;

            EditorService.InvokeOnUIThread(() => ShowApprovalUI(toolName, arguments), Dispatcher);

            // Block until user responds
            _approvalWaitHandle.WaitOne();

            return _approvalResult;
        }

        private void ShowApprovalUI(string toolName, string arguments)
        {
            AppendText("\n[Approval Required] " + toolName);
            AppendText("\n" + arguments + "\n");

            ClearButtons();

            var yesButton = CreateStandardButton("Approve", OnApprovalYes);
            var noButton = CreateStandardButton("Reject", OnApprovalNo);
            var stopButton = CreateStandardButton("Stop", OnApprovalStop);

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
            EditorService.InvokeOnUIThread(() =>
            {
                ClearButtons();
                ButtonPanel.Visibility = Visibility.Collapsed;
            }, Dispatcher);
        }


        /// <summary>
        /// Shows the input bar.
        /// </summary>
        public void ShowInputBar()
        {
            EditorService.InvokeOnUIThread(() =>
            {
                InputBar.Visibility = Visibility.Visible;
                InputBox.Focus();
            }, Dispatcher);
        }

        /// <summary>
        /// Hides the input bar.
        /// </summary>
        public void HideInputBar()
        {
            EditorService.InvokeOnUIThread(() =>
            {
                InputBar.Visibility = Visibility.Collapsed;
                InputBox.Clear();
            }, Dispatcher);
        }

        /// <summary>
        /// Handles the Send button click.
        /// </summary>
        private void InputSendButton_Click(object sender, RoutedEventArgs e)
        {
            SendInputMessage();
        }

        /// <summary>
        /// Handles the Attach Image toggle button checked event.
        /// </summary>
        private void AttachImageButton_Checked(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files (*.*)|*.*",
                Title = "Select an image to attach"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string imagePath = openFileDialog.FileName;
                    
                    // Read the image file and convert to base64
                    byte[] imageBytes = System.IO.File.ReadAllBytes(imagePath);
                    _attachedImageBase64 = Convert.ToBase64String(imageBytes);
                    
                    // Update tooltip to show image is attached
                    AttachImageButton.ToolTip = "Image attached: " + System.IO.Path.GetFileName(imagePath);
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(
                        "Error loading image: " + ex.Message,
                        "NyoCoder",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Error);
                    // Uncheck the button if there was an error
                    AttachImageButton.IsChecked = false;
                }
            }
            else
            {
                // User cancelled the dialog, uncheck the button
                AttachImageButton.IsChecked = false;
            }
        }

        /// <summary>
        /// Handles the Attach Image toggle button unchecked event.
        /// </summary>
        private void AttachImageButton_Unchecked(object sender, RoutedEventArgs e)
        {
            // Clear the attached image
            _attachedImageBase64 = null;
            AttachImageButton.ToolTip = null;
        }

        /// <summary>
        /// Handles the Enter key press in the input box.
        /// </summary>
        private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // Shift+Enter: Allow default behavior (new line)
                    return;
                }
                else
                {
                    // Enter: Send message
                    e.Handled = true;
                    SendInputMessage();
                }
            }
        }

        /// <summary>
        /// Sends an input message.
        /// </summary>
        private void SendInputMessage()
        {
            string message = InputBox.Text != null ? InputBox.Text.Trim() : null;
            if (string.IsNullOrWhiteSpace(message))
                return;

            // Get package instance and LLM client
            NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
            LLMClient llmClient = package != null ? package.LlmClient : null;
            
            // Determine if this is a new session (no client or empty conversation)
            bool isNewSession = llmClient == null || llmClient.Conversation == null || llmClient.Conversation.Count == 0;

            // Check if an AI request is already running
            if (System.Threading.Interlocked.CompareExchange(ref package._isAiRunning, 1, 0) != 0)
            {
                System.Windows.Forms.MessageBox.Show(
                    "An AI request is already in progress. Please wait for it to complete.",
                    "NyoCoder",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            // Get attached image before clearing
            string attachedImage = _attachedImageBase64;
            
            // Clear input and hide input bar
            InputBox.Clear();
            
            // Clear attached image and reset button
            _attachedImageBase64 = null;
            AttachImageButton.IsChecked = false;
            AttachImageButton.ToolTip = null;
            
            HideInputBar();

            // For new sessions, set up LLM client and clear output
            if (isNewSession)
            {
                // Validate configuration and create LLM client
                LLMClient newClient = LLMClient.CreateFromConfig();
                if (newClient == null)
                {
                    System.Threading.Interlocked.Exchange(ref package._isAiRunning, 0); // Reset flag
                    ShowInputBar(); // Show input bar again
                    return;
                }

                llmClient = newClient;
                package.LlmClient = llmClient;

                // Clear previous output
                ClearOutput();
            }

            // Display user message
            string userMessageDisplay = message;
            if (!string.IsNullOrEmpty(attachedImage))
            {
                userMessageDisplay += " [Image attached]";
            }
            
            // Add spacing between messages for follow-up conversations
            string prefix = isNewSession ? "" : "\n";
            AppendLine(prefix + "User: " + userMessageDisplay);
            AppendLine("\nAssistant: ");

            // Reset stop flag
            ResetStopRequested();

            // Save all open files
            try
            {
                package.SaveAllOpenFiles();
            }
            catch { }

            // Build the user prompt - include context for new sessions
            string userMessage = message;
            if (isNewSession)
            {
                // Build context for initial prompt
                DTE2 dte = EditorService.GetDte();
                ContextEngine contextEngine = new ContextEngine(dte);
                string context = contextEngine.BuildUserPromptContext();
                if (!string.IsNullOrWhiteSpace(context))
                {
                    userMessage = context + "\n\n---\n\n" + message;
                    
                    // Add the hidden characters so the status bar matches actual context usage
                    int hiddenDelta = userMessage.Length - message.Length;
                    if (hiddenDelta > 0)
                    {
                        AddToCharacterCount(hiddenDelta);
                    }
                }
            }

            // Send message on background thread
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    // ProcessConversation will use and update llmClient.Conversation automatically
                    llmClient.ProcessConversation(
                        userMessage,
                        attachedImage, // image (base64 encoded)
                        "Assistant",
                        null, // toolsRequiringApproval - will use defaults
                        false, // outputOnly
                        true, // showToolOutput
                        delegate(string text)
                        {
                            AppendText(text);
                        },
                        delegate(string toolName, string arguments)
                        {
                            return RequestToolApproval(toolName, arguments);
                        },
                        stopRequested: delegate() { return IsStopRequested(); },
                        onSummarized: delegate(int newCharCount)
                        {
                            ResetCharacterCount(newCharCount);
                        }
                    );
                    AppendText(Environment.NewLine);

                    // Show input bar again when done
                    ShowInputBar();
                }
                catch (Exception ex)
                {
                    AppendLine("\nError: " + ex.Message);
                    EditorService.InvokeOnUIThread(() =>
                    {
                        System.Windows.Forms.MessageBox.Show(
                            "Error communicating with LLM: " + ex.Message,
                            "NyoCoder",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Error);
                    }, Dispatcher);
                    ShowInputBar();
                }
                finally
                {
                    // Reset the AI running flag
                    System.Threading.Interlocked.Exchange(ref package._isAiRunning, 0);
                }
            });
        }
    }
}