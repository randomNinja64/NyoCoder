using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NyoCoder
{
    /// <summary>
    /// Chat history: auto-saves the chat after every turn and lists the workspace's saved
    /// chats in a panel over the chat, where one can be opened, exported or deleted.
    /// </summary>
    public partial class NyoCoderControl
    {
        private readonly ObservableCollection<ChatHistoryEntry> _historyEntries =
            new ObservableCollection<ChatHistoryEntry>();

        // The file the chat saves to, or null for a new chat that hasn't been saved yet.
        private string _sessionPath;
        private string _sessionTitle;
        private string _sessionWorkspace;

        // The mode last saved, so a mode change saves only when it differs.
        private string _sessionMode;

        // Set when the user leaves a chat while a reply is generating: the stopped turn
        // isn't saved, and _afterRun (the switch) runs once the run is over.
        private volatile bool _discardTurn;
        private Action _afterRun;

        private const string HistoryIconResource = "NyoCoder.History.png";

        private void InitializeHistory()
        {
            _dispatcher.TurnEnding = OnTurnEnding;
            _dispatcher.RunFinished = OnRunFinished;
            HistoryList.ItemsSource = _historyEntries;
            LoadHistoryIcon();

            // Lives as long as the tool window, like the indexing status subscription.
            ConfigHandler.Saved += () => EditorService.BeginInvokeOnUIThread(RefreshHistoryAvailability, Dispatcher);
            RefreshHistoryAvailability();
        }

        /// <summary>Our history icon, drawn in the theme's text color.</summary>
        private void LoadHistoryIcon()
        {
            try
            {
                using (Stream stream = typeof(NyoCoderControl).Assembly.GetManifestResourceStream(HistoryIconResource))
                {
                    if (stream != null)
                    {
                        BitmapFrame icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        HistoryIcon.OpacityMask = new ImageBrush(icon);
                        TokenStatusText.SizeChanged += (s, e) => SizeHistoryButton();
                        HistoryButton.SizeChanged += (s, e) => FitHistoryIcon();
                        return;
                    }
                }
            }
            catch (Exception)
            {
            }
            HistoryButton.Content = "History";
        }

        /// <summary>
        /// A square a little taller than the token text; equal negative margins let it overhang
        /// into the bar's padding (3px), so the bar stays one text line tall.
        /// </summary>
        private void SizeHistoryButton()
        {
            const double overhang = 2;
            double side = TokenStatusText.ActualHeight + 2 * overhang;
            HistoryButton.Width = side;
            HistoryButton.Height = side;
            HistoryButton.Margin = new Thickness(6, -overhang, 0, -overhang);
        }

        /// <summary>
        /// Scales the icon to the space inside the button: the toolbar style centers its
        /// content rather than stretching it.
        /// </summary>
        private void FitHistoryIcon()
        {
            Thickness border = HistoryButton.BorderThickness;
            Thickness padding = HistoryButton.Padding;
            double inside = Math.Floor(Math.Min(
                HistoryButton.ActualHeight - border.Top - border.Bottom - padding.Top - padding.Bottom,
                HistoryButton.ActualWidth - border.Left - border.Right - padding.Left - padding.Right));
            if (inside <= 0)
                return;
            HistoryIcon.Width = inside;
            HistoryIcon.Height = inside;
        }

        private static bool IsHistoryEnabled()
        {
            return ConfigHandler.GetChatHistoryLimit() > 0;
        }

        private void RefreshHistoryAvailability()
        {
            bool enabled = IsHistoryEnabled();
            HistoryButton.IsEnabled = enabled;
            HistoryButton.Opacity = enabled ? 1.0 : 0.4;
            HistoryButton.ToolTip = enabled ? "History" : "History (off: set Chat History in the Context options)";
            if (!enabled)
                CloseHistory();
        }

        // ── Saving ─────────────────────────────────────────────────────

        private void ResetSession()
        {
            _sessionPath = null;
            _sessionTitle = null;
            _sessionWorkspace = null;
            _sessionMode = null;
        }

        /// <summary>
        /// The first message names the chat until the model's title replaces it.
        /// </summary>
        private void NameChatFrom(string typedMessage)
        {
            if (_sessionTitle == null)
                _sessionTitle = ChatHistory.MakeTitle(typedMessage);
        }

        // On the conversation thread, after every turn: finished, stopped or failed.
        private void OnTurnEnding(LLMClient llmClient, string typedMessage, bool isNewSession)
        {
            if (_discardTurn || !IsHistoryEnabled())
                return;

            // Titles name saved chats, so only with history on; and not if stopped.
            // The reply is read here, on the conversation thread that owns the list.
            string firstReply = null;
            if (isNewSession && !StopRequested && ConfigHandler.GetChatTitlesByModel())
                firstReply = LatestAssistantReply(llmClient);

            EditorService.InvokeOnUIThread(() =>
            {
                if (_discardTurn)
                    return;
                SaveSession(llmClient);
                if (firstReply != null)
                    StartTitleGeneration(llmClient, typedMessage, firstReply);
            }, Dispatcher);
        }

        private static string LatestAssistantReply(LLMClient llmClient)
        {
            List<LLMClient.ChatMessage> conversation = llmClient.Conversation;
            for (int i = conversation.Count - 1; i >= 0; i--)
            {
                if (conversation[i].Role == "assistant" && !TextNormalization.IsNullOrWhiteSpace(conversation[i].Content))
                    return conversation[i].Content;
            }
            return null;
        }

        // The chat whose model title arrived mid-run; OnRunFinished saves it.
        private string _titleSavePendingPath;

        /// <summary>
        /// Asks the model for a title in the background, so the turn ends (and the input
        /// bar returns) without waiting for it. The chat is already saved under its
        /// first-message title; the model's title replaces it when it arrives, unless the
        /// user has left the chat. Any failure keeps the first-message title. UI thread.
        /// </summary>
        private void StartTitleGeneration(LLMClient llmClient, string prompt, string reply)
        {
            string path = _sessionPath;
            if (path == null)
                return;

            Thread thread = new Thread(() =>
            {
                string title;
                try
                {
                    title = ChatHistory.CleanModelTitle(llmClient.GenerateTitle(prompt, reply));
                }
                catch (Exception)
                {
                    return;
                }
                if (title == null)
                    return;

                EditorService.BeginInvokeOnUIThread(() => ApplyModelTitle(llmClient, path, title), Dispatcher);
            });
            thread.IsBackground = true;
            thread.Name = "NyoCoder chat title";
            thread.Start();
        }

        // UI thread. Turns start on the UI thread, so a run can't begin during the save.
        private void ApplyModelTitle(LLMClient llmClient, string path, string title)
        {
            NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
            if (package == null || _sessionPath != path || package.LlmClient != llmClient)
                return; // left the chat

            _sessionTitle = title;
            if (IsAiRunning(package))
            {
                // A turn is running: its end-of-turn save takes the title, or, if it
                // was already past that, OnRunFinished saves it.
                _titleSavePendingPath = path;
                return;
            }
            try
            {
                SaveSession(llmClient);
            }
            catch (Exception)
            {
                // Best-effort, like the end-of-turn save.
            }
        }

        /// <summary>Writes the chat to its file, creating it on the first save. UI thread.</summary>
        private void SaveSession(LLMClient llmClient)
        {
            int limit = ConfigHandler.GetChatHistoryLimit();
            if (limit <= 0 || llmClient == null || llmClient.Conversation == null || llmClient.Conversation.Count == 0)
                return;

            if (_sessionPath == null)
            {
                string workspace = ChatHistory.CurrentWorkspace();
                if (workspace == null)
                    return;
                _sessionWorkspace = workspace;
                _sessionPath = ChatHistory.NewSessionPath(workspace);
            }

            ChatSessionData chat = new ChatSessionData
            {
                Title = _sessionTitle,
                Workspace = _sessionWorkspace,
                Mode = SelectedModeId(),
                Messages = new List<LLMClient.ChatMessage>(llmClient.Conversation)
            };

            try
            {
                ChatHistory.Save(_sessionPath, chat);
                _sessionMode = chat.Mode;
                ChatHistory.Prune(limit, _sessionPath);
            }
            catch (Exception ex)
            {
                StartOutputBlock();
                AppendLine("[Error auto-saving chat: " + ex.Message + "]");
            }

            if (HistoryPanel.Visibility == Visibility.Visible)
                RefreshHistory();
        }

        /// <summary>A mode change is saved with the chat; mid-run, the end-of-turn save takes it.</summary>
        private void SaveModeChange()
        {
            // No selection: the list is being rebuilt (modes edited), and reselects next.
            NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
            if (_sessionPath == null || package == null || IsAiRunning(package)
                || ModeSelector.SelectedItem == null
                || string.Equals(SelectedModeId(), _sessionMode, StringComparison.OrdinalIgnoreCase))
                return;
            SaveSession(package.LlmClient);
        }

        private string SelectedModeId()
        {
            string modeId = ModeSelector.SelectedValue as string;
            return string.IsNullOrEmpty(modeId) ? ModeIds.Agent : modeId;
        }

        private static bool IsAiRunning(NyoCoder_VSIXPackage package)
        {
            return Interlocked.CompareExchange(ref package._isAiRunning, 0, 0) != 0;
        }

        // ── Leaving a chat ─────────────────────────────────────────────

        /// <summary>
        /// Runs <paramref name="then"/> now, or, while a reply is generating, stops it and
        /// runs it once the run is over (the stopped turn isn't saved). With
        /// <paramref name="ask"/>, the user confirms first. False if they chose to stay.
        /// </summary>
        private bool LeaveChat(Action then, bool ask)
        {
            NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
            if (package == null || !IsAiRunning(package))
            {
                then();
                return true;
            }

            if (ask)
            {
                MessageBoxResult answer = MessageBox.Show(
                    "A reply is still being generated. Stop it and leave this chat? The unfinished turn won't be saved.",
                    "Chat in Progress", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                    return false;
            }

            _discardTurn = true;
            _afterRun = then;
            StopRequested = true;
            _interactionManager.CancelPending();
            return true;
        }

        // On the conversation thread, once the run is over.
        private void OnRunFinished()
        {
            EditorService.BeginInvokeOnUIThread(() =>
            {
                Action then = _afterRun;
                _afterRun = null;

                // A model title that arrived mid-run, after its turn-end save. Not for a
                // discarded turn: saving now would keep the turn the user left.
                string titlePath = _titleSavePendingPath;
                _titleSavePendingPath = null;
                NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
                if (titlePath != null && !_discardTurn && titlePath == _sessionPath && package != null)
                {
                    try { SaveSession(package.LlmClient); }
                    catch (Exception) { }
                }

                _discardTurn = false;
                if (then != null)
                    then();
            }, Dispatcher);
        }

        /// <summary>Starts an empty chat; the next message creates the client.</summary>
        private void StartNewChat()
        {
            NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
            if (package == null || Interlocked.CompareExchange(ref package._isAiRunning, 1, 0) != 0)
                return;

            package.LlmClient = null;
            ClearOutput();
            InputBox.Clear();
            _dispatcher.ClearSteerQueue();
            Interlocked.Exchange(ref package._isAiRunning, 0);
        }

        /// <summary>
        /// The solution opened or closed: its chats are another workspace's, so a chat in
        /// progress is left for a new one, without asking (VS has already switched).
        /// </summary>
        public void OnSolutionChanged()
        {
            EditorService.InvokeOnUIThread(() =>
            {
                if (HistoryPanel.Visibility == Visibility.Visible)
                    RefreshHistory();

                NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
                if (package == null)
                    return;
                bool hasChat = IsAiRunning(package) || _sessionPath != null
                    || (package.LlmClient != null && package.LlmClient.Conversation != null
                        && package.LlmClient.Conversation.Count > 0);
                if (hasChat)
                    LeaveChat(StartNewChat, false);
            }, Dispatcher);
        }

        // ── Loading ────────────────────────────────────────────────────

        private void LoadChat(string path)
        {
            NyoCoder_VSIXPackage package = NyoCoder_VSIXPackage.Instance;
            if (package == null || Interlocked.CompareExchange(ref package._isAiRunning, 1, 0) != 0)
                return;

            try
            {
                ChatSessionData chat;
                try
                {
                    chat = ChatHistory.Load(path);
                }
                catch (Exception ex)
                {
                    ShowError("Failed to load chat: " + ex.Message);
                    return;
                }

                LLMClient client = LLMClient.CreateFromConfig();
                if (client == null)
                    return;
                client.Conversation = chat.Messages;
                package.LlmClient = client;

                ClearOutput();
                InputBox.Clear();
                _dispatcher.ClearSteerQueue();
                _dispatcher.BeginSession();

                ReplayConversation(chat.Messages);
                _tokenTracker.ResetCharacterCount(client.GetConversationCharacterCount(client.Conversation));

                // Before the session fields are set, so selecting it doesn't save the chat.
                string mode = HasMode(chat.Mode) ? chat.Mode : ModeIds.Agent;
                SelectModeById(mode);

                _sessionPath = path;
                _sessionTitle = chat.Title;
                _sessionWorkspace = chat.Workspace;
                _sessionMode = SelectedModeId();
                ScrollToBottom();
            }
            finally
            {
                Interlocked.Exchange(ref package._isAiRunning, 0);
            }
        }

        /// <summary>
        /// Redraws a loaded conversation with the text a live turn writes, so it looks as it
        /// did when it ran under the current display settings. Thinking isn't kept, and a
        /// plan step shows as the label and result the conversation keeps for it.
        /// </summary>
        private void ReplayConversation(List<LLMClient.ChatMessage> conversation)
        {
            bool hideToolArguments = ConfigHandler.GetToolCallDisplayMode() == ChatBlockDisplayMode.Hidden;
            bool hideToolOutput = ConfigHandler.GetToolOutputDisplayMode() == ChatBlockDisplayMode.Hidden;
            bool assistantLabel = false;

            foreach (LLMClient.ChatMessage message in conversation)
            {
                if (message.Role == "user")
                {
                    ReplayTurn("User: " + message.Content + (message.Image != null ? " [Image attached]" : ""));
                    assistantLabel = true;
                    continue;
                }

                if (message.Role == "tool")
                {
                    string content = message.Content ?? string.Empty;
                    ReplayTurn(hideToolOutput
                        ? "[tool output]\nExit Code: " + ReplayedExitCode(content) + "\n"
                        : "[tool output]\n" + content.TrimEnd() + "\n[/tool output]\n");
                    continue;
                }

                // An assistant reply is labelled once per user turn, as live.
                string text = assistantLabel ? "Assistant: \n" : string.Empty;
                assistantLabel = false;
                if (!TextNormalization.IsNullOrWhiteSpace(message.Content))
                    text += message.Content;
                if (text.Length > 0)
                    ReplayTurn(text);

                if (message.ToolCalls == null)
                    continue;
                foreach (ToolHandler.ToolCall call in message.ToolCalls)
                {
                    ReplayTurn("[tool call] " + call.Name + "\n"
                        + (hideToolArguments ? string.Empty : call.Arguments) + "[/tool call]\n");
                }
            }
            ApplyMarkdown();
        }

        private void ReplayTurn(string text)
        {
            ChatTurn turn = AddTurn();
            turn.AppendText(text);
            turn.Complete();
        }

        // Tool results start with ToolHandler.FormatCommandResult's "Exit Code: N" line.
        private static string ReplayedExitCode(string content)
        {
            Match match = Regex.Match(content, @"^Exit Code: (-?\d+)", RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value : "0";
        }

        private bool HasMode(string modeId)
        {
            if (string.IsNullOrEmpty(modeId))
                return false;
            foreach (ModeDefinition mode in ModeSelector.Items)
            {
                if (string.Equals(mode.Id, modeId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ── History panel ──────────────────────────────────────────────

        private void HistoryButton_Checked(object sender, RoutedEventArgs e)
        {
            RefreshHistory();
            HistoryPanel.Visibility = Visibility.Visible;
            HistoryList.Focus();
        }

        private void HistoryButton_Unchecked(object sender, RoutedEventArgs e)
        {
            HistoryPanel.Visibility = Visibility.Collapsed;
        }

        private void HistoryCloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseHistory();
        }

        private void CloseHistory()
        {
            HistoryButton.IsChecked = false;
        }

        private void HistoryPanel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;
            CloseHistory();
            e.Handled = true;
        }

        private void RefreshHistory()
        {
            _historyEntries.Clear();
            string workspace = ChatHistory.CurrentWorkspace();
            if (workspace != null)
            {
                foreach (ChatHistoryEntry entry in ChatHistory.Read(ChatHistory.DirectoryFor(workspace)))
                    _historyEntries.Add(entry);
            }

            HistoryEmptyText.Text = workspace != null ? "(empty)" : "No workspace open.";
            HistoryEmptyText.Visibility = _historyEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SelectCurrentChat();
        }

        // Marks the open chat (its own highlight, kept while arrow keys move the
        // selection) and starts the selection there for keyboard use.
        private void SelectCurrentChat()
        {
            HistoryList.SelectedItem = null;
            foreach (ChatHistoryEntry entry in _historyEntries)
            {
                entry.IsCurrent = IsCurrentChat(entry);
                if (entry.IsCurrent)
                {
                    HistoryList.SelectedItem = entry;
                    HistoryList.ScrollIntoView(entry);
                }
            }
        }

        private bool IsCurrentChat(ChatHistoryEntry entry)
        {
            return string.Equals(entry.Path, _sessionPath, StringComparison.OrdinalIgnoreCase);
        }

        private void HistoryItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ListBoxItem item = sender as ListBoxItem;
            if (item != null)
                OpenHistoryEntry(item.DataContext as ChatHistoryEntry);
        }

        // Opening is on click or Enter, not selection change, so arrowing through the
        // list doesn't load every chat passed. Delete acts on the selected row.
        private void HistoryList_KeyDown(object sender, KeyEventArgs e)
        {
            ChatHistoryEntry entry = HistoryList.SelectedItem as ChatHistoryEntry;
            if (e.Key == Key.Enter)
                OpenHistoryEntry(entry);
            else if (e.Key == Key.Delete && entry != null)
                DeleteChat(entry);
            else
                return;
            e.Handled = true;
        }

        private void OpenHistoryEntry(ChatHistoryEntry entry)
        {
            if (entry == null)
                return;
            if (IsCurrentChat(entry))
            {
                CloseHistory();
                return;
            }

            if (!File.Exists(entry.Path))
            {
                ShowError("Chat file not found.");
                RefreshHistory();
                return;
            }

            string path = entry.Path;
            if (LeaveChat(() => LoadChat(path), true))
                CloseHistory();
        }

        // Built when it opens, for the chat right-clicked.
        private void HistoryList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            DependencyObject node = e.OriginalSource as DependencyObject;
            while (node != null && !(node is ListBoxItem))
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            ChatHistoryEntry entry = node != null ? ((ListBoxItem)node).DataContext as ChatHistoryEntry : null;
            if (entry == null)
            {
                e.Handled = true;   // No menu over empty space.
                return;
            }

            ContextMenu menu = HistoryList.ContextMenu;
            menu.Items.Clear();
            menu.Items.Add(MenuItemFor("Export...", () => ExportChat(entry)));
            menu.Items.Add(MenuItemFor("Delete", () => DeleteChat(entry)));
        }

        private static MenuItem MenuItemFor(string header, Action action)
        {
            MenuItem item = new MenuItem { Header = header };
            item.Click += (s, a) => action();
            return item;
        }

        // A copy of the saved file: auto-save keeps it current after every turn.
        private void ExportChat(ChatHistoryEntry entry)
        {
            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog();
            dialog.Filter = "Chat (*.json)|*.json";
            dialog.FileName = SafeFileName(entry.Title) + ".json";
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                File.Copy(entry.Path, dialog.FileName, true);
            }
            catch (Exception ex)
            {
                ShowError("Failed to export chat: " + ex.Message);
            }
        }

        private void DeleteChat(ChatHistoryEntry entry)
        {
            MessageBoxResult answer = MessageBox.Show(
                "Delete \"" + entry.Title + "\"? This can't be undone.",
                "Delete Chat", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            string path = entry.Path;
            if (!IsCurrentChat(entry))
            {
                DeleteChatFile(path);
                return;
            }

            // Leave it first, or its next auto-save would write it back.
            LeaveChat(() =>
            {
                StartNewChat();
                DeleteChatFile(path);
            }, true);
        }

        private void DeleteChatFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                ShowError("Failed to delete chat: " + ex.Message);
            }
            if (HistoryPanel.Visibility == Visibility.Visible)
                RefreshHistory();
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(message, "NyoCoder", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private static string SafeFileName(string title)
        {
            string name = title ?? "chat";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name.Trim().Length == 0 ? "chat" : name.Trim();
        }
    }
}
