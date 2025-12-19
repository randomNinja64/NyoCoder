using System;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace NyoCoder
{
    /// <summary>
    /// Provides Visual Studio editor and document integration services.
    /// All methods in this class interact with the VS UI/editor.
    /// </summary>
    public static class EditorService
    {
        /// <summary>
        /// Gets the DTE2 instance from the package.
        /// </summary>
        internal static DTE2 GetDte()
        {
            try
            {
                NyoCoder_VSIXPackage pkg = NyoCoder_VSIXPackage.Instance;
                if (pkg == null) return null;
                return pkg.ApplicationObject;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Finds an open document by full path.
        /// </summary>
        internal static Document FindOpenDocument(DTE2 dte, string fullPath)
        {
            try
            {
                foreach (Document doc in dte.Documents)
                {
                    try
                    {
                        if (doc != null && !string.IsNullOrEmpty(doc.FullName) &&
                            string.Equals(doc.FullName, fullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return doc;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Attempts to open a file in Visual Studio's text editor.
        /// </summary>
        internal static void TryOpenFileInVisualStudio(string filePath)
        {
            try
            {
                Action openFile = () =>
                {
                    try
                    {
                        // Normalize the path to ensure it matches
                        string normalizedPath = System.IO.Path.GetFullPath(filePath);

                        // Always use shell API to open in TextView.
                        // This ensures we get code view even if designer is already open.
                        IVsUIShellOpenDocument openDoc = Package.GetGlobalService(typeof(SVsUIShellOpenDocument)) as IVsUIShellOpenDocument;
                        if (openDoc == null)
                        {
                            // Fallback: best-effort DTE open
                            DTE2 dte = GetDte();
                            if (dte != null)
                            {
                                try { dte.ItemOperations.OpenFile(normalizedPath, EnvDTE.Constants.vsViewKindTextView); } catch { }
                            }
                            return;
                        }

                        Guid viewGuid = VSConstants.LOGVIEWID_TextView;
                        Microsoft.VisualStudio.OLE.Interop.IServiceProvider sp;
                        IVsUIHierarchy hier;
                        uint itemid;
                        IVsWindowFrame frame;

                        int hr = openDoc.OpenDocumentViaProject(
                            normalizedPath,
                            ref viewGuid,
                            out sp,
                            out hier,
                            out itemid,
                            out frame);

                        if (ErrorHandler.Succeeded(hr) && frame != null)
                        {
                            try { frame.Show(); } catch { }
                        }
                    }
                    catch { }
                };

                // DTE/Shell automation should run on the UI thread
                if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(openFile);
                }
                else
                {
                    openFile();
                }
            }
            catch
            {
                // Silently fail if we can't open the file - it's not critical
            }
        }

        /// <summary>
        /// Attempts to scroll to a character offset in an open document.
        /// </summary>
        internal static void TryScrollToOffset(string filePath, string content, int charOffset)
        {
            try
            {
                // Convert character offset to line number
                int lineNumber = 1;
                if (!string.IsNullOrEmpty(content) && charOffset > 0)
                {
                    int pos = 0;
                    for (int i = 0; i < content.Length && pos < charOffset; i++)
                    {
                        if (content[i] == '\n')
                        {
                            lineNumber++;
                        }
                        pos++;
                    }
                }

                Action scroll = () =>
                {
                    DTE2 dte = GetDte();
                    if (dte == null) return;

                    Document doc = FindOpenDocument(dte, filePath);
                    if (doc == null) return;

                    TextDocument textDoc = doc.Object("TextDocument") as TextDocument;
                    if (textDoc == null) return;

                    // Move selection to the line - this scrolls the view
                    textDoc.Selection.GotoLine(lineNumber, false);
                    textDoc.Selection.ActivePoint.TryToShow(vsPaneShowHow.vsPaneShowCentered, null);
                };

                if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(scroll);
                }
                else
                {
                    scroll();
                }
            }
            catch
            {
                // Ignore scroll errors - not critical
            }
        }

        /// <summary>
        /// Attempts to close a file in Visual Studio.
        /// </summary>
        internal static void TryCloseFileInVisualStudio(string filePath)
        {
            try
            {
                Action closeFile = () =>
                {
                    try
                    {
                        DTE2 dte = GetDte();
                        if (dte == null) return;

                        // Check if file is open
                        Document doc = FindOpenDocument(dte, filePath);
                        if (doc != null)
                        {
                            // Close the document without saving (since we're deleting it)
                            doc.Close(EnvDTE.vsSaveChanges.vsSaveChangesNo);
                        }
                    }
                    catch { }
                };

                // DTE automation should run on the UI thread
                if (System.Windows.Application.Current != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(closeFile);
                }
                else
                {
                    closeFile();
                }
            }
            catch
            {
                // Silently fail if we can't close the file - it's not critical
            }
        }

        /// <summary>
        /// Attempts to set the content of an open document in Visual Studio.
        /// </summary>
        /// <param name="fullPath">Full path to the document.</param>
        /// <param name="newContent">New content to set.</param>
        /// <param name="save">Whether to save the document after setting content.</param>
        /// <returns>True if the document was found and content was set.</returns>
        internal static bool TrySetOpenDocumentContent(string fullPath, string newContent, bool save)
        {
            try
            {
                Action apply = () =>
                {
                    DTE2 dte = GetDte();
                    if (dte == null) return;

                    Document doc = FindOpenDocument(dte, fullPath);
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

                DTE2 check = GetDte();
                if (check == null) return false;
                return FindOpenDocument(check, fullPath) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a document is currently open in Visual Studio.
        /// </summary>
        internal static bool IsDocumentOpen(string fullPath)
        {
            try
            {
                DTE2 dte = GetDte();
                if (dte == null) return false;
                return FindOpenDocument(dte, fullPath) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
