using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NyoCoder
{
    /// <summary>
    /// UserControl that hosts the NyoCoder output pane content.
    /// </summary>
    public partial class NyoCoderControl : UserControl
    {
        private volatile bool _stopRequested;

        // Image attachment
        private string _attachedImageBase64;

        // Manages blocking approval / question prompts
        private InteractionManager _interactionManager;

        // Owns main + step character counts and status-bar labels
        private TokenTracker _tokenTracker;

        // Owns background conversation loop, plan execution, and plan review
        private MessageDispatcher _dispatcher;

        private readonly ObservableCollection<ChatTurn> _chatTurns = new ObservableCollection<ChatTurn>();
        private ChatTurn _currentTurn;
        private ScrollViewer _chatScrollViewer;

        private const string SteerInputTooltip =
            "Queue a message to steer the conversation after the current tool call or response";

        private const string StopGenerationTooltip =
            "Stop the current generation";

        private const string WelcomeMessage =
            "NyoCoder is ready. Type a message below, or press Ctrl+Alt+N from anywhere in Visual Studio.";

        public NyoCoderControl()
        {
            InitializeComponent();

            ChatList.ItemsSource = _chatTurns;
            ShowWelcomeTurn();

            _interactionManager = new InteractionManager(
                ButtonPanel,
                AppendText,
                ScrollToBottom,
                hideInputBar: () => InputBar.Visibility = Visibility.Collapsed,
                showInputBar: () => InputBar.Visibility = Visibility.Visible,
                startBlock: StartOutputBlock);
            _interactionManager.StopRequested += () => { StopRequested = true; };
            _tokenTracker = new TokenTracker(TokenStatusText, StepTokenStatusText, SubagentStatusRow, Dispatcher);
            _dispatcher = new MessageDispatcher(
                AppendText,
                StartOutputBlock,
                AppendLine,
                ApplyMarkdown,
                () => StopRequested,
                ResetCharacterCount,
                AddToCharacterCount,
                () => SetInputBarGenerationMode(false),
                HideStepDisplay,
                mode => EditorService.InvokeOnUIThread(() => ModeSelector.SelectedItem = mode, Dispatcher),
                () => EditorService.BeginInvokeOnUIThread(RefreshStepDisplay, Dispatcher),
                ScrollToBottom,
                _tokenTracker,
                _interactionManager,
                Dispatcher);

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

            // Keep the persistent indexing status bar in sync with the shared reporter. This
            // control is created once for the life of the tool window, so we subscribe once and
            // never unsubscribe — VS toggles WPF Loaded/Unloaded on tab switches and docking
            // changes far more often than the control is actually destroyed, and unsubscribing
            // on Unloaded (without a matching re-subscribe on the next Loaded) silently stops
            // the status bar from ever updating again.
            IndexingStatusReporter.StatusChanged += OnIndexingStatusChanged;
            this.Loaded += NyoCoderControl_Loaded;
            RefreshIndexingStatus();
        }

        private void NyoCoderControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Populate the bar with the current on-disk index status without blocking the UI.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { CodebaseIndex.PublishStatus(); }
                catch { }
            });
        }

        private void OnIndexingStatusChanged()
        {
            EditorService.BeginInvokeOnUIThread(RefreshIndexingStatus, Dispatcher);
        }

        /// <summary>
        /// Updates the persistent indexing status bar from the shared reporter. The bar is only
        /// shown when indexing is enabled (mode != Off).
        /// </summary>
        private void RefreshIndexingStatus()
        {
            IndexingStatusSnapshot snapshot = IndexingStatusReporter.Current;
            bool visible = ConfigHandler.GetIndexingMode() != IndexingMode.Off;
            IndexingStatusBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
                return;

            IndexingStatusText.Text = string.IsNullOrEmpty(snapshot.BriefText) ? "Index: idle" : snapshot.BriefText;
            IndexingStatusText.ToolTip = string.IsNullOrEmpty(snapshot.DetailText) ? null : snapshot.DetailText;
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

            _currentTurn.TrimTrailingBlankParagraphs();
            _currentTurn = null;
        }

        private void AppendTextInternal(string text)
        {
            _tokenTracker.OnTextAppended(text.Length);

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
                    MarkdownHandler.ProcessMarkdown(
                        turn.Document,
                        ref turn.MarkdownProcessedBlockCount);
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
            ChatTurn turn = new ChatTurn(_chatTurns.Count > 0);
            if (ChatList.FontSize > 0)
                turn.Document.FontSize = ChatList.FontSize;
            if (ChatList.Foreground != null)
                turn.Document.Foreground = ChatList.Foreground;
            ApplyDocumentPageWidth(turn);
            _chatTurns.Add(turn);
            return turn;
        }

        private void ShowWelcomeTurn()
        {
            ChatTurn welcome = AddTurn();
            welcome.AppendText(WelcomeMessage);
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
                turn.Document.PageWidth = width;
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
            EditorService.BeginInvokeOnUIThread(ScrollChatToEnd, Dispatcher);
        }

        public bool StopRequested
        {
            get { return _stopRequested; }
            set { _stopRequested = value; }
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
        /// Shows the input bar in idle mode (Send) after generation completes.
        /// </summary>
        public void ShowInputBar()
        {
            SetInputBarGenerationMode(false);
        }

        /// <summary>
        /// Switches the input bar between idle (Send / New Chat) and generation (Steer / Stop) modes.
        /// </summary>
        private void SetInputBarGenerationMode(bool generating)
        {
            EditorService.InvokeOnUIThread(() =>
            {
                InputBar.Visibility = Visibility.Visible;
                InputSendButton.Content = generating ? "Steer" : "Send";
                InputSendButton.ToolTip = generating ? SteerInputTooltip : null;
                InputBox.ToolTip = generating ? SteerInputTooltip : null;
                NewChatButton.Content = generating ? "Stop" : "New Chat";
                NewChatButton.ToolTip = generating ? StopGenerationTooltip : null;
                ModeSelector.IsEnabled = !generating;
                AttachImageButton.IsEnabled = !generating;

                if (!generating)
                {
                    _dispatcher.ClearSteerQueue();
                }

                InputBox.Focus();
            }, Dispatcher);
        }

        /// <summary>
        /// Handles the Send / Steer button click.
        /// </summary>
        private void InputSendButton_Click(object sender, RoutedEventArgs e)
        {
            SubmitInputMessage();
        }

        /// <summary>
        /// Handles the New Chat / Stop button click.
        /// While generating, requests a stop; otherwise starts a fresh session.
        /// </summary>
        private void NewChatButton_Click(object sender, RoutedEventArgs e)
        {
            NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
            if (package == null) return;

            // Stop button while a generation is running
            if (Interlocked.CompareExchange(ref package._isAiRunning, 0, 0) != 0)
            {
                StopRequested = true;
                return;
            }

            if (Interlocked.CompareExchange(ref package._isAiRunning, 1, 0) != 0)
                return;

            LLMClient newClient = LLMClient.CreateFromConfig();
            if (newClient == null)
            {
                Interlocked.Exchange(ref package._isAiRunning, 0);
                return;
            }

            package.LlmClient = newClient;
            ClearOutput();
            InputBox.Clear();
            _dispatcher.ClearSteerQueue();
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
                    return;

                e.Handled = true;
                SubmitInputMessage();
            }
        }

        /// <summary>
        /// Submits the input box — starts a conversation when idle, queues steer when generating.
        /// </summary>
        private void SubmitInputMessage()
        {
            string message = InputBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return;

            NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
            if (package != null && Interlocked.CompareExchange(ref package._isAiRunning, 0, 0) != 0)
            {
                _dispatcher.QueueSteer(message);
                InputBox.Clear();
                return;
            }

            StartConversation(message);
        }

        /// <summary>
        /// Starts a new conversation turn.
        /// </summary>
        private void StartConversation(string message)
        {
            // Read selected mode directly from the ComboBox — driven by the ChatMode enum.
            ChatMode chatMode = ModeSelector.SelectedItem is ChatMode ? (ChatMode)ModeSelector.SelectedItem : ChatMode.Agent;

            // Get package instance and LLM client
            NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
            LLMClient llmClient = package != null ? package.LlmClient : null;

            // Determine if this is a new session (no client or empty conversation)
            bool isNewSession = llmClient == null || llmClient.Conversation == null || llmClient.Conversation.Count == 0;

            // Check if an AI request is already running
            if (Interlocked.CompareExchange(ref package._isAiRunning, 1, 0) != 0)
                return;

            // Get attached image before clearing
            string attachedImage = _attachedImageBase64;

            // Clear attached image and reset button (setting IsChecked=false fires AttachImageButton_Unchecked)
            AttachImageButton.IsChecked = false;
            InputBox.Clear();
            SetInputBarGenerationMode(true);

            // For new sessions, validate config, create LLM client, and clear output
            if (isNewSession)
            {
                LLMClient newClient = LLMClient.CreateFromConfig();
                if (newClient == null)
                {
                    Interlocked.Exchange(ref package._isAiRunning, 0);
                    ShowInputBar();
                    return;
                }

                llmClient = newClient;
                package.LlmClient = llmClient;
                ClearOutput();
            }

            string userMessageDisplay = message;
            if (!string.IsNullOrEmpty(attachedImage))
                userMessageDisplay += " [Image attached]";

            StartOutputBlock();
            AppendLine("User: " + userMessageDisplay);

            StopRequested = false;

            // Save all open files
            try { package.SaveAllOpenFiles(); } catch { }

            var builtMessage = _dispatcher.BuildUserMessage(message, isNewSession);
            _dispatcher.RunConversation(builtMessage, attachedImage, llmClient, chatMode, isNewSession, package);
        }
    }
}
