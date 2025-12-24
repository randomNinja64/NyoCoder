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
        /// The system prompt used for all LLM interactions.
        /// </summary>
        public static readonly string SystemPrompt = 
            "You are operating as and within NyoCoder, a Visual Studio extension that provides an AI coding assistant powered by LLM models. It enables natural language interaction with a local codebase within Visual Studio. Use the available tools when helpful.\n\n" +
            "You can:\n\n" +
            "    Receive user prompts, project context, and files.\n" +
            "    Send responses and emit function calls (e.g., shell commands, code edits).\n" +
            "    Apply patches, run commands, based on user approvals.\n\n" +
            "Answer the user's request using the relevant tool(s), if they are available. Check that all the required parameters for each tool call are provided or can reasonably be inferred from context. IF there are no relevant tools or there are missing values for required parameters, ask the user to supply these values; otherwise proceed with the tool calls. If the user provides a specific value for a parameter (for example provided in quotes), make sure to use that value EXACTLY. DO NOT make up values for or ask about optional parameters. Carefully analyze descriptive terms in the request as they may indicate required parameter values that should be included even if not explicitly quoted.\n\n" +
            "Always try your hardest to use the tools to answer the user's request. If you can't use the tools, explain why and ask the user for more information.\n\n" +
            "Act as an agentic assistant, if a user asks for a long task, break it down and do it step by step.";

        /// <summary>
        /// Calculates the base token overhead for every LLM request (system prompt + tool definitions).
        /// Returns approximate character count that should be added to conversation tokens.
        /// </summary>
        public static int GetBaseCharacterOverhead()
        {
            int overhead = SystemPrompt.Length;
            overhead += ToolDefinitions.GetToolDefinitionsLength();
            return overhead;
        }

        /// <summary>
        /// Calculates approximate token count from character count, including base overhead.
        /// Uses the standard approximation of 3 characters per token.
        /// </summary>
        /// <param name="characterCount">Character count excluding base overhead.</param>
        /// <returns>Approximate token count including base overhead.</returns>
        public static int ApproximateTokens(int characterCount)
        {
            int totalCharacters = characterCount + GetBaseCharacterOverhead();
            return totalCharacters / 3;
        }

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
        /// Gets the current symbol context (namespace, class, method/property) where the cursor is positioned.
        /// </summary>
        /// <param name="selection">The current text selection.</param>
        /// <returns>A string describing the current symbol context, or empty if unavailable.</returns>
        private string GetCurrentSymbolContext(TextSelection selection)
        {
            try
            {
                if (selection == null) return string.Empty;

                // Try to get the code element at the cursor position (try most specific first)
                vsCMElement[] elementTypes = { 
                    vsCMElement.vsCMElementFunction, 
                    vsCMElement.vsCMElementProperty,
                    vsCMElement.vsCMElementClass,
                    vsCMElement.vsCMElementInterface,
                    vsCMElement.vsCMElementStruct
                };

                CodeElement codeElement = null;
                foreach (var elementType in elementTypes)
                {
                    try
                    {
                        codeElement = selection.ActivePoint.CodeElement[elementType];
                        if (codeElement != null) break;
                    }
                    catch { }
                }

                if (codeElement == null) return string.Empty;

                // Build the symbol path by collecting names from the hierarchy
                StringBuilder symbolPath = new StringBuilder();
                string namespaceName = null;
                string typeName = null;
                string memberName = null;

                CodeElement current = codeElement;
                while (current != null)
                {
                    try
                    {
                        switch (current.Kind)
                        {
                            case vsCMElement.vsCMElementNamespace:
                                namespaceName = current.Name;
                                break;
                            case vsCMElement.vsCMElementClass:
                            case vsCMElement.vsCMElementInterface:
                            case vsCMElement.vsCMElementStruct:
                                typeName = current.Name;
                                break;
                            case vsCMElement.vsCMElementFunction:
                            case vsCMElement.vsCMElementProperty:
                                memberName = current.Name;
                                break;
                        }

                        if (current.Collection == null) break;
                        var parent = current.Collection.Parent as CodeElement;
                        if (parent == null || parent == current) break;
                        current = parent;
                    }
                    catch { break; }
                }

                // Build the final path string
                if (!string.IsNullOrEmpty(namespaceName))
                    symbolPath.Append(namespaceName);

                if (!string.IsNullOrEmpty(typeName))
                {
                    if (symbolPath.Length > 0) symbolPath.Append(".");
                    symbolPath.Append(typeName);
                }

                if (!string.IsNullOrEmpty(memberName))
                {
                    if (symbolPath.Length > 0) symbolPath.Append(".");
                    symbolPath.Append(memberName).Append("()");
                }

                return symbolPath.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets information about the current solution and project.
        /// </summary>
        /// <returns>A string containing solution and project information, or empty if unavailable.</returns>
        private string GetProjectSolutionInfo()
        {
            try
            {
                if (_dte == null || _dte.Solution == null) return string.Empty;

                StringBuilder info = new StringBuilder();

                // Get solution name
                if (!string.IsNullOrEmpty(_dte.Solution.FullName))
                {
                    string solutionName = System.IO.Path.GetFileNameWithoutExtension(_dte.Solution.FullName);
                    info.AppendLine("Solution: " + solutionName);
                }

                // Get active project information
                if (_dte.ActiveDocument != null && !string.IsNullOrEmpty(_dte.ActiveDocument.FullName))
                {
                    try
                    {
                        // Try to find the project containing the active document
                        EnvDTE.Project activeProject = _dte.ActiveDocument.ProjectItem != null 
                            ? _dte.ActiveDocument.ProjectItem.ContainingProject 
                            : null;

                        if (activeProject != null)
                        {
                            info.AppendLine("Project: " + activeProject.Name);

                            // Try to get project type/kind
                            try
                            {
                                string projectType = GetProjectTypeDescription(activeProject.Kind);
                                if (!string.IsNullOrEmpty(projectType))
                                {
                                    info.AppendLine("Project type: " + projectType);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                return info.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Converts a project kind GUID to a human-readable description.
        /// </summary>
        private string GetProjectTypeDescription(string projectKind)
        {
            if (string.IsNullOrEmpty(projectKind)) return string.Empty;

            // Common Visual Studio project type GUIDs
            switch (projectKind.ToUpperInvariant())
            {
                case "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}":
                    return "C# Project";
                case "{F184B08F-C81C-45F6-A57F-5ABD9991F28F}":
                    return "VB.NET Project";
                case "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}":
                    return "C++ Project";
                case "{E24C65DC-7377-472B-9ABA-BC803B73C61A}":
                    return "Web Site Project";
                case "{349C5851-65DF-11DA-9384-00065B846F21}":
                    return "Web Application";
                case "{603C0E0B-DB56-11DC-BE95-000D561079B0}":
                    return "ASP.NET MVC Project";
                case "{F135793A-A601-4B6E-8E10-1F93E0D8F3B3}":
                    return "F# Project";
                case "{82B43B9B-A64C-4715-B499-D71E9CA2BD60}":
                    return "Visual Studio Extension (VSIX)";
                case "{A1591282-1198-4647-A2B1-27E5FF5F6F3B}":
                    return "Silverlight Application";
                case "{786C830F-07A1-408B-BD7F-6EE04809D6DB}":
                    return "Portable Class Library";
                case "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}":
                    return ".NET SDK Project";
                default:
                    return "Other";
            }
        }

        /// <summary>
        /// Gets active compiler errors from the error list.
        /// </summary>
        /// <param name="activeFilePath">Path to the active file to filter errors for that file, or null for all errors.</param>
        /// <returns>A string containing error information, or empty if no errors.</returns>
        private string GetCompilerErrors(string activeFilePath)
        {
            try
            {
                if (_dte == null) return string.Empty;

                // Get the error list items
                ErrorList errorList = _dte.ToolWindows.ErrorList;
                if (errorList == null) return string.Empty;

                ErrorItems errorItems = errorList.ErrorItems;
                if (errorItems == null || errorItems.Count == 0) return string.Empty;

                StringBuilder errors = new StringBuilder();
                int errorCount = 0;

                // Iterate through error items (only errors, not warnings)
                for (int i = 1; i <= errorItems.Count; i++)
                {
                    try
                    {
                        ErrorItem item = errorItems.Item(i);
                        
                        // Only include errors (not warnings or messages)
                        if (item == null || item.ErrorLevel != vsBuildErrorLevel.vsBuildErrorLevelHigh)
                            continue;

                        // If activeFilePath is provided, only include errors from that file
                        if (!string.IsNullOrEmpty(activeFilePath) && 
                            !string.IsNullOrEmpty(item.FileName) &&
                            !string.Equals(item.FileName, activeFilePath, StringComparison.OrdinalIgnoreCase))
                            continue;

                        errorCount++;
                        
                        // Format: Line 45: CS0246: The type or namespace name 'Foo' could not be found
                        string fileName = !string.IsNullOrEmpty(item.FileName) 
                            ? System.IO.Path.GetFileName(item.FileName) 
                            : "Unknown";
                        
                        string errorMsg = "  - ";
                        if (item.Line > 0)
                        {
                            errorMsg += "Line " + item.Line + ": ";
                        }
                        errorMsg += item.Description;
                        
                        // Add file name if showing errors from multiple files
                        if (string.IsNullOrEmpty(activeFilePath) && !string.IsNullOrEmpty(item.FileName))
                        {
                            errorMsg += " (" + fileName + ")";
                        }
                        
                        errors.AppendLine(errorMsg);
                    }
                    catch { }
                }

                if (errorCount == 0) return string.Empty;

                StringBuilder result = new StringBuilder();
                result.AppendLine("Build errors: " + errorCount);
                result.Append(errors.ToString().TrimEnd());
                
                return result.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets surrounding code lines around the cursor position.
        /// </summary>
        /// <param name="document">The document to get surrounding code from.</param>
        /// <param name="cursorLine">The line number where the cursor is positioned.</param>
        /// <param name="contextLines">Number of lines to include before and after the cursor.</param>
        /// <returns>A string containing the surrounding code.</returns>
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
                    result.AppendLine(lines[i]);
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

                // Add project and solution information
                string projectInfo = GetProjectSolutionInfo();
                if (!string.IsNullOrEmpty(projectInfo))
                {
                    context.Append(projectInfo);
                    context.AppendLine();
                }

                if (_dte != null && _dte.ActiveDocument != null && !string.IsNullOrWhiteSpace(_dte.ActiveDocument.FullName))
                {
                    string currentFilePath = _dte.ActiveDocument.FullName;
                    
                    // Add compiler errors for the current file
                    string compilerErrors = GetCompilerErrors(currentFilePath);
                    if (!string.IsNullOrEmpty(compilerErrors))
                    {
                        context.AppendLine(compilerErrors);
                        context.AppendLine();
                        hasContext = true;
                    }
                    
                    context.AppendLine("Active file: " + currentFilePath);
                    hasContext = true;

                    // Get total line count of the file
                    try
                    {
                        TextDocument textDoc = _dte.ActiveDocument.Object("TextDocument") as TextDocument;
                        if (textDoc != null)
                        {
                            string fullText = textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);
                            string[] lines = fullText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                            context.AppendLine("Total lines: " + lines.Length);
                        }
                    }
                    catch { }

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

                            // Get current symbol context (namespace, class, method)
                            string symbolContext = GetCurrentSymbolContext(selection);
                            if (!string.IsNullOrEmpty(symbolContext))
                            {
                                context.AppendLine("Current scope: " + symbolContext);
                            }

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
