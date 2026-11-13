using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace NyoCoder
{
    /// <summary>
    /// Diff preview highlighting for the pre-2010 editor, using text line markers in place of
    /// the MEF classification tagger. An add-in cannot register its own marker types (that
    /// needs a package-proffered service), so built-in types are used; their colors are the
    /// ones listed under Tools > Options > Fonts and Colors.
    /// </summary>
    internal static class DiffHighlightMarkers
    {
        private const int AdditionMarkerType = 15; // MARKERTYPE2.MARKER_EXSTENCIL ("Code Snippet Field")
        private static int _deletionMarkerType;    // custom "NyoCoder Deleted Text" marker; 0 if not registered

        private static readonly Dictionary<string, List<IVsTextLineMarker>> _markers =
            new Dictionary<string, List<IVsTextLineMarker>>(StringComparer.OrdinalIgnoreCase);

        internal static void Attach()
        {
            _deletionMarkerType = DeletionMarkerType.Register();

            ToolHandler.OnDiffChangesPreview += (filePath, changes) =>
                EditorService.BeginInvokeOnUIThread(() => ApplyHighlights(filePath, changes));
            ToolHandler.OnDiffPreviewCleared += filePath =>
                EditorService.BeginInvokeOnUIThread(() => ClearHighlights(filePath));
        }

        private static void ApplyHighlights(string filePath, List<SearchReplaceTool.InlineSpan> changes)
        {
            try
            {
                ClearHighlights(filePath);

                IVsTextLines lines = GetTextLines(filePath);
                if (lines == null || changes == null)
                    return;

                List<IVsTextLineMarker> created = new List<IVsTextLineMarker>();
                foreach (SearchReplaceTool.InlineSpan change in changes)
                {
                    if (change.Length <= 0)
                        continue;

                    int startLine, startIndex, endLine, endIndex;
                    if (lines.GetLineIndexOfPosition(change.Start, out startLine, out startIndex) != 0 ||
                        lines.GetLineIndexOfPosition(change.Start + change.Length, out endLine, out endIndex) != 0)
                        continue;

                    int type = change.Type == SearchReplaceTool.ChangeType.Addition
                        ? AdditionMarkerType
                        : _deletionMarkerType;
                    if (type == 0)
                        continue;

                    IVsTextLineMarker[] marker = new IVsTextLineMarker[1];
                    if (lines.CreateLineMarker(type, startLine, startIndex, endLine, endIndex, null, marker) == 0 &&
                        marker[0] != null)
                        created.Add(marker[0]);
                }

                _markers[filePath] = created;
            }
            catch
            {
                // Highlighting is best-effort; the approval prompt still shows the diff.
            }
        }

        private static void ClearHighlights(string filePath)
        {
            List<IVsTextLineMarker> existing;
            if (!_markers.TryGetValue(filePath, out existing))
                return;

            _markers.Remove(filePath);
            foreach (IVsTextLineMarker marker in existing)
            {
                try { marker.Invalidate(); } catch { }
            }
        }

        private static IVsTextLines GetTextLines(string filePath)
        {
            IVsRunningDocumentTable rdt = Package.GetGlobalService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            if (rdt == null)
                return null;

            IVsHierarchy hierarchy;
            uint itemId, cookie;
            IntPtr docData;
            if (rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_NoLock, filePath, out hierarchy, out itemId, out docData, out cookie) != 0 ||
                docData == IntPtr.Zero)
                return null;

            try
            {
                object data = Marshal.GetObjectForIUnknown(docData);
                IVsTextLines lines = data as IVsTextLines;
                IVsTextBufferProvider provider = data as IVsTextBufferProvider;
                if (lines == null && provider != null)
                    provider.GetTextBuffer(out lines);
                return lines;
            }
            finally
            {
                Marshal.Release(docData);
            }
        }
    }
}
