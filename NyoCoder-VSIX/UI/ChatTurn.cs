using System.Windows;
using System.Windows.Documents;

namespace NyoCoder
{
    /// <summary>
    /// One chat output block (user, assistant, tool, etc.) backed by its own FlowDocument.
    /// </summary>
    public class ChatTurn
    {
        public FlowDocument Document { get; private set; }

        /// <summary>
        /// Block index already processed by MarkdownHandler for this turn's document.
        /// </summary>
        public int MarkdownProcessedBlockCount;

        // False until visible text is appended; used to drop leading padding newlines.
        private bool _hasContent;

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

            new TextRange(Document.ContentEnd, Document.ContentEnd).Text = text;
        }

        /// <summary>
        /// Removes empty paragraphs left at the end of the document by padding newlines.
        /// </summary>
        public void TrimTrailingBlankParagraphs()
        {
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
    }
}
