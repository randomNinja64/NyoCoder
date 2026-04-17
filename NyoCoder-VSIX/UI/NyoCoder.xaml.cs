using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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
        // Shared synchronization for approval and question prompts.
        // Only one interaction can be pending at a time.
        private ManualResetEvent _pendingWaitHandle;
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

            // Split by newlines and handle each part
            string[] parts = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);

            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    lastParagraph = new Paragraph();
                    OutputTextBox.Document.Blocks.Add(lastParagraph);
                }

                if (!string.IsNullOrEmpty(parts[i]))
                {
                    lastParagraph.Inlines.Add(new Run(parts[i]));
                }
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
            using (var waitHandle = new ManualResetEvent(false))
            {
                _pendingWaitHandle = waitHandle;
                _approvalResult = ApprovalResult.Rejected;

                EditorService.InvokeOnUIThread(() => ShowApprovalUI(toolName, arguments), Dispatcher);

                // Block until user responds
                waitHandle.WaitOne();
                _pendingWaitHandle = null;

                return _approvalResult;
            }
        }

        private void ShowApprovalUI(string toolName, string arguments)
        {
            AppendText("\n[Approval Required] " + toolName);
            AppendText("\n" + arguments + "\n");

            ButtonPanel.Children.Clear();

            ButtonPanel.Children.Add(CreateStandardButton("Approve", OnApprovalYes));
            ButtonPanel.Children.Add(CreateStandardButton("Reject", OnApprovalNo));
            ButtonPanel.Children.Add(CreateStandardButton("Stop", OnStopButton));

            ButtonPanel.Visibility = Visibility.Visible;
        }

        private void OnApprovalYes(object sender, RoutedEventArgs e) { SetApprovalResult(ApprovalResult.Approved); }
        private void OnApprovalNo(object sender, RoutedEventArgs e) { SetApprovalResult(ApprovalResult.Rejected); }

        private void SetApprovalResult(ApprovalResult result)
        {
            HideInteractionUI();
            _approvalResult = result;
            if (_pendingWaitHandle != null) _pendingWaitHandle.Set();
        }

        private void OnStopButton(object sender, RoutedEventArgs e)
        {
            _questionOtherBox = null;
            _approvalResult = ApprovalResult.Stopped;
            HideInteractionUI();
            _stopRequested = true;
            if (_pendingWaitHandle != null) _pendingWaitHandle.Set();
        }

        private void HideInteractionUI()
        {
            ButtonPanel.Children.Clear();
            ButtonPanel.Visibility = Visibility.Collapsed;
        }

        // Synchronization for user questions (ask_user_question tool).
        private string _questionAnswer;
        private TextBox _questionOtherBox;

        /// <summary>
        /// Prompts the user with a question and a list of preset options plus a
        /// free-form "Other" text field. Blocks the calling (background) thread
        /// until the user responds. Returns the chosen option text, the typed
        /// free-form answer, or an empty string if cancelled.
        /// </summary>
        public string RequestUserQuestion(string question, string[] options)
        {
            using (var waitHandle = new ManualResetEvent(false))
            {
                _pendingWaitHandle = waitHandle;
                _questionAnswer = null;

                EditorService.InvokeOnUIThread(() => ShowQuestionUI(question, options), Dispatcher);

                waitHandle.WaitOne();
                _pendingWaitHandle = null;
                return _questionAnswer ?? "";
            }
        }

        private void ShowQuestionUI(string question, string[] options)
        {
            AppendText("\n[Question] " + (question ?? "") + "\n");

            ButtonPanel.Children.Clear();

            if (options != null)
            {
                foreach (string option in options)
                {
                    string captured = option;
                    ButtonPanel.Children.Add(CreateStandardButton(captured, (s, e) => OnQuestionAnswered(captured)));
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
            ButtonPanel.Children.Add(_questionOtherBox);
            ButtonPanel.Children.Add(CreateStandardButton("Submit", OnQuestionSubmitOther));
            ButtonPanel.Children.Add(CreateStandardButton("Stop", OnStopButton));

            ButtonPanel.Visibility = Visibility.Visible;
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
            AppendText("[Answer] " + answer + "\n");

            if (_pendingWaitHandle != null) _pendingWaitHandle.Set();
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
                    byte[] imageBytes = File.ReadAllBytes(imagePath);
                    _attachedImageBase64 = Convert.ToBase64String(imageBytes);
                    
                    // Update tooltip to show image is attached
                    AttachImageButton.ToolTip = "Image attached: " + Path.GetFileName(imagePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Error loading image: " + ex.Message,
                        "NyoCoder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
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
            string message = InputBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return;

            // Get package instance and LLM client
            NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
            LLMClient llmClient = package != null ? package.LlmClient : null;
            
            // Determine if this is a new session (no client or empty conversation)
            bool isNewSession = llmClient == null || llmClient.Conversation == null || llmClient.Conversation.Count == 0;

            // Check if an AI request is already running
            if (Interlocked.CompareExchange(ref package._isAiRunning, 1, 0) != 0)
            {
                MessageBox.Show(
                    "An AI request is already in progress. Please wait for it to complete.",
                    "NyoCoder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Get attached image before clearing
            string attachedImage = _attachedImageBase64;
            
            // Clear attached image and reset button (setting IsChecked=false fires AttachImageButton_Unchecked)
            AttachImageButton.IsChecked = false;
            
            HideInputBar();

            // For new sessions, set up LLM client and clear output
            if (isNewSession)
            {
                // Validate configuration and create LLM client
                LLMClient newClient = LLMClient.CreateFromConfig();
                if (newClient == null)
                {
                    Interlocked.Exchange(ref package._isAiRunning, 0); // Reset flag
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
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    // ProcessConversation will use and update llmClient.Conversation automatically
                    llmClient.ProcessConversation(
                        userMessage,
                        attachedImage, // image (base64 encoded)
                        "Assistant",
                        null, // toolsRequiringApproval - will use defaults
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

                    // Show input bar again when done (but not if user stopped)
                    if (!IsStopRequested())
                        ShowInputBar();
                }
                catch (Exception ex)
                {
                    AppendLine("\nError: " + ex.Message);
                    EditorService.InvokeOnUIThread(() =>
                    {
                        MessageBox.Show(
                            "Error communicating with LLM: " + ex.Message,
                            "NyoCoder",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }, Dispatcher);
                    ShowInputBar();
                }
                finally
                {
                    // Reset the AI running flag
                    Interlocked.Exchange(ref package._isAiRunning, 0);
                }
            });
        }
    }
}