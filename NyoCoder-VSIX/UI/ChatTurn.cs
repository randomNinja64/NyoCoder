using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;

namespace NyoCoder
{
    /// <summary>
    /// One chat output block (user, assistant, tool, etc.) backed by its own FlowDocument.
    /// </summary>
    public class ChatTurn
    {
        /// <summary>
        /// Classic +/- expander style provided by <c>NyoCoderControl</c>.
        /// </summary>
        public static Style ThinkingExpanderStyle { get; set; }

        private static readonly string[] OpenTags = { "[thinking]", "<think>" };
        private static readonly string[] CloseTags = { "[/thinking]", "</think>" };

        /// <summary>
        /// Per-block UI state stored on the thinking expander's Tag.
        /// </summary>
        internal sealed class ThinkingBlockState
        {
            public bool Active;
            public Expander Expander;
            public TextBlock HeaderLabel;
            public TextBlock BodyText;
            public DispatcherTimer Timer;
            public int EllipsisCount = 1;
            public DateTime StartedUtc;
            public int DurationSeconds;

            public bool Collapsed
            {
                get { return !Expander.IsExpanded; }
            }
        }

        public FlowDocument Document { get; private set; }

        /// <summary>
        /// Block index already processed by MarkdownHandler for this turn's document.
        /// </summary>
        public int MarkdownProcessedBlockCount;

        // False until visible text is appended; used to drop leading padding newlines.
        private bool _hasContent;

        private ThinkingBlockState _activeThinking;

        /// <param name="leadingSeparator">
        /// When true, starts with a blank paragraph so this turn renders with a blank
        /// line above it (every turn except the first in the conversation).
        /// </param>
        public ChatTurn(bool leadingSeparator = false)
        {
            Document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                LineHeight = 14,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };

            if (leadingSeparator)
                Document.Blocks.Add(new Paragraph());
        }

        public void AppendText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (!_hasContent)
            {
                text = text.TrimStart('\r', '\n');
                if (text.Length == 0)
                    return;
                _hasContent = true;

                // Always start real content in a fresh paragraph, so a leading
                // separator paragraph (if any) is never overwritten.
                Document.Blocks.Add(new Paragraph());
            }

            string remaining = text;
            while (!string.IsNullOrEmpty(remaining))
            {
                if (_activeThinking == null)
                {
                    int openIndex;
                    int openLength;
                    if (!TryFindEarliest(remaining, OpenTags, out openIndex, out openLength))
                    {
                        AppendPlain(remaining);
                        break;
                    }

                    if (openIndex > 0)
                        AppendPlain(remaining.Substring(0, openIndex));

                    StartThinking();
                    remaining = remaining.Substring(openIndex + openLength).TrimStart('\r', '\n');
                }
                else
                {
                    int closeIndex;
                    int closeLength;
                    if (!TryFindEarliest(remaining, CloseTags, out closeIndex, out closeLength))
                    {
                        _activeThinking.BodyText.Text += remaining;
                        break;
                    }

                    if (closeIndex > 0)
                        _activeThinking.BodyText.Text += remaining.Substring(0, closeIndex);

                    EndThinking();
                    remaining = remaining.Substring(closeIndex + closeLength).TrimStart('\r', '\n');
                }
            }
        }

        /// <summary>
        /// Removes empty paragraphs left at the end of the document by padding newlines.
        /// </summary>
        public void TrimTrailingBlankParagraphs()
        {
            if (_activeThinking != null)
                EndThinking();

            while (Document.Blocks.Count > 1)
            {
                Paragraph paragraph = Document.Blocks.LastBlock as Paragraph;
                if (paragraph == null)
                    break;

                string text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
                if (text.Trim().Length != 0)
                    break;

                Document.Blocks.Remove(paragraph);
            }
        }

        private void AppendPlain(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            new TextRange(Document.ContentEnd, Document.ContentEnd).Text = text;
        }

        private void StartThinking()
        {
            // Drop an empty trailing paragraph left by a prior newline so the
            // expander becomes the next visible block.
            Paragraph last = Document.Blocks.LastBlock as Paragraph;
            if (last != null
                && string.IsNullOrWhiteSpace(new TextRange(last.ContentStart, last.ContentEnd).Text))
            {
                Document.Blocks.Remove(last);
            }

            var state = new ThinkingBlockState
            {
                Active = true,
                EllipsisCount = 1,
                StartedUtc = DateTime.UtcNow
            };

            state.HeaderLabel = new TextBlock();
            state.HeaderLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);

            state.BodyText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(0),
                Margin = new Thickness(0)
            };
            state.BodyText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);

            state.Expander = new Expander
            {
                Header = state.HeaderLabel,
                Content = state.BodyText,
                IsExpanded = !ConfigHandler.GetCollapseThinkingBlocks(),
                Style = ThinkingExpanderStyle,
                Tag = state
            };
            state.Expander.SetResourceReference(Control.ForegroundProperty, VsBrushes.WindowTextKey);
            state.Expander.Expanded += OnThinkingExpanded;
            state.Expander.Collapsed += OnThinkingCollapsed;

            Document.Blocks.Add(new BlockUIContainer(state.Expander)
            {
                Margin = new Thickness(0)
            });

            _activeThinking = state;
            state.HeaderLabel.Text = BuildLabelText(state);

            if (state.Collapsed)
                StartEllipsisTimer(state);
        }

        private void EndThinking()
        {
            ThinkingBlockState state = _activeThinking;
            if (state == null)
                return;

            state.Active = false;
            state.DurationSeconds = Math.Max(0, (int)Math.Round((DateTime.UtcNow - state.StartedUtc).TotalSeconds));
            _activeThinking = null;
            StopEllipsisTimer(state);
            state.HeaderLabel.Text = BuildLabelText(state);

            // Drop trailing newlines so the expander doesn't sit on empty line height.
            if (state.BodyText.Text != null)
                state.BodyText.Text = state.BodyText.Text.TrimEnd('\r', '\n');
        }

        private void OnThinkingExpanded(object sender, RoutedEventArgs e)
        {
            Expander expander = sender as Expander;
            ThinkingBlockState state = expander != null ? expander.Tag as ThinkingBlockState : null;
            if (state == null)
                return;

            StopEllipsisTimer(state);
            state.HeaderLabel.Text = BuildLabelText(state);
        }

        private void OnThinkingCollapsed(object sender, RoutedEventArgs e)
        {
            Expander expander = sender as Expander;
            ThinkingBlockState state = expander != null ? expander.Tag as ThinkingBlockState : null;
            if (state == null)
                return;

            if (state.Active)
                StartEllipsisTimer(state);
            else
                state.HeaderLabel.Text = BuildLabelText(state);
        }

        private static void StartEllipsisTimer(ThinkingBlockState state)
        {
            if (state.Timer == null)
            {
                state.Timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(450)
                };
                state.Timer.Tick += (s, e) => OnEllipsisTick(state);
            }

            state.HeaderLabel.Text = BuildLabelText(state);
            if (!state.Timer.IsEnabled)
                state.Timer.Start();
        }

        private static void StopEllipsisTimer(ThinkingBlockState state)
        {
            if (state.Timer == null)
                return;

            state.Timer.Stop();
        }

        private static void OnEllipsisTick(ThinkingBlockState state)
        {
            if (!state.Active || !state.Collapsed)
            {
                StopEllipsisTimer(state);
                state.HeaderLabel.Text = BuildLabelText(state);
                return;
            }

            state.EllipsisCount = state.EllipsisCount >= 3 ? 1 : state.EllipsisCount + 1;
            state.HeaderLabel.Text = BuildLabelText(state);
        }

        private static string BuildLabelText(ThinkingBlockState state)
        {
            if (state.Collapsed && state.Active)
                return "thinking" + new string('.', state.EllipsisCount);

            if (state.Collapsed && !state.Active)
            {
                int seconds = state.DurationSeconds;
                return "thought for " + seconds + " second" + (seconds == 1 ? "" : "s");
            }

            return "thinking";
        }

        private static bool TryFindEarliest(string text, string[] tags, out int index, out int length)
        {
            index = -1;
            length = 0;

            foreach (string tag in tags)
            {
                int found = text.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                    continue;

                if (index < 0 || found < index)
                {
                    index = found;
                    length = tag.Length;
                }
            }

            return index >= 0;
        }
    }
}
