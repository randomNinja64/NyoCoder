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

        // VB.NET declaration modifiers, as the VB editor capitalizes them.
        private const string VbModifiers =
            @"^\s*(?:(?:Public|Private|Protected|Friend|Partial|Shared|Shadows|MustInherit|NotInheritable|MustOverride|Overridable|Overrides|NotOverridable|Overloads|ReadOnly|WriteOnly|Default)\s+)*";

        // Applied per line, in priority order; the first matching rule wins for that line.
        private static readonly Rule[] Rules = new[]
        {
            new Rule(@"\bnamespace\s+([A-Za-z_][A-Za-z0-9_\.]*)", "namespace", RegexOptions.None),
            new Rule(@"\b(?:module|package|trait)\s+([A-Za-z_][A-Za-z0-9_\.]*)", "module", RegexOptions.None),
            // class / struct / enum, but not template parameters (template <class T>) or
            // forward declarations and variables (class Foo; / struct stat st;).
            new Rule(@"(?<![<,]\s*)\bclass\s+([A-Za-z_][A-Za-z0-9_]*)\b(?![^{]*;\s*$)", "class", RegexOptions.None),
            new Rule(@"\binterface\s+([A-Za-z_][A-Za-z0-9_]*)", "interface", RegexOptions.None),
            new Rule(@"\bstruct\s+([A-Za-z_][A-Za-z0-9_]*)\b(?![^{]*;\s*$)", "struct", RegexOptions.None),
            new Rule(@"\benum\s+([A-Za-z_][A-Za-z0-9_]*)\b(?![^{]*;\s*$)", "enum", RegexOptions.None),
            new Rule(@"\brecord\s+([A-Za-z_][A-Za-z0-9_]*)", "record", RegexOptions.None),
            // VB.NET type and property declarations (capitalized keywords)
            new Rule(VbModifiers + @"Class\s+([A-Za-z_][A-Za-z0-9_]*)", "class", RegexOptions.None),
            new Rule(VbModifiers + @"Module\s+([A-Za-z_][A-Za-z0-9_]*)", "module", RegexOptions.None),
            new Rule(VbModifiers + @"Structure\s+([A-Za-z_][A-Za-z0-9_]*)", "struct", RegexOptions.None),
            new Rule(VbModifiers + @"Interface\s+([A-Za-z_][A-Za-z0-9_]*)", "interface", RegexOptions.None),
            new Rule(VbModifiers + @"Enum\s+([A-Za-z_][A-Za-z0-9_]*)", "enum", RegexOptions.None),
            new Rule(VbModifiers + @"Property\s+([A-Za-z_][A-Za-z0-9_]*)", "property", RegexOptions.None),
            // Go / TypeScript / Rust / Python type declarations: type Name struct|interface / type Name[<T>] =
            new Rule(@"^\s*(?:export\s+)?(?:declare\s+)?type\s+([A-Za-z_$][A-Za-z0-9_$]*)(?:\s*<[^=]*>)?\s*(?:=|struct\b|interface\b)", "type", RegexOptions.None),
            // Python / Ruby definitions
            new Rule(@"\bdef\s+([A-Za-z_][A-Za-z0-9_]*)", "function", RegexOptions.None),
            // JavaScript / TypeScript function declarations, including generators (function* name)
            new Rule(@"\bfunction(?:\s*\*\s*|\s+)([A-Za-z_$][A-Za-z0-9_$]*)", "function", RegexOptions.None),
            // Go functions (optionally with a receiver)
            new Rule(@"\bfunc\s+(?:\([^)]*\)\s*)?([A-Za-z_][A-Za-z0-9_]*)", "function", RegexOptions.None),
            // VB.NET Sub / Function
            new Rule(@"\b(?:Sub|Function)\s+([A-Za-z_][A-Za-z0-9_]*)", "function", RegexOptions.IgnoreCase),
            // JS/TS arrow-function or function-expression assignments (optional TS return type)
            new Rule(@"\b(?:const|let|var)\s+([A-Za-z_$][A-Za-z0-9_$]*)\s*=\s*(?:async\s*)?(?:function\b|\([^)]*\)(?:\s*:\s*[^=]+?)?\s*=>|[A-Za-z_$][A-Za-z0-9_$]*\s*=>)", "function", RegexOptions.None),
            // JS/TS class fields and object-literal members holding functions:
            // handleClick = () => {  /  onSave: function (e) {
            new Rule(@"^\s*(?:(?:public|private|protected|static|readonly|async)\s+)*([A-Za-z_$][A-Za-z0-9_$]*)\s*[:=]\s*(?:async\s*)?(?:function\b|\([^)]*\)(?:\s*:\s*[^=]+?)?\s*=>|[A-Za-z_$][A-Za-z0-9_$]*\s*=>)", "function", RegexOptions.None),
            // Rust functions: [pub[(crate)]] [const|async|unsafe|extern "C"]* fn name
            new Rule(@"^\s*(?:pub(?:\([^)]*\))?\s+)?(?:(?:const|async|unsafe|extern(?:\s+""[^""]*"")?)\s+)*fn\s+([A-Za-z_][A-Za-z0-9_]*)", "function", RegexOptions.None),
            // Rust impl blocks: impl[<..>] Type / impl[<..>] Trait for Type -> the implementing type
            new Rule(@"^\s*(?:unsafe\s+)?impl\b(?:\s*<[^{]*?>)?\s+(?:[A-Za-z_][A-Za-z0-9_:]*(?:<[^{]*?>)?\s+for\s+)?([A-Za-z_][A-Za-z0-9_]*)", "impl", RegexOptions.None),
            // Kotlin functions, including generic and extension functions (fun <T> Recv.name()
            new Rule(@"^\s*(?:(?:public|private|protected|internal|open|override|abstract|final|inline|suspend|operator|infix|tailrec|external|actual|expect)\s+)*fun\s+(?:<[^>]*>\s*)?(?:[A-Za-z_][A-Za-z0-9_\.]*(?:<[^>]*>)?\.)?([A-Za-z_][A-Za-z0-9_]*)\s*\(", "function", RegexOptions.None),
            // C++ out-of-line definitions at column 0: [ret-type] [ns::]Class::method( with no ';'
            // (a call statement ends in ';').
            new Rule(@"^(?!(?:return|else|case|throw|delete|new|co_return|using)\b)(?:[A-Za-z_][A-Za-z0-9_:<>,\*&\s]*[\s\*&])?(?:[A-Za-z_][A-Za-z0-9_]*(?:<[^>;]*>)?::)+(~?[A-Za-z_][A-Za-z0-9_]*)\s*\([^;]*$", "method", RegexOptions.None),
            // C#/Java/C++/TS style methods: require at least one modifier keyword to reduce
            // false positives from ordinary call expressions. The return type is optional
            // (constructors, TS methods), and generic parameters may follow the modifiers
            // (Java <T>) or the name (C# Get<T>). A line starting "new Foo(" / "new Foo<..>(" /
            // "new int[" / "new Foo {" is an expression, not a method.
            new Rule(@"^(?!\s*new\s+[A-Za-z0-9_\.]+\s*[\(<\[\{])\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|virtual|override|abstract|sealed|async|new|extern|unsafe|partial|final|synchronized|native|readonly)\s+)+(?:<[^>]*>\s*)?(?:[A-Za-z_][A-Za-z0-9_<>\[\],\.\?\s]*\s+)?(~?[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>()]*>)?\s*\(", "method", RegexOptions.None),
            // C# properties: modifiers Type Name { ... } / => expr / brace on the next line.
            // Type is one token (spaces only inside <..>) so prose starting "public" doesn't match.
            new Rule(@"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|virtual|override|abstract|sealed|new|extern|unsafe|readonly)\s+)+[A-Za-z_][A-Za-z0-9_\.]*(?:<[^()=;]*>)?(?:\[[,\s]*\])*\??\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|=>|$)", "property", RegexOptions.None)
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
