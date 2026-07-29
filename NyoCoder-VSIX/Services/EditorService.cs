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
        /// Invokes an action on the UI thread if necessary.
        /// If already on the UI thread, executes directly; otherwise dispatches to the UI thread.
        /// </summary>
        /// <param name="action">The action to invoke.</param>
        /// <param name="dispatcher">Optional dispatcher to use. If null, uses Application.Current.Dispatcher.</param>
        internal static void InvokeOnUIThread(Action action, System.Windows.Threading.Dispatcher dispatcher = null)
        {
            dispatcher = dispatcher ?? (System.Windows.Application.Current != null ? System.Windows.Application.Current.Dispatcher : null);
            
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }

        /// <summary>
        /// Begins invoking an action on the UI thread asynchronously (non-blocking).
        /// At the default (Normal) priority: if already on the UI thread, executes directly;
        /// otherwise dispatches to the UI thread. Priorities below Normal are always queued
        /// through the dispatcher, even if already on the UI thread — see <paramref name="priority"/>.
        /// </summary>
        /// <param name="action">The action to invoke.</param>
        /// <param name="dispatcher">Optional dispatcher to use. If null, uses Application.Current.Dispatcher.</param>
        /// <param name="priority">
        /// Dispatcher priority for the queued invocation. Defaults to Normal. Pass a priority
        /// below Render (e.g. Background) when the action depends on a pending layout pass,
        /// since Normal-priority callbacks run before queued Render-priority layout work.
        /// </param>
        internal static void BeginInvokeOnUIThread(
            Action action,
            System.Windows.Threading.Dispatcher dispatcher = null,
            System.Windows.Threading.DispatcherPriority priority = System.Windows.Threading.DispatcherPriority.Normal)
        {
            dispatcher = dispatcher ?? (System.Windows.Application.Current != null ? System.Windows.Application.Current.Dispatcher : null);

            if (dispatcher == null)
            {
                action();
                return;
            }

            // A below-Normal priority (e.g. Background) means the caller wants this to run
            // after any already-queued layout/render work. Callers that are themselves
            // marshalled onto the UI thread via a *synchronous* Invoke (e.g. interaction
            // prompts triggered from a background thread) are technically "on" the UI thread
            // by the time they call this, so the CheckAccess fast path below would otherwise
            // run the action inline — ahead of that pending layout — defeating the priority
            // entirely. Route anything below Normal through the dispatcher queue unconditionally.
            if (priority < System.Windows.Threading.DispatcherPriority.Normal)
            {
                dispatcher.BeginInvoke(action, priority);
                return;
            }

            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(action, priority);
            }
            else
            {
                action();
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
                InvokeOnUIThread(openFile);
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

                InvokeOnUIThread(scroll);
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
                InvokeOnUIThread(closeFile);
            }
            catch
            {
                // Silently fail if we can't close the file - it's not critical
            }
        }

        /// <summary>
        /// Attempts to read the content of an open document in Visual Studio.
        /// </summary>
        /// <param name="fullPath">Full path to the document.</param>
        /// <param name="content">Output parameter for the document content, or null if not found/not open.</param>
        /// <returns>True if the document was found and content was read.</returns>
        internal static bool TryReadOpenDocument(string fullPath, out string content)
        {
            content = null;

            try
            {
                string localContent = null;
                bool found = false;

                Action read = () =>
                {
                    DTE2 dte = GetDte();
                    if (dte == null) return;

                    Document doc = FindOpenDocument(dte, fullPath);
                    if (doc == null) return;

                    TextDocument textDoc = doc.Object("TextDocument") as TextDocument;
                    if (textDoc == null) return;

                    found = true;
                    EditPoint start = textDoc.StartPoint.CreateEditPoint();
                    localContent = start.GetText(textDoc.EndPoint);
                };

                InvokeOnUIThread(read);
                content = localContent;
                return found;
            }
            catch
            {
                content = null;
                return false;
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

                InvokeOnUIThread(apply);

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
        /// Normalizes a file path by expanding environment variables and resolving to full path.
        /// Expands environment variables, resolves relative paths, and returns the full absolute path.
        /// </summary>
        /// <param name="filePath">The file path to normalize. Can be null or empty.</param>
        /// <returns>The normalized full path, or null if the input is null/empty, or the best-effort expanded path if normalization fails.</returns>
        internal static string NormalizeFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            try
            {
                string expandedPath = Environment.ExpandEnvironmentVariables(filePath.Trim());
                if (!System.IO.Path.IsPathRooted(expandedPath))
                {
                    expandedPath = System.IO.Path.Combine(Environment.CurrentDirectory, expandedPath);
                }
                return System.IO.Path.GetFullPath(expandedPath);
            }
            catch
            {
                // If normalization fails, return the expanded path or original
                try
                {
                    return Environment.ExpandEnvironmentVariables(filePath.Trim());
                }
                catch
                {
                    return filePath;
                }
            }
        }

    }
}
