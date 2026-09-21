using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace NyoCoder
{
    public partial class NyoCoderControl
    {
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
            // Read selected mode from the ComboBox — driven by ModeRegistry.
            string modeId = ModeSelector.SelectedValue as string;
            if (string.IsNullOrEmpty(modeId))
                modeId = ModeIds.Agent;

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
            _dispatcher.RunConversation(builtMessage, attachedImage, llmClient, modeId, isNewSession, package);
        }

    }
}
