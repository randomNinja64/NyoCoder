namespace NyoCoder
{
    internal static class TextNormalization
    {
        /// <summary>
        /// Normalizes CRLF and lone CR to LF for consistent text comparison and patching.
        /// </summary>
        internal static string NormalizeLineEndings(string text)
        {
            if (text == null) return null;
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        /// <summary>
        /// string.IsNullOrWhiteSpace, which .NET 3.5 (NyoCoder-Legacy) does not have.
        /// </summary>
        internal static bool IsNullOrWhiteSpace(string text)
        {
            if (text == null) return true;
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsWhiteSpace(text[i])) return false;
            }
            return true;
        }
    }
}
