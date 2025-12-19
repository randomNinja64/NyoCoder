using System;
using System.Text;
using EnvDTE;
using EnvDTE80;

namespace NyoCoder
{
    /// <summary>
    /// Service responsible for building context information for AI prompts.
    /// Gathers information about the current editor state, cursor position, selected text, etc.
    /// </summary>
    public class ContextEngine
    {
        private readonly DTE2 _dte;

        /// <summary>
        /// Initializes a new instance of the ContextEngine class.
        /// </summary>
        /// <param name="dte">The DTE2 application object for Visual Studio integration.</param>
        public ContextEngine(DTE2 dte)
        {
            _dte = dte;
        }

        /// <summary>
        /// Checks if the current active window is a text editor.
        /// </summary>
        public bool IsInTextEditor()
        {
            try
            {
                if (_dte == null) return false;

                Window activeWindow = _dte.ActiveWindow;
                return _dte.ActiveDocument != null &&
                       activeWindow != null &&
                       activeWindow.Type == vsWindowType.vsWindowTypeDocument &&
                       !activeWindow.Caption.Contains("[Design]") &&
                       activeWindow.Object is TextWindow;
            }
            catch { return false; }
        }

        /// <summary>
        /// Gets surrounding code lines around the cursor position.
        /// Marks the current line with '>' for visibility.
        /// </summary>
        /// <param name="document">The document to get surrounding code from.</param>
        /// <param name="cursorLine">The line number where the cursor is positioned.</param>
        /// <param name="contextLines">Number of lines to include before and after the cursor.</param>
        /// <returns>A string containing the surrounding code with the cursor line marked.</returns>
        private string GetSurroundingCode(Document document, int cursorLine, int contextLines)
        {
            try
            {
                TextDocument textDoc = document.Object("TextDocument") as TextDocument;
                if (textDoc == null) return string.Empty;

                string fullText = textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);
                string[] lines = fullText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                int startLine = Math.Max(0, cursorLine - 1 - contextLines);
                int endLine = Math.Min(lines.Length, cursorLine + contextLines);

                StringBuilder result = new StringBuilder();
                for (int i = startLine; i < endLine; i++)
                {
                    string marker = (i == cursorLine - 1) ? "> " : "  ";
                    result.AppendLine(marker + lines[i]);
                }

                return result.ToString().TrimEnd();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Builds context information to include with user prompts (e.g., currently open file, cursor position, selected text).
        /// Returns a formatted context section, or an empty string if no context is available.
        /// The format clearly separates context from the user's actual prompt.
        /// </summary>
        /// <returns>A formatted string containing context information, or an empty string if no context is available.</returns>
        public string BuildUserPromptContext()
        {
            StringBuilder context = new StringBuilder();
            bool hasContext = false;

            try
            {
                // List all currently open files in the editor
                if (_dte != null && _dte.Documents != null && _dte.Documents.Count > 0)
                {
                    context.AppendLine("Open files in editor:");
                    foreach (Document doc in _dte.Documents)
                    {
                        try
                        {
                            if (doc != null && !string.IsNullOrWhiteSpace(doc.FullName))
                            {
                                // Mark the active document with an asterisk
                                string marker = (_dte.ActiveDocument != null && 
                                               string.Equals(doc.FullName, _dte.ActiveDocument.FullName, StringComparison.OrdinalIgnoreCase)) 
                                               ? " *" : "";
                                context.AppendLine("  - " + doc.FullName + marker);
                                hasContext = true;
                            }
                        }
                        catch { }
                    }
                    context.AppendLine();
                }

                if (_dte != null && _dte.ActiveDocument != null && !string.IsNullOrWhiteSpace(_dte.ActiveDocument.FullName))
                {
                    string currentFilePath = _dte.ActiveDocument.FullName;
                    context.AppendLine("Active file: " + currentFilePath);
                    hasContext = true;

                    // Try to get cursor position and selected text
                    try
                    {
                        TextSelection selection = _dte.ActiveDocument.Selection as TextSelection;
                        if (selection != null)
                        {
                            // Get cursor position (line and column)
                            int currentLine = selection.ActivePoint.Line;
                            int currentColumn = selection.ActivePoint.DisplayColumn;
                            context.AppendLine("Cursor position: Line " + currentLine + ", Column " + currentColumn);

                            // Get surrounding code context (5 lines before/after cursor)
                            string surroundingCode = GetSurroundingCode(_dte.ActiveDocument, currentLine, 5);
                            if (!string.IsNullOrEmpty(surroundingCode))
                            {
                                context.AppendLine("Surrounding code:");
                                context.AppendLine("```");
                                context.AppendLine(surroundingCode);
                                context.AppendLine("```");
                            }

                            // Get selected/highlighted text if any
                            string selectedText = selection.Text;
                            if (!string.IsNullOrEmpty(selectedText))
                            {
                                context.AppendLine("Selected text:");
                                context.AppendLine("```");
                                context.AppendLine(selectedText);
                                context.AppendLine("```");
                            }
                        }
                    }
                    catch
                    {
                        // If we can't get selection info, continue without it
                    }
                }
            }
            catch
            {
                // If we can't get context information, continue without it
            }

            // Only return formatted context if we have any context information
            if (hasContext)
            {
                return "---Context---\n" + context.ToString().TrimEnd() + "\n---End Context---";
            }

            return string.Empty;
        }
    }
}
