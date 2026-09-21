using System;

namespace NyoCoder
{
    public partial class ChatTurn
    {
        private static readonly string[] CollapsibleOpenTags =
        {
            "[thinking]", "<think>", "[tool call]", "[tool output]"
        };
        private static readonly string[] CollapsibleCloseTags =
        {
            "[/thinking]", "</think>", "[/tool call]", "[/tool output]"
        };

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
