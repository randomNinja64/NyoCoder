using System;

namespace NyoCoder
{
    /// <summary>
    /// Wraps the chat output sink and tracks how many consecutive newlines were
    /// emitted last, so StartBlock can pad to exactly one blank line between
    /// output blocks regardless of how the previous block ended.
    /// </summary>
    internal class ChatOutputWriter
    {
        private readonly Action<string> _sink;

        // Start of an empty pane counts as already separated.
        private int _trailingNewlines = 2;

        public ChatOutputWriter(Action<string> sink)
        {
            _sink = sink;
        }

        /// <summary>
        /// Writes text to the sink and updates the trailing-newline count.
        /// All chat output must flow through here for StartBlock to stay accurate.
        /// </summary>
        public void Write(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            _sink(text);

            int newlines = 0;
            int i = text.Length - 1;
            while (i >= 0 && text[i] == '\n')
            {
                newlines++;
                i--;
                if (i >= 0 && text[i] == '\r')
                    i--;
            }

            // If the text was nothing but newlines, they extend the existing run.
            _trailingNewlines = (i < 0) ? _trailingNewlines + newlines : newlines;
        }

        /// <summary>
        /// Ensures the output ends with exactly one blank line before a new block starts.
        /// Emits 0, 1, or 2 newlines depending on what was written last.
        /// </summary>
        public void StartBlock()
        {
            if (_trailingNewlines < 2)
                Write(new string('\n', 2 - _trailingNewlines));
        }

        /// <summary>
        /// Resets tracking after the output pane is cleared.
        /// </summary>
        public void Reset()
        {
            _trailingNewlines = 2;
        }
    }
}
