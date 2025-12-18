using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using EnvDTE;
using EnvDTE80;
using NyoCoder;

namespace NyoCoder
{
    internal static class SearchReplaceTool
    {
        private static readonly Regex BlockWithFenceRegex = new Regex(
            @"```[\s\S]*?\n<{5,} SEARCH\r?\n(.*?)\r?\n?={5,}\r?\n(.*?)\r?\n?>{5,} REPLACE\s*\n```",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex BlockRegex = new Regex(
            @"<{5,} SEARCH\r?\n(.*?)\r?\n?={5,}\r?\n(.*?)\r?\n?>{5,} REPLACE",
            RegexOptions.Singleline | RegexOptions.Compiled);

        internal struct Block
        {
            public string Search;
            public string Replace;
        }

        internal enum ChangeType
        {
            Addition,
            Deletion,
            Modification
        }

        internal struct InlineSpan
        {
            public int Start;
            public int Length;
            public ChangeType Type;
        }

        internal sealed class InlinePreview
        {
            public string Content;
            public List<InlineSpan> Spans;

            public InlinePreview()
            {
                Content = string.Empty;
                Spans = new List<InlineSpan>();
            }
        }

        internal sealed class Change
        {
            // Start index in FINAL content (after all blocks applied)
            public int StartIndex;
            // Start index in ORIGINAL content (for pre-apply preview adornments)
            public int OriginalIndex;
            public int OldLength;
            public int NewLength;
            public string OldText;
            public string NewText;
            public ChangeType Type;
        }

        internal sealed class ApplyResult
        {
            public string OriginalContent;
            public string NewContent;
            public List<Block> Blocks;
            public List<Change> Changes;
            public List<string> Errors;
            public List<string> Warnings;
            public string PreviewDiff;
            public string NormalizedFilePath;

            public ApplyResult()
            {
                Blocks = new List<Block>();
                Changes = new List<Change>();
                Errors = new List<string>();
                Warnings = new List<string>();
                PreviewDiff = string.Empty;
                NormalizedFilePath = string.Empty;
            }
        }

        internal static ApplyResult Preview(string filePath, string content)
        {
            ApplyResult res = new ApplyResult();

            string expandedPath = NormalizePath(filePath);
            if (string.IsNullOrEmpty(expandedPath))
            {
                res.Errors.Add("File path cannot be empty");
                return res;
            }

            if (!File.Exists(expandedPath))
            {
                res.Errors.Add("File does not exist: " + expandedPath);
                return res;
            }

            if (string.IsNullOrEmpty(content))
            {
                res.Errors.Add("Empty content provided");
                return res;
            }

            res.NormalizedFilePath = expandedPath;

            res.Blocks = ParseBlocks(content);
            if (res.Blocks.Count == 0)
            {
                res.Errors.Add(
                    "No valid SEARCH/REPLACE blocks found in content.\n" +
                    "Expected format:\n" +
                    "<<<<<<< SEARCH\n" +
                    "[exact content to find]\n" +
                    "=======\n" +
                    "[exact text to replace it with]\n" +
                    ">>>>>>> REPLACE");
                return res;
            }

            // Open the file in the editor first so the user sees the changes
            FileHandler.TryOpenFileInVisualStudio(expandedPath);

            // Prefer editing an open document buffer (so VS updates live)
            string original = null;
            bool editedOpenDocument = false;

            TryReadFromOpenDocument(expandedPath, out original, out editedOpenDocument);

            if (original == null)
            {
                original = File.ReadAllText(expandedPath, Encoding.UTF8);
            }

            // Normalize line endings for consistent matching
            original = NormalizeLineEndings(original);

            res.OriginalContent = original;

            ApplyBlocksInMemory(res);

            // Build a preview diff even if we fail (helps debugging)
            res.PreviewDiff = BuildUnifiedDiff(res.OriginalContent ?? string.Empty, res.NewContent ?? string.Empty, 200);

            // Scroll to the first change so the user can see where the diff is
            if (res.Changes.Count > 0)
            {
                int firstChangeOffset = res.Changes[0].OriginalIndex;
                FileHandler.TryScrollToOffset(expandedPath, res.OriginalContent, firstChangeOffset);
            }

            if (res.Errors.Count > 0)
            {
                return res;
            }

            return res;
        }

        internal static InlinePreview BuildInlinePreview(ApplyResult res)
        {
            InlinePreview preview = new InlinePreview();

            if (res == null || res.Errors.Count > 0)
            {
                preview.Content = res != null ? (res.OriginalContent ?? string.Empty) : string.Empty;
                return preview;
            }

            string current = res.OriginalContent ?? string.Empty;
            List<InlineSpan> spans = new List<InlineSpan>();

            // Build inline preview by inserting trimmed replacement text immediately AFTER the original text,
            // leaving the original text in place. This makes the diff visible inline before applying.
            //
            // Process from the end so indices remain stable.
            List<Change> changes = new List<Change>(res.Changes);
            changes.Sort((a, b) => b.OriginalIndex.CompareTo(a.OriginalIndex));

            for (int i = 0; i < changes.Count; i++)
            {
                Change c = changes[i];

                int start = c.OriginalIndex;
                if (start < 0) start = 0;
                if (start > current.Length) start = current.Length;

                int oldLen = Math.Max(0, c.OldLength);
                if (start + oldLen > current.Length) oldLen = current.Length - start;

                string newPreviewText = TrimLeadingWhitespaceFirstLine(c.NewText ?? string.Empty);
                int insertPos = start + oldLen;

                // Insert new preview text right after old text
                if (newPreviewText.Length > 0)
                {
                    current = current.Substring(0, insertPos) + newPreviewText + current.Substring(insertPos);
                    spans.Add(new InlineSpan { Start = insertPos, Length = newPreviewText.Length, Type = ChangeType.Addition });
                }

                // Highlight old text in-place as deletion
                if (oldLen > 0)
                {
                    spans.Add(new InlineSpan { Start = start, Length = oldLen, Type = ChangeType.Deletion });
                }
            }

            preview.Content = current;
            preview.Spans = spans;
            return preview;
        }

        private static string TrimLeadingWhitespaceFirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (ch == ' ' || ch == '\t')
                {
                    i++;
                    continue;
                }
                break;
            }
            return i > 0 ? text.Substring(i) : text;
        }

        internal static bool ApplyPreview(ApplyResult res)
        {
            if (res == null) return false;
            if (res.Errors != null && res.Errors.Count > 0) return false;
            if (string.IsNullOrEmpty(res.NormalizedFilePath)) return false;
            if (string.Equals(res.NewContent, res.OriginalContent, StringComparison.Ordinal)) return true;

            // Apply to open document if it is open; otherwise write to disk
            bool appliedToOpen = TryApplyToOpenDocument(res.NormalizedFilePath, res.NewContent);
            if (!appliedToOpen)
            {
                File.WriteAllText(res.NormalizedFilePath, res.NewContent, Encoding.UTF8);
            }
            return true;
        }

        internal static bool TrySetOpenDocumentContent(string fullPath, string newContent, bool save)
        {
            try
            {
                Action apply = () =>
                {
                    DTE2 dte = FileHandler.GetDte();
                    if (dte == null) return;

                    Document doc = FileHandler.FindOpenDocument(dte, fullPath);
                    if (doc == null) return;

                    TextDocument textDoc = doc.Object("TextDocument") as TextDocument;
                    if (textDoc == null) return;

                    EditPoint start = textDoc.StartPoint.CreateEditPoint();
                    start.ReplaceText(textDoc.EndPoint, newContent ?? string.Empty, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);

                    if (save)
                    {
                        try { doc.Save(); } catch { }
                    }
                };

                if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(apply);
                }
                else
                {
                    apply();
                }

                DTE2 check = FileHandler.GetDte();
                if (check == null) return false;
                return FileHandler.FindOpenDocument(check, fullPath) != null;
            }
            catch
            {
                return false;
            }
        }

        // Backwards-compatible helper: preview + apply (no approval)
        internal static ApplyResult Run(string filePath, string content)
        {
            ApplyResult res = Preview(filePath, content);
            if (res.Errors.Count == 0 && !string.Equals(res.NewContent, res.OriginalContent, StringComparison.Ordinal))
            {
                ApplyPreview(res);
            }
            return res;
        }

        private static void ApplyBlocksInMemory(ApplyResult res)
        {
            string current = res.OriginalContent ?? string.Empty;
            string originalBase = current;
            List<Change> recorded = new List<Change>();
            int cumulativeDeltaForMonotonicMap = 0;
            int lastFoundIndex = -1;

            for (int i = 0; i < res.Blocks.Count; i++)
            {
                Block block = res.Blocks[i];
                int blockNum = i + 1;

                int occurrences = CountOccurrences(current, block.Search);
                if (occurrences == 0)
                {
                    res.Errors.Add(BuildNotFoundError(current, block.Search, blockNum));
                    continue;
                }

                // Enforce the "exactly once" rule (this also makes highlighting reliable)
                if (occurrences != 1)
                {
                    res.Errors.Add(
                        "SEARCH/REPLACE block " + blockNum + " failed: SEARCH text appears " + occurrences + " times.\n" +
                        "Your SEARCH text must match EXACTLY once. Make it more specific.");
                    continue;
                }

                int index = current.IndexOf(block.Search, StringComparison.Ordinal);

                ChangeType type = DetermineType(block.Search, block.Replace);

                int originalIndex = FindUniqueIndex(originalBase, block.Search);
                if (originalIndex < 0)
                {
                    // Fallback: approximate mapping for content that only exists after earlier replacements.
                    originalIndex = index - cumulativeDeltaForMonotonicMap;
                    if (originalIndex < 0) originalIndex = 0;
                    if (originalIndex > originalBase.Length) originalIndex = originalBase.Length;
                }

                Change change = new Change
                {
                    // We'll adjust StartIndex for later edits using delta shifting
                    StartIndex = index,
                    OriginalIndex = originalIndex,
                    OldLength = block.Search.Length,
                    NewLength = block.Replace.Length,
                    OldText = block.Search,
                    NewText = block.Replace,
                    Type = type
                };

                // Replace in current content
                current = current.Substring(0, index) + block.Replace + current.Substring(index + block.Search.Length);

                // Shift previously recorded changes if this replacement happened before them
                int delta = block.Replace.Length - block.Search.Length;
                if (delta != 0)
                {
                    for (int r = 0; r < recorded.Count; r++)
                    {
                        if (recorded[r].StartIndex > index)
                        {
                            recorded[r].StartIndex += delta;
                        }
                    }
                }

                recorded.Add(change);

                // Track monotonic delta mapping best-effort (helps later OriginalIndex approximations)
                if (index >= lastFoundIndex)
                {
                    cumulativeDeltaForMonotonicMap += delta;
                    lastFoundIndex = index;
                }
            }

            res.NewContent = current;
            res.Changes = recorded;
        }

        private static int FindUniqueIndex(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return -1;

            int first = haystack.IndexOf(needle, StringComparison.Ordinal);
            if (first < 0) return -1;

            int second = haystack.IndexOf(needle, first + needle.Length, StringComparison.Ordinal);
            if (second >= 0) return -1; // not unique

            return first;
        }

        private static ChangeType DetermineType(string oldText, string newText)
        {
            if (string.IsNullOrEmpty(oldText) && !string.IsNullOrEmpty(newText)) return ChangeType.Addition;
            if (!string.IsNullOrEmpty(oldText) && string.IsNullOrEmpty(newText)) return ChangeType.Deletion;
            return ChangeType.Modification;
        }

        internal static List<Block> ParseBlocks(string content)
        {
            List<Block> blocks = new List<Block>();

            MatchCollection fenceMatches = BlockWithFenceRegex.Matches(content);
            if (fenceMatches.Count > 0)
            {
                foreach (Match match in fenceMatches)
                {
                    blocks.Add(new Block
                    {
                        Search = NormalizeLineEndings((match.Groups[1].Value ?? string.Empty).TrimEnd('\r', '\n')),
                        Replace = NormalizeLineEndings((match.Groups[2].Value ?? string.Empty).TrimEnd('\r', '\n'))
                    });
                }
                return blocks;
            }

            MatchCollection matches = BlockRegex.Matches(content);
            foreach (Match match in matches)
            {
                blocks.Add(new Block
                {
                    Search = NormalizeLineEndings((match.Groups[1].Value ?? string.Empty).TrimEnd('\r', '\n')),
                    Replace = NormalizeLineEndings((match.Groups[2].Value ?? string.Empty).TrimEnd('\r', '\n'))
                });
            }

            return blocks;
        }

        private static string NormalizeLineEndings(string text)
        {
            if (text == null) return null;
            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static string NormalizePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;

            string expandedPath = Environment.ExpandEnvironmentVariables(filePath.Trim());
            if (!Path.IsPathRooted(expandedPath))
            {
                expandedPath = Path.Combine(Environment.CurrentDirectory, expandedPath);
            }

            try
            {
                return Path.GetFullPath(expandedPath);
            }
            catch
            {
                return expandedPath;
            }
        }

        private static int CountOccurrences(string text, string search)
        {
            if (string.IsNullOrEmpty(search)) return 0;

            int count = 0;
            int index = 0;
            while (true)
            {
                index = text.IndexOf(search, index, StringComparison.Ordinal);
                if (index < 0) break;
                count++;
                index += search.Length;
            }
            return count;
        }

        private static string BuildNotFoundError(string currentContent, string searchText, int blockNum)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("SEARCH/REPLACE block " + blockNum + " failed: Search text not found.");
            sb.AppendLine("Search text was:");
            sb.AppendLine(searchText);
            sb.AppendLine();
            sb.AppendLine("Tip: SEARCH must match EXACTLY including whitespace + line endings.");
            return sb.ToString();
        }

        private static void TryReadFromOpenDocument(string expandedPath, out string content, out bool isOpen)
        {
            content = null;
            isOpen = false;

            try
            {
                string localContent = null;
                bool localIsOpen = false;

                Action read = () =>
                {
                    DTE2 dte = FileHandler.GetDte();
                    if (dte == null) return;

                    Document doc = FileHandler.FindOpenDocument(dte, expandedPath);
                    if (doc == null) return;

                    TextDocument textDoc = doc.Object("TextDocument") as TextDocument;
                    if (textDoc == null) return;

                    localIsOpen = true;

                    EditPoint start = textDoc.StartPoint.CreateEditPoint();
                    localContent = NormalizeLineEndings(start.GetText(textDoc.EndPoint));
                };

                // DTE automation should run on the UI thread
                if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(read);
                }
                else
                {
                    read();
                }

                content = localContent;
                isOpen = localIsOpen;
            }
            catch
            {
                content = null;
                isOpen = false;
            }
        }

        private static bool TryApplyToOpenDocument(string expandedPath, string newContent)
        {
            try
            {
                Action apply = () =>
                {
                    DTE2 dte = FileHandler.GetDte();
                    if (dte == null) return;

                    Document doc = FileHandler.FindOpenDocument(dte, expandedPath);
                    if (doc == null) return;

                    TextDocument textDoc = doc.Object("TextDocument") as TextDocument;
                    if (textDoc == null) return;

                    EditPoint start = textDoc.StartPoint.CreateEditPoint();
                    start.ReplaceText(textDoc.EndPoint, newContent, (int)vsEPReplaceTextOptions.vsEPReplaceTextKeepMarkers);

                    try
                    {
                        if (!doc.Saved)
                        {
                            doc.Save();
                        }
                    }
                    catch { }
                };

                // DTE automation should run on the UI thread
                if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(apply);
                }
                else
                {
                    apply();
                }

                // If we got here without throwing and the doc was open, we consider it applied
                DTE2 check = FileHandler.GetDte();
                if (check == null) return false;
                return FileHandler.FindOpenDocument(check, expandedPath) != null;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildUnifiedDiff(string oldText, string newText, int maxLines)
        {
            // Simple line-based diff (good enough for a preview)
            string[] a = (oldText ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            string[] b = (newText ?? string.Empty).Replace("\r\n", "\n").Split('\n');

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("--- original");
            sb.AppendLine("+++ new");

            int i = 0;
            int j = 0;
            int linesOut = 0;

            while (i < a.Length || j < b.Length)
            {
                if (linesOut >= maxLines)
                {
                    sb.AppendLine("...(diff truncated)");
                    break;
                }

                string la = i < a.Length ? a[i] : null;
                string lb = j < b.Length ? b[j] : null;

                if (la == lb)
                {
                    if (la != null)
                    {
                        sb.AppendLine(" " + la);
                        linesOut++;
                    }
                    i++;
                    j++;
                    continue;
                }

                if (la != null)
                {
                    sb.AppendLine("-" + la);
                    linesOut++;
                    i++;
                }

                if (lb != null)
                {
                    sb.AppendLine("+" + lb);
                    linesOut++;
                    j++;
                }
            }

            return sb.ToString();
        }
    }
}
