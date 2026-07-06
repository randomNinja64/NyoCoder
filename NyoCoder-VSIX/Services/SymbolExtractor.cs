using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NyoCoder
{
    /// <summary>
    /// A code symbol (definition) discovered in a source file.
    /// </summary>
    public class SymbolEntry
    {
        public string Name;
        public string Kind;
        public string File;
        public int Line;
        public string Signature;
        public List<SymbolCaller> Callers;

        public SymbolEntry() { }

        public SymbolEntry(string name, string kind, string file, int line, string signature)
        {
            Name = name;
            Kind = kind;
            File = file;
            Line = line;
            Signature = signature;
        }

        /// <summary>Dictionary key for this definition site (one symbol per line in our index).</summary>
        public static string DefSiteKey(string file, int line)
        {
            return (file ?? string.Empty) + "\x1e" + line.ToString();
        }

        public string DefSiteKey()
        {
            return DefSiteKey(File, Line);
        }
    }

    /// <summary>A caller site that references a symbol definition.</summary>
    public class SymbolCaller
    {
        public string File;
        public int Line;

        public SymbolCaller() { }

        public SymbolCaller(string file, int line)
        {
            File = file;
            Line = line;
        }
    }

    /// <summary>
    /// Language-agnostic, regex-based extraction of definitions (classes, interfaces,
    /// structs, enums, functions/methods) from source text. Deliberately avoids EnvDTE /
    /// COM so it is fast and works on any file type. Heuristic by design: favors recall for
    /// common languages (C#, VB, C/C++, Java, JS/TS, Python, Go, Ruby, Rust) over precision.
    /// </summary>
    internal static class SymbolExtractor
    {
        private const int MaxSignatureLength = 200;

        private struct Rule
        {
            public Regex Pattern;
            public string Kind;
            public Rule(string pattern, string kind, RegexOptions options)
            {
                Pattern = new Regex(pattern, options | RegexOptions.Compiled);
                Kind = kind;
            }
        }

        // Applied per line, in priority order; the first matching rule wins for that line.
        private static readonly Rule[] Rules = new[]
        {
            new Rule(@"\bnamespace\s+([A-Za-z_][A-Za-z0-9_\.]*)", "namespace", RegexOptions.None),
            new Rule(@"\b(?:module|package|trait)\s+([A-Za-z_][A-Za-z0-9_\.]*)", "module", RegexOptions.None),
            new Rule(@"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)", "class", RegexOptions.None),
            new Rule(@"\binterface\s+([A-Za-z_][A-Za-z0-9_]*)", "interface", RegexOptions.None),
            new Rule(@"\bstruct\s+([A-Za-z_][A-Za-z0-9_]*)", "struct", RegexOptions.None),
            new Rule(@"\benum\s+([A-Za-z_][A-Za-z0-9_]*)", "enum", RegexOptions.None),
            new Rule(@"\brecord\s+([A-Za-z_][A-Za-z0-9_]*)", "record", RegexOptions.None),
            // Python / Ruby definitions
            new Rule(@"\bdef\s+([A-Za-z_][A-Za-z0-9_]*)", "function", RegexOptions.None),
            // JavaScript / TypeScript function declarations
            new Rule(@"\bfunction\s+([A-Za-z_$][A-Za-z0-9_$]*)", "function", RegexOptions.None),
            // Go functions (optionally with a receiver)
            new Rule(@"\bfunc\s+(?:\([^)]*\)\s*)?([A-Za-z_][A-Za-z0-9_]*)", "function", RegexOptions.None),
            // VB.NET Sub / Function
            new Rule(@"\b(?:Sub|Function)\s+([A-Za-z_][A-Za-z0-9_]*)", "function", RegexOptions.IgnoreCase),
            // JS/TS arrow-function or function-expression assignments
            new Rule(@"\b(?:const|let|var)\s+([A-Za-z_$][A-Za-z0-9_$]*)\s*=\s*(?:async\s*)?(?:function\b|\([^)]*\)\s*=>|[A-Za-z_$][A-Za-z0-9_$]*\s*=>)", "function", RegexOptions.None),
            // C#/Java/C++ style methods: require at least one modifier keyword to reduce
            // false positives from ordinary call expressions.
            new Rule(@"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|virtual|override|abstract|sealed|async|new|extern|unsafe|partial|final|synchronized|native)\s+)+[A-Za-z_][A-Za-z0-9_<>\[\],\.\?\s]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", "method", RegexOptions.None)
        };

        /// <summary>
        /// Extracts symbols from the given file content. <paramref name="filePath"/> is stored
        /// on each returned entry; it is not read from disk.
        /// </summary>
        public static List<SymbolEntry> Extract(string filePath, string content)
        {
            List<SymbolEntry> symbols = new List<SymbolEntry>();
            if (string.IsNullOrEmpty(content))
                return symbols;

            string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line))
                    continue;

                string trimmed = line.TrimStart();
                if (trimmed.Length == 0 || IsCommentLine(trimmed))
                    continue;

                for (int r = 0; r < Rules.Length; r++)
                {
                    Match match = Rules[r].Pattern.Match(line);
                    if (!match.Success || match.Groups.Count < 2)
                        continue;

                    string name = match.Groups[1].Value;
                    if (string.IsNullOrEmpty(name))
                        continue;

                    symbols.Add(new SymbolEntry(
                        name,
                        Rules[r].Kind,
                        filePath,
                        i + 1,
                        MakeSignature(line)));

                    // First matching rule wins for this line.
                    break;
                }
            }

            return symbols;
        }

        internal static bool IsCommentLine(string trimmed)
        {
            return trimmed.StartsWith("//")
                || trimmed.StartsWith("#")
                || trimmed.StartsWith("*")
                || trimmed.StartsWith("/*")
                || trimmed.StartsWith("'"); // VB comment
        }

        private static string MakeSignature(string line)
        {
            string signature = Regex.Replace(line.Trim(), @"\s+", " ");
            if (signature.Length > MaxSignatureLength)
                signature = signature.Substring(0, MaxSignatureLength);
            return signature;
        }
    }
}
