using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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

        private static readonly string[] CollapsibleOpenTags =
        {
            "[thinking]", "<think>", "[tool call]"
        };
        private static readonly string[] CollapsibleCloseTags =
        {
            "[/thinking]", "</think>", "[/tool call]"
        };

        internal enum CollapsibleBlockKind
        {
            Thinking,
            ToolCall
        }

        /// <summary>
        /// Shared UI state for thinking / tool-call expanders.
        /// </summary>
        internal sealed class CollapsibleBlockState
        {
            public bool Active;
            public string Name;
            public CollapsibleBlockKind Kind;
            public Expander Expander;
            public TextBlock HeaderLabel;
            public TextBox BodyText;
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

        private CollapsibleBlockState _activeBlock;

        /// <summary>
        /// One chat output block (user, assistant, tool, etc.) backed by its own FlowDocument.
        /// </summary>
        public ChatTurn()
        {
            Document = new FlowDocument
            {
                PagePadding = new Thickness(0)
            };
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

                // Start real content in a fresh paragraph.
                Document.Blocks.Add(new Paragraph());
            }

            string remaining = text;
            while (!string.IsNullOrEmpty(remaining))
            {
                if (_activeBlock == null)
                {
                    int openIndex;
                    int openLength;
                    if (!TryFindTag(remaining, CollapsibleOpenTags, out openIndex, out openLength))
                    {
                        AppendPlain(remaining);
                        break;
                    }

                    string openTag = remaining.Substring(openIndex, openLength);
                    if (openIndex > 0)
                        AppendPlain(remaining.Substring(0, openIndex));

                    remaining = remaining.Substring(openIndex + openLength);
                    if (IsToolCallOpenTag(openTag))
                    {
                        string name;
                        remaining = TakeToolCallName(remaining, out name);
                        StartCollapsibleBlock(name, CollapsibleBlockKind.ToolCall);
                    }
                    else
                    {
                        remaining = remaining.TrimStart('\r', '\n');
                        StartCollapsibleBlock(null, CollapsibleBlockKind.Thinking);
                    }
                }
                else
                {
                    int closeIndex;
                    int closeLength;
                    if (!TryFindTag(remaining, CollapsibleCloseTags, out closeIndex, out closeLength))
                    {
                        _activeBlock.BodyText.Text += remaining;
                        break;
                    }

                    if (closeIndex > 0)
                        _activeBlock.BodyText.Text += remaining.Substring(0, closeIndex);

                    EndCollapsibleBlock();
                    remaining = remaining.Substring(closeIndex + closeLength).TrimStart('\r', '\n');
                }
            }
        }

        /// <summary>
        /// Removes empty paragraphs left at the end of the document by padding newlines.
        /// </summary>
        public void TrimTrailingBlankParagraphs()
        {
            if (_activeBlock != null)
                EndCollapsibleBlock();

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

        private void StartCollapsibleBlock(string name, CollapsibleBlockKind kind)
        {
            // Drop an empty trailing paragraph left by content setup so the
            // expander is the next visible block (no blank line above it).
            Paragraph last = Document.Blocks.LastBlock as Paragraph;
            if (last != null
                && string.IsNullOrWhiteSpace(new TextRange(last.ContentStart, last.ContentEnd).Text))
            {
                Document.Blocks.Remove(last);
            }

            bool collapseByDefault = kind == CollapsibleBlockKind.ToolCall
                ? ConfigHandler.GetCollapseToolCalls()
                : ConfigHandler.GetCollapseThinkingBlocks();

            var state = new CollapsibleBlockState
            {
                Active = true,
                Name = name,
                Kind = kind,
                EllipsisCount = 1,
                StartedUtc = DateTime.UtcNow
            };

            state.HeaderLabel = new TextBlock();
            state.HeaderLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);

            state.BodyText = new TextBox
            {
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            state.BodyText.SetResourceReference(Control.ForegroundProperty, VsBrushes.WindowTextKey);
            state.BodyText.TextChanged += OnBodyTextChanged;
            state.BodyText.SizeChanged += OnBodySizeChanged;

            state.Expander = new Expander
            {
                Header = state.HeaderLabel,
                Content = state.BodyText,
                IsExpanded = !collapseByDefault,
                Style = ThinkingExpanderStyle,
                Tag = state
            };
            state.Expander.SetResourceReference(Control.ForegroundProperty, VsBrushes.WindowTextKey);
            state.Expander.Expanded += OnBlockExpanded;
            state.Expander.Collapsed += OnBlockCollapsed;

            Document.Blocks.Add(new BlockUIContainer(state.Expander)
            {
                Margin = new Thickness(0)
            });

            _activeBlock = state;
            state.HeaderLabel.Text = BuildLabelText(state);

            if (state.Collapsed)
                StartEllipsisTimer(state);
        }

        private void EndCollapsibleBlock()
        {
            CollapsibleBlockState state = _activeBlock;
            if (state == null)
                return;

            state.Active = false;
            state.DurationSeconds = Math.Max(0, (int)Math.Round((DateTime.UtcNow - state.StartedUtc).TotalSeconds));
            _activeBlock = null;
            StopEllipsisTimer(state);
            state.HeaderLabel.Text = BuildLabelText(state);

            if (state.BodyText.Text != null)
            {
                state.BodyText.Text = state.BodyText.Text.TrimEnd('\r', '\n');
                FitBodyHeight(state.BodyText);
            }
        }

        private static void OnBodyTextChanged(object sender, TextChangedEventArgs e)
        {
            FitBodyHeight(sender as TextBox);
        }

        private static void OnBodySizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged)
                FitBodyHeight(sender as TextBox);
        }

        private static void FitBodyHeight(TextBox box)
        {
            if (box == null)
                return;

            double width = box.ActualWidth;
            FrameworkElement parent = box.Parent as FrameworkElement;
            if (width <= 0 && parent != null && parent.ActualWidth > 0)
                width = parent.ActualWidth;
            if (width <= 0)
                return;

            string text = string.IsNullOrEmpty(box.Text) ? " " : box.Text;
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                box.FlowDirection,
                new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch),
                box.FontSize > 0 ? box.FontSize : 12,
                box.Foreground ?? Brushes.Black);
            formatted.MaxTextWidth = Math.Max(1, width - box.Padding.Left - box.Padding.Right);
            box.Height = Math.Ceiling(formatted.Height) + 2;
        }

        private void OnBlockExpanded(object sender, RoutedEventArgs e)
        {
            Expander expander = sender as Expander;
            CollapsibleBlockState state = expander != null ? expander.Tag as CollapsibleBlockState : null;
            if (state == null)
                return;

            StopEllipsisTimer(state);
            state.HeaderLabel.Text = BuildLabelText(state);
            FitBodyHeight(state.BodyText);
        }

        private void OnBlockCollapsed(object sender, RoutedEventArgs e)
        {
            Expander expander = sender as Expander;
            CollapsibleBlockState state = expander != null ? expander.Tag as CollapsibleBlockState : null;
            if (state == null)
                return;

            if (state.Active)
                StartEllipsisTimer(state);
            else
                state.HeaderLabel.Text = BuildLabelText(state);
        }

        private static void StartEllipsisTimer(CollapsibleBlockState state)
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

        private static void StopEllipsisTimer(CollapsibleBlockState state)
        {
            if (state.Timer == null)
                return;

            state.Timer.Stop();
        }

        private static void OnEllipsisTick(CollapsibleBlockState state)
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

        private static string BuildLabelText(CollapsibleBlockState state)
        {
            if (state.Kind == CollapsibleBlockKind.ToolCall)
            {
                string baseLabel = "tool call: " + (state.Name ?? "tool");
                if (state.Collapsed && state.Active)
                    return baseLabel + new string('.', state.EllipsisCount);
                return baseLabel;
            }

            if (state.Collapsed && state.Active)
                return "thinking" + new string('.', state.EllipsisCount);

            if (state.Collapsed && !state.Active)
            {
                int seconds = state.DurationSeconds;
                return "thought for " + seconds + " second" + (seconds == 1 ? "" : "s");
            }

            return "thinking";
        }

        /// <summary>
        /// After <c>[tool call]</c>, take the tool name from the rest of the line.
        /// </summary>
        private static string TakeToolCallName(string text, out string name)
        {
            if (string.IsNullOrEmpty(text))
            {
                name = "tool";
                return text;
            }

            int newline = text.IndexOf('\n');
            if (newline < 0)
            {
                name = text.Trim();
                if (name.Length == 0)
                    name = "tool";
                return string.Empty;
            }

            name = text.Substring(0, newline).Trim();
            if (name.Length == 0)
                name = "tool";

            string rest = text.Substring(newline + 1);
            if (rest.Length > 0 && rest[0] == '\r')
                rest = rest.Substring(1);
            return rest;
        }

        private static bool IsToolCallOpenTag(string openTag)
        {
            return openTag.Equals(CollapsibleOpenTags[2], StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryFindTag(string text, string[] tags, out int index, out int length)
        {
            index = -1;
            length = 0;

            foreach (string tag in tags)
            {
                int start = 0;
                while (true)
                {
                    int found = text.IndexOf(tag, start, StringComparison.OrdinalIgnoreCase);
                    if (found < 0)
                        break;

                    // Avoid matching an open tag inside its close tag (e.g. "[tool call]" in "[/tool call]").
                    if (found == 0 || text[found - 1] != '/')
                    {
                        if (index < 0 || found < index)
                        {
                            index = found;
                            length = tag.Length;
                        }
                        break;
                    }

                    start = found + 1;
                }
            }

            return index >= 0;
        }
    }
}
