using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;

namespace NyoCoder
{
    /// <summary>
    /// Text-backed chat output with a releasable document and per-block display state.
    /// </summary>
    public class ChatTurn
    {
        /// <summary>
        /// Classic +/- expander style provided by <c>NyoCoderControl</c>.
        /// </summary>
        public static Style ThinkingExpanderStyle { get; set; }

        private static readonly string[] CollapsibleOpenTags =
        {
            "[thinking]", "<think>", "[tool call]", "[tool output]"
        };
        private static readonly string[] CollapsibleCloseTags =
        {
            "[/thinking]", "</think>", "[/tool call]", "[/tool output]"
        };

        internal enum CollapsibleBlockKind
        {
            Thinking,
            ToolCall,
            ToolOutput
        }

        /// <summary>
        /// Shared UI state for thinking / tool-call / tool-output expanders.
        /// </summary>
        internal sealed class CollapsibleBlockState
        {
            public ContentRecord Record;
            public bool Active;
            public string Name;
            public CollapsibleBlockKind Kind;
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

        internal sealed class ContentRecord
        {
            public CollapsibleBlockKind? Kind;
            public string Name;
            public bool Expanded;
            public int DurationSeconds;
            public StringBuilder Buffer = new StringBuilder();
            public string Text;
            public string GetText() { return Text ?? Buffer.ToString(); }
        }

        private readonly List<ContentRecord> _content = new List<ContentRecord>();
        private FlowDocument _document;
        private ContentRecord _restoringRecord;
        private bool _restoring;
        private bool _completed;
        private bool _viewActive = true;
        private bool _markdown;
        private double _fontSize = 12;
        private double _pageWidth = double.NaN;
        public bool IsCompleted { get { return _completed; } }
        public bool HasDocument { get { return _document != null; } }

        public FlowDocument Document
        {
            get
            {
                if (_document == null)
                {
                    _document = CreateDocument();
                    MarkdownProcessedBlockCount = 0;
                    _restoring = true;
                    try
                    {
                        foreach (ContentRecord record in _content)
                        {
                            if (record.Kind.HasValue)
                            {
                                _restoringRecord = record;
                                StartCollapsibleBlock(record.Name, record.Kind.Value);
                                _activeBlock.BodyText.Text = record.GetText();
                                EndCollapsibleBlock();
                            }
                            else AppendPlain(record.GetText());
                        }
                        TrimTrailingBlankParagraphs();
                    }
                    finally { _restoring = false; _restoringRecord = null; }
                    if (_markdown) ProcessMarkdown();
                }
                return _document;
            }
        }

        private FlowDocument CreateDocument()
        {
            FlowDocument document = new FlowDocument
            {
                PagePadding = new Thickness(0), FontSize = _fontSize, PageWidth = _pageWidth
            };
            document.SetResourceReference(FlowDocument.ForegroundProperty, VsBrushes.WindowTextKey);
            return document;
        }

        public void Complete()
        {
            if (_completed) return;
            TrimTrailingBlankParagraphs();
            foreach (ContentRecord record in _content)
            {
                record.Text = record.Buffer.ToString();
                record.Buffer = null;
            }
            _completed = true;
        }

        public void ProcessMarkdown()
        {
            _markdown = true;
            if (_document != null)
                MarkdownHandler.ProcessMarkdown(_document, ref MarkdownProcessedBlockCount);
        }

        public void ApplyFontSize(double size)
        {
            _fontSize = size;
            if (_document != null) _document.FontSize = size;
        }

        public void SetPageWidth(double width)
        {
            _pageWidth = width;
            if (_document != null) _document.PageWidth = width;
        }

        public void ReleaseDocument()
        {
            if (!_completed || _document == null || _document.Parent != null) return;
            _document = null;
        }

        public void SetViewActive(bool active)
        {
            _viewActive = active;
            if (_activeBlock == null) return;
            if (active && _activeBlock.Collapsed) StartEllipsisTimer(_activeBlock);
            else StopEllipsisTimer(_activeBlock);
        }

        /// <summary>
        /// Block index already processed by MarkdownHandler for this turn's document.
        /// </summary>
        public int MarkdownProcessedBlockCount;

        // False until visible text is appended; used to drop leading padding newlines.
        private bool _hasContent;

        private CollapsibleBlockState _activeBlock;

        public ChatTurn()
        {
            _document = CreateDocument();
        }

        public void AppendText(string text)
        {
            if (_completed) throw new InvalidOperationException("Cannot append to a completed turn.");
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
                    else if (IsToolOutputOpenTag(openTag))
                    {
                        // Hidden mode emits a plain "[tool output]\nExit Code: N" stub
                        // (SimpleLLMChat parity) — do not wrap it in an Expander.
                        if (ConfigHandler.GetToolOutputDisplayMode() == ChatBlockDisplayMode.Hidden)
                        {
                            AppendPlain(openTag);
                            continue;
                        }

                        remaining = remaining.TrimStart('\r', '\n');
                        StartCollapsibleBlock(null, CollapsibleBlockKind.ToolOutput);
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
                        AppendBody(remaining);
                        break;
                    }

                    if (closeIndex > 0)
                        AppendBody(remaining.Substring(0, closeIndex));

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

            bool removed = false;
            while (Document.Blocks.Count > 1)
            {
                Paragraph paragraph = Document.Blocks.LastBlock as Paragraph;
                if (paragraph == null)
                    break;

                string text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text;
                if (text.Trim().Length != 0)
                    break;

                Document.Blocks.Remove(paragraph);
                removed = true;
            }
            if (removed && !_restoring && _content.Count != 0)
            {
                ContentRecord last = _content[_content.Count - 1];
                if (!last.Kind.HasValue && last.Buffer != null)
                {
                    string source = last.Buffer.ToString();
                    int end = source.Length;
                    while (end > 0)
                    {
                        int start = end;
                        while (start > 0 && source[start - 1] != '\r' && source[start - 1] != '\n') start--;
                        if (source.Substring(start, end - start).Trim().Length != 0) break;
                        end = start;
                        while (end > 0 && (source[end - 1] == '\r' || source[end - 1] == '\n')) end--;
                    }
                    last.Buffer.Length = end;
                }
            }
        }

        private void AppendPlain(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            if (!_restoring)
            {
                ContentRecord record = _content.Count == 0 ? null : _content[_content.Count - 1];
                if (record == null || record.Kind.HasValue)
                {
                    record = new ContentRecord();
                    _content.Add(record);
                }
                record.Buffer.Append(text);
            }
            new TextRange(Document.ContentEnd, Document.ContentEnd).Text = text;
        }

        private void AppendBody(string text)
        {
            _activeBlock.Record.Buffer.Append(text);
            _activeBlock.BodyText.Text += text;
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

            ChatBlockDisplayMode mode = GetDisplayMode(kind);
            // Shown and Hidden (name-only tool call) start expanded; Collapsed does not.
            bool expandByDefault = _restoring ? _restoringRecord.Expanded : mode != ChatBlockDisplayMode.Collapsed;
            ContentRecord record = _restoring ? _restoringRecord : new ContentRecord
            {
                Kind = kind, Name = name, Expanded = expandByDefault
            };
            if (!_restoring) _content.Add(record);

            var state = new CollapsibleBlockState
            {
                Record = record,
                Active = !_restoring,
                Name = name,
                Kind = kind,
                EllipsisCount = 1,
                StartedUtc = DateTime.UtcNow
            };

            state.HeaderLabel = new TextBlock();
            state.HeaderLabel.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);

            state.BodyText = new TextBlock
            {
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                TextWrapping = TextWrapping.Wrap
            };
            state.BodyText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);

            state.Expander = new Expander
            {
                Header = state.HeaderLabel,
                Content = state.BodyText,
                IsExpanded = expandByDefault,
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

            if (_viewActive && state.Active && state.Collapsed)
                StartEllipsisTimer(state);
        }

        private static ChatBlockDisplayMode GetDisplayMode(CollapsibleBlockKind kind)
        {
            switch (kind)
            {
                case CollapsibleBlockKind.ToolCall:
                    return ConfigHandler.GetToolCallDisplayMode();
                case CollapsibleBlockKind.ToolOutput:
                    return ConfigHandler.GetToolOutputDisplayMode();
                default:
                    return ConfigHandler.GetThinkingDisplayMode();
            }
        }

        private void EndCollapsibleBlock()
        {
            CollapsibleBlockState state = _activeBlock;
            if (state == null)
                return;

            state.Active = false;
            state.DurationSeconds = _restoring ? state.Record.DurationSeconds
                : Math.Max(0, (int)Math.Round((DateTime.UtcNow - state.StartedUtc).TotalSeconds));
            state.Record.DurationSeconds = state.DurationSeconds;
            _activeBlock = null;
            StopEllipsisTimer(state);
            state.HeaderLabel.Text = BuildLabelText(state);

            if (state.BodyText.Text != null)
                state.BodyText.Text = state.BodyText.Text.TrimEnd('\r', '\n');
        }

        private void OnBlockExpanded(object sender, RoutedEventArgs e)
        {
            Expander expander = sender as Expander;
            CollapsibleBlockState state = expander != null ? expander.Tag as CollapsibleBlockState : null;
            if (state == null)
                return;

            state.Record.Expanded = true;
            StopEllipsisTimer(state);
            state.HeaderLabel.Text = BuildLabelText(state);
        }

        private void OnBlockCollapsed(object sender, RoutedEventArgs e)
        {
            Expander expander = sender as Expander;
            CollapsibleBlockState state = expander != null ? expander.Tag as CollapsibleBlockState : null;
            if (state == null)
                return;

            state.Record.Expanded = false;
            if (_viewActive && state.Active)
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

            if (state.Kind == CollapsibleBlockKind.ToolOutput)
            {
                if (state.Collapsed && state.Active)
                    return "tool output" + new string('.', state.EllipsisCount);
                return "tool output";
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
            return openTag.Equals("[tool call]", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsToolOutputOpenTag(string openTag)
        {
            return openTag.Equals("[tool output]", StringComparison.OrdinalIgnoreCase);
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
