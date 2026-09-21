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
    public partial class ChatTurn
    {
        /// <summary>
        /// Classic +/- expander style provided by <c>NyoCoderControl</c>.
        /// </summary>
        public static Style ThinkingExpanderStyle { get; set; }


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
    }
}
