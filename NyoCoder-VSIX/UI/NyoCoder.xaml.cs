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
        private volatile bool _stopRequested;

        // Image attachment
        private string _attachedImageBase64;

        // Step planner for complex task decomposition
        private StepPlanner _stepPlanner;

        // Manages blocking approval / question prompts
        private InteractionManager _interactionManager;

        // Owns main + step character counts and status-bar labels
        private TokenTracker _tokenTracker;

        public NyoCoderControl()
        {
            InitializeComponent();
            _interactionManager = new InteractionManager(ButtonPanel, AppendText, ScrollToBottom);
            _interactionManager.StopRequested += () => { _stopRequested = true; };
            _tokenTracker = new TokenTracker(TokenStatusText, StepTokenStatusText, SubagentStatusRow, Dispatcher);

            // Populate mode selector from the ChatMode enum so adding a new value
            // only requires changing the enum.
            ModeSelector.ItemsSource = Enum.GetValues(typeof(ChatMode));
            ModeSelector.SelectedIndex = 0;
            ModeSelector.SelectionChanged += (s, e) =>
            {
                if (ModeSelector.SelectedItem is ChatMode)
                {
                    _tokenTracker.CurrentMode = (ChatMode)ModeSelector.SelectedItem;
                    _tokenTracker.ResetCharacterCount(_tokenTracker.TotalCharacterCount);
                }
            };
        }


        /// <summary>
        /// Resets the character count to a specific value (used after summarization).
        /// </summary>
        public void ResetCharacterCount(int newCount = 0)
        {
            _tokenTracker.ResetCharacterCount(newCount);
        }

        /// <summary>
        /// Adds characters to the token counter without printing them.
        /// </summary>
        public void AddToCharacterCount(int delta)
        {
            _tokenTracker.AddToCharacterCount(delta);
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
            _tokenTracker.OnTextAppended(text.Length);

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
            
            // Defer scroll so it runs after the layout pass measures the new content
            Dispatcher.BeginInvoke(new Action(() => OutputTextBox.ScrollToEnd()),
                System.Windows.Threading.DispatcherPriority.Background);
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
                _tokenTracker.Reset();

                // Reset step planner display
                if (_stepPlanner != null)
                {
                    _stepPlanner.Reset();
                    _stepPlanner = null;
                }
                StepStatusText.Visibility = Visibility.Collapsed;
                StepStatusText.ToolTip = null;
            }, Dispatcher);
        }

        /// <summary>
        /// Updates the step progress indicator in the status bar.
        /// Must be called on the UI thread.
        /// </summary>
        private void RefreshStepDisplay()
        {
            if (_stepPlanner == null || _stepPlanner.Steps.Count == 0)
            {
                StepStatusText.Visibility = Visibility.Collapsed;
                StepStatusText.ToolTip = null;
                return;
            }

            StepStatusText.Text = _stepPlanner.GetStepIndicator();
            StepStatusText.ToolTip = _stepPlanner.GetDetailedTooltip();
            StepStatusText.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Hides the step progress indicator.
        /// </summary>
        private void HideStepDisplay()
        {
            EditorService.BeginInvokeOnUIThread(() =>
            {
                StepStatusText.Visibility = Visibility.Collapsed;
                StepStatusText.ToolTip = null;
            }, Dispatcher);
        }

        /// <summary>
        /// Sets the output text, replacing any existing content.
        /// </summary>
        public void SetOutput(string text)
        {
            EditorService.InvokeOnUIThread(() =>
            {
                OutputTextBox.Document.Blocks.Clear();
                _tokenTracker.ResetCharacterCount(text != null ? text.Length : 0);
                var paragraph = new Paragraph(new Run(text)) { Margin = new Thickness(0), Padding = new Thickness(0) };
                OutputTextBox.Document.Blocks.Add(paragraph);
                Dispatcher.BeginInvoke(new Action(() => OutputTextBox.ScrollToEnd()),
                    System.Windows.Threading.DispatcherPriority.Background);
            }, Dispatcher);
        }

        /// <summary>
        /// Scrolls the output box to the bottom.
        /// </summary>
        public void ScrollToBottom()
        {
            EditorService.InvokeOnUIThread(() =>
                Dispatcher.BeginInvoke(new Action(() => OutputTextBox.ScrollToEnd()),
                    System.Windows.Threading.DispatcherPriority.Background), Dispatcher);
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
        /// Delegates to the InteractionManager.
        /// </summary>
        public ApprovalResult RequestToolApproval(string toolName, string arguments)
        {
            return _interactionManager.RequestToolApproval(toolName, arguments);
        }

        /// <summary>
        /// Prompts the user with a question and preset options.
        /// Delegates to the InteractionManager.
        /// </summary>
        public string RequestUserQuestion(string question, string[] options)
        {
            return _interactionManager.RequestUserQuestion(question, options);
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
        /// Handles the New Chat button click — clears the session and resets the input bar.
        /// </summary>
        private void NewChatButton_Click(object sender, RoutedEventArgs e)
        {
            NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
            if (package == null) return;

            if (Interlocked.CompareExchange(ref package._isAiRunning, 1, 0) != 0)
            {
                MessageBox.Show(
                    "An AI request is already in progress. Please wait for it to complete.",
                    "NyoCoder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            LLMClient newClient = LLMClient.CreateFromConfig();
            if (newClient == null)
            {
                Interlocked.Exchange(ref package._isAiRunning, 0);
                return;
            }

            package.LlmClient = newClient;
            ClearOutput();
            ShowInputBar();
            Interlocked.Exchange(ref package._isAiRunning, 0);
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

            // Read selected mode directly from the ComboBox — driven by the ChatMode enum.
            ChatMode chatMode = ModeSelector.SelectedItem is ChatMode ? (ChatMode)ModeSelector.SelectedItem : ChatMode.Agent;

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

            string userMessageDisplay = message;
            if (!string.IsNullOrEmpty(attachedImage))
                userMessageDisplay += " [Image attached]";

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
                    // Initialize step planner for new sessions so the LLM can use manage_plan
                    if (isNewSession)
                    {
                        _stepPlanner = StepPlanner.Initialize();
                        _stepPlanner.StepsChanged += delegate
                        {
                            EditorService.BeginInvokeOnUIThread(() => RefreshStepDisplay(), Dispatcher);
                        };
                    }

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
                        },
                        mode: chatMode
                    );

                    // Handle plan review UI or hide stale step display
                    if (chatMode == ChatMode.Plan)
                    {
                        // ── Plan mode: always show the review UI after the LLM responds ──
                        HandlePlanReview(llmClient, chatMode);
                    }
                    else
                    {
                        // ── Agent / Debug mode: hide stale step display if no new plan was created ──
                        StepPlanner currentPlanner = StepPlanner.Instance;
                        if (currentPlanner != null && !currentPlanner.PlanRequiresExecution && !isNewSession)
                        {
                            // Follow-up message that didn't produce a new plan — hide any previous plan display
                            EditorService.BeginInvokeOnUIThread(() =>
                            {
                                StepStatusText.Visibility = Visibility.Collapsed;
                                StepStatusText.ToolTip = null;
                            }, Dispatcher);
                        }
                    }

                    // Execute step plan if the Agent created one (covers both Agent mode and Plan→Execute handoff)
                    StepPlanner planner = StepPlanner.Instance;
                    if (planner != null && planner.PlanRequiresExecution)
                        ExecutePlan(planner, llmClient);

                    AppendText(Environment.NewLine);

                    // Show input bar again when done
                    ShowInputBar();
                    ScrollToBottom();
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
                    ScrollToBottom();
                }
                finally
                {
                    // Reset the AI running flag
                    Interlocked.Exchange(ref package._isAiRunning, 0);
                }
            });
        }

        /// <summary>
        /// Executes a plan via StepExecutor with full Agent tools.
        /// </summary>
        private void ExecutePlan(StepPlanner planner, LLMClient llmClient)
        {
            var executor = new StepExecutor(
                planner,
                llmClient,
                delegate(string text) { AppendText(text); },
                delegate(string toolName, string arguments) { return RequestToolApproval(toolName, arguments); },
                delegate() { return IsStopRequested(); });

            executor.ExecutionStarted += delegate(int prePlanCharCount)
            {
                _tokenTracker.BeginStepTracking(prePlanCharCount);
            };

            executor.MainTokenCountChanged += delegate(int count)
            {
                _tokenTracker.SyncMainCount(count);
            };

            executor.StepTokenCountChanged += delegate(int count)
            {
                _tokenTracker.SyncStepCount(count);
            };

            executor.ExecutionFinished += delegate(int finalCharCount)
            {
                _tokenTracker.EndStepTracking(finalCharCount);
            };

            executor.Execute();
        }

        /// <summary>
        /// Handles the plan review loop for Plan mode.
        /// Shows the plan to the user and lets them execute, refine, or cancel.
        /// On Execute, hands off to Agent mode to implement the plan.
        /// </summary>
        private void HandlePlanReview(LLMClient llmClient, ChatMode planMode)
        {
            while (true)
            {
                if (IsStopRequested())
                    break;

                string refineText;
                PlanReviewResult reviewResult = _interactionManager.RequestPlanReview(out refineText);

                if (reviewResult == PlanReviewResult.Execute)
                {
                    // Switch to Agent mode — plan is approved, implementation begins
                    EditorService.InvokeOnUIThread(() =>
                    {
                        ModeSelector.SelectedItem = ChatMode.Agent;
                    }, Dispatcher);

                    // Hand off to Agent mode — it implements the plan using the full tool set
                    AppendLine("\n[Handing off to Agent for implementation...]\n");
                    AppendLine("\nAssistant: ");

                    llmClient.ProcessConversation(
                        "The plan above has been approved. Please implement it now. Use manage_plan to track your progress through the steps if the tool is available.",
                        null,
                        "Assistant",
                        null,
                        true,
                        delegate(string text) { AppendText(text); },
                        delegate(string toolName, string arguments) { return RequestToolApproval(toolName, arguments); },
                        stopRequested: delegate() { return IsStopRequested(); },
                        onSummarized: delegate(int newCharCount) { ResetCharacterCount(newCharCount); },
                        mode: ChatMode.Agent
                    );

                    break;
                }
                else if (reviewResult == PlanReviewResult.Refine)
                {
                    // Send feedback back to the LLM in Plan mode for revision
                    AppendLine("\nAssistant: ");

                    llmClient.ProcessConversation(
                        refineText,
                        null,
                        "Assistant",
                        null,
                        true,
                        delegate(string text) { AppendText(text); },
                        delegate(string toolName, string arguments) { return RequestToolApproval(toolName, arguments); },
                        stopRequested: delegate() { return IsStopRequested(); },
                        onSummarized: delegate(int newCharCount) { ResetCharacterCount(newCharCount); },
                        mode: planMode
                    );

                    // Loop back to show the review UI again with the revised plan
                    continue;
                }
                else // Cancel
                {
                    AppendLine("\n[Plan cancelled]\n");
                    HideStepDisplay();
                    break;
                }
            }
        }
    }
}