using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NyoCoder
{
    /// <summary>
    /// Owns main and step-level character counts and keeps two TextBlock status
    /// labels in sync. All public methods are safe to call from any thread;
    /// UI updates are dispatched internally.
    /// </summary>
    internal class TokenTracker
    {
        private readonly TextBlock _tokenStatusText;
        private readonly TextBlock _stepTokenStatusText;
        private readonly Dispatcher _dispatcher;

        private int _totalCharacterCount;
        private int _stepCharacterCount;
        private bool _isTrackingStepTokens;

        public int TotalCharacterCount { get { return _totalCharacterCount; } }
        public int StepCharacterCount { get { return _stepCharacterCount; } }
        public bool IsTrackingStepTokens { get { return _isTrackingStepTokens; } }

        public TokenTracker(TextBlock tokenStatusText, TextBlock stepTokenStatusText, Dispatcher dispatcher)
        {
            _tokenStatusText = tokenStatusText;
            _stepTokenStatusText = stepTokenStatusText;
            _dispatcher = dispatcher;
        }

        // ── Main counter ───────────────────────────────────────────────

        /// <summary>
        /// Resets the main character count (e.g. after summarization).
        /// </summary>
        public void ResetCharacterCount(int newCount = 0)
        {
            EditorService.InvokeOnUIThread(() =>
            {
                _totalCharacterCount = newCount;
                RefreshTokenDisplay();
            }, _dispatcher);
        }

        /// <summary>
        /// Adds a delta to the main character count without printing text.
        /// </summary>
        public void AddToCharacterCount(int delta)
        {
            if (delta == 0)
                return;

            EditorService.BeginInvokeOnUIThread(() =>
            {
                _totalCharacterCount = Math.Max(0, _totalCharacterCount + delta);
                RefreshTokenDisplay();
            }, _dispatcher);
        }

        /// <summary>
        /// Called when text is appended to the output pane. Increments the
        /// appropriate counter (step or main) depending on the current mode.
        /// Must be called on the UI thread.
        /// </summary>
        public void OnTextAppended(int charCount)
        {
            if (charCount <= 0)
                return;

            if (_isTrackingStepTokens)
            {
                _stepCharacterCount += charCount;
                RefreshStepTokenDisplay();
            }
            else
            {
                _totalCharacterCount += charCount;
                RefreshTokenDisplay();
            }
        }

        /// <summary>
        /// Resets both counters and hides the step display. Must be called on
        /// the UI thread.
        /// </summary>
        public void Reset()
        {
            _totalCharacterCount = 0;
            _isTrackingStepTokens = false;
            _stepCharacterCount = 0;
            RefreshTokenDisplay();
            _stepTokenStatusText.Visibility = Visibility.Collapsed;
        }

        // ── Step execution lifecycle ───────────────────────────────────

        /// <summary>
        /// Begins step-level tracking. Sets the main counter to
        /// <paramref name="prePlanCharCount"/> and shows the step token display.
        /// </summary>
        public void BeginStepTracking(int prePlanCharCount)
        {
            _isTrackingStepTokens = true;
            EditorService.InvokeOnUIThread(() =>
            {
                _totalCharacterCount = prePlanCharCount;
                RefreshTokenDisplay();
                _stepTokenStatusText.Visibility = Visibility.Visible;
            }, _dispatcher);
        }

        /// <summary>
        /// Updates the main counter from an external source (e.g. after a step
        /// injects messages into the main conversation).
        /// </summary>
        public void SyncMainCount(int count)
        {
            EditorService.BeginInvokeOnUIThread(() =>
            {
                _totalCharacterCount = count;
                RefreshTokenDisplay();
            }, _dispatcher);
        }

        /// <summary>
        /// Updates the step counter from an external source (e.g. StepExecutor
        /// resetting to pre-plan size or receiving an onSummarized callback).
        /// </summary>
        public void SyncStepCount(int count)
        {
            _stepCharacterCount = count;
            EditorService.InvokeOnUIThread(() => RefreshStepTokenDisplay(), _dispatcher);
        }

        /// <summary>
        /// Ends step-level tracking, syncs the main counter, and hides the step
        /// token display.
        /// </summary>
        public void EndStepTracking(int finalMainCharCount)
        {
            _isTrackingStepTokens = false;
            _stepCharacterCount = 0;
            EditorService.InvokeOnUIThread(() =>
            {
                _totalCharacterCount = finalMainCharCount;
                RefreshTokenDisplay();
                _stepTokenStatusText.Visibility = Visibility.Collapsed;
            }, _dispatcher);
        }

        // ── Display helpers (must be called on UI thread) ──────────────

        private void RefreshTokenDisplay()
        {
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

            _tokenStatusText.Text = statusText;
        }

        private void RefreshStepTokenDisplay()
        {
            int approximateTokens = ContextEngine.ApproximateTokens(_stepCharacterCount);
            int? contextWindowSize = ConfigHandler.ContextWindowSize;

            string statusText;
            if (contextWindowSize.HasValue && contextWindowSize.Value > 0)
            {
                double percentage = (double)approximateTokens / contextWindowSize.Value * 100;
                statusText = string.Format("Step Tokens: ~{0:N0} / {1:N0} ({2:F1}%)",
                    approximateTokens, contextWindowSize.Value, percentage);
            }
            else
            {
                statusText = string.Format("Step Tokens: ~{0:N0}", approximateTokens);
            }

            _stepTokenStatusText.Text = statusText;
        }
    }
}
