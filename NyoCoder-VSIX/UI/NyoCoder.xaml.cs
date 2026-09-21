using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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

            ChatTurn.ThinkingExpanderStyle = TryFindResource("ThinkingExpanderStyle") as Style;

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
                modeId => EditorService.InvokeOnUIThread(() => SelectModeById(modeId), Dispatcher),
                () => EditorService.BeginInvokeOnUIThread(RefreshStepDisplay, Dispatcher),
                ScrollToBottom,
                _tokenTracker,
                _interactionManager,
                Dispatcher);

            ModeSelector.DisplayMemberPath = "DisplayName";
            ModeSelector.SelectedValuePath = "Id";
            RefreshModeSelector();
            ModeRegistry.ModesChanged += OnModesChanged;
            ModeSelector.SelectionChanged += ModeSelector_SelectionChanged;

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

            // VS re-fires Loaded (rebuilding the visual tree, including the ScrollViewer) far
            // more often than the control is actually destroyed — e.g. tab switches, docking
            // changes, and reattaching after a debug session. A freshly generated ScrollViewer
            // always starts at VerticalOffset 0, so re-sync to the bottom of the existing chat
            // history whenever the control (re)loads.
            _chatScrollViewer = null;
            ScrollToBottom();
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

        public bool StopRequested
        {
            get { return _stopRequested; }
            set { _stopRequested = value; }
        }

    }
}
