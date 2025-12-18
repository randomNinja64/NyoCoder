using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;

namespace NyoCoder
{
    /// <summary>
    /// Adornment class that highlights diff changes in the editor.
    /// Additions are highlighted in green, deletions in red.
    /// </summary>
    public class DiffHighlightAdornment
    {
        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;
        private readonly List<HighlightSpan> _spans;
        private readonly string _filePath;

        // Inline highlight colors
        private static readonly Brush AddBrush = new SolidColorBrush(Color.FromArgb(70, 0, 200, 0));
        private static readonly Brush DelBrush = new SolidColorBrush(Color.FromArgb(70, 255, 0, 0));
        private static readonly Brush DelStrikeBrush = new SolidColorBrush(Color.FromArgb(180, 150, 0, 0));

        static DiffHighlightAdornment()
        {
            AddBrush.Freeze();
            DelBrush.Freeze();
            DelStrikeBrush.Freeze();
        }

        public DiffHighlightAdornment(IWpfTextView view, string filePath)
        {
            _view = view;
            _filePath = filePath;
            _spans = new List<HighlightSpan>();
            _layer = view.GetAdornmentLayer("DiffHighlightAdornment");

            // Subscribe to events
            _view.LayoutChanged += OnLayoutChanged;
            _view.Closed += OnViewClosed;

            // Subscribe to tool handler events
            ToolHandler.OnDiffChangesPreview += OnDiffChangesPreview;
            ToolHandler.OnDiffPreviewCleared += OnDiffPreviewCleared;
        }

        private void OnViewClosed(object sender, EventArgs e)
        {
            // Unsubscribe from events
            _view.LayoutChanged -= OnLayoutChanged;
            _view.Closed -= OnViewClosed;
            ToolHandler.OnDiffChangesPreview -= OnDiffChangesPreview;
            ToolHandler.OnDiffPreviewCleared -= OnDiffPreviewCleared;
            
            // Clear highlights
            _spans.Clear();
            _layer.RemoveAllAdornments();
        }

        private void OnDiffChangesPreview(string filePath, List<ToolHandler.DiffChange> changes)
        {
            // Check if this is the file we're viewing
            if (!string.Equals(filePath, _filePath, StringComparison.OrdinalIgnoreCase))
                return;

            // Must be on UI thread
            _view.VisualElement.Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyHighlights(changes);
            }));
        }

        private void OnDiffPreviewCleared(string filePath)
        {
            if (!string.Equals(filePath, _filePath, StringComparison.OrdinalIgnoreCase))
                return;

            _view.VisualElement.Dispatcher.BeginInvoke(new Action(() =>
            {
                ClearHighlights();
            }));
        }

        private void ApplyHighlights(List<ToolHandler.DiffChange> changes)
        {
            // Clear existing highlights
            _spans.Clear();
            _layer.RemoveAllAdornments();

            ITextSnapshot snapshot = _view.TextSnapshot;

            foreach (ToolHandler.DiffChange change in changes)
            {
                try
                {
                    int start = Math.Max(0, Math.Min(change.StartIndex, snapshot.Length));
                    int len = Math.Max(0, change.Length);
                    if (len == 0) continue;
                    if (start + len > snapshot.Length) len = snapshot.Length - start;
                    if (len <= 0) continue;

                    SnapshotSpan raw = new SnapshotSpan(snapshot, new Span(start, len));
                    foreach (SnapshotSpan s in SplitByLine(raw))
                    {
                        _spans.Add(new HighlightSpan
                        {
                            Span = s,
                            Type = change.Type
                        });
                    }
                }
                catch
                {
                    // Skip invalid regions
                }
            }

            // Redraw
            DrawHighlights();
        }

        public void ClearHighlights()
        {
            _spans.Clear();
            _layer.RemoveAllAdornments();
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            // Redraw highlights when layout changes
            DrawHighlights();
        }

        private void DrawHighlights()
        {
            _layer.RemoveAllAdornments();

            if (_spans.Count == 0)
                return;

            ITextSnapshot snapshot = _view.TextSnapshot;

            foreach (HighlightSpan h in _spans)
            {
                try
                {
                    SnapshotSpan span = h.Span.TranslateTo(snapshot, SpanTrackingMode.EdgeInclusive);
                    Geometry geom = _view.TextViewLines.GetMarkerGeometry(span);
                    if (geom == null) continue;

                    Brush fill = (h.Type == ToolHandler.DiffChangeType.Addition) ? AddBrush : DelBrush;

                    System.Windows.Shapes.Path path = new System.Windows.Shapes.Path
                    {
                        Data = geom,
                        Fill = fill,
                        IsHitTestVisible = false
                    };

                    _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, null, path, null);

                    if (h.Type == ToolHandler.DiffChangeType.Deletion)
                    {
                        Rect b = geom.Bounds;
                        System.Windows.Shapes.Line line = new System.Windows.Shapes.Line
                        {
                            X1 = b.Left,
                            X2 = b.Right,
                            Y1 = b.Top + (b.Height / 2),
                            Y2 = b.Top + (b.Height / 2),
                            Stroke = DelStrikeBrush,
                            StrokeThickness = 1.5,
                            IsHitTestVisible = false
                        };

                        _layer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative, span, null, line, null);
                    }
                }
                catch
                {
                    // Skip invalid regions
                }
            }
        }

        private static IEnumerable<SnapshotSpan> SplitByLine(SnapshotSpan span)
        {
            if (span.IsEmpty) yield break;

            ITextSnapshot snapshot = span.Snapshot;
            int startLine = snapshot.GetLineNumberFromPosition(span.Start.Position);
            int endLine = snapshot.GetLineNumberFromPosition(Math.Max(span.End.Position - 1, span.Start.Position));

            for (int lineNum = startLine; lineNum <= endLine; lineNum++)
            {
                ITextSnapshotLine line = snapshot.GetLineFromLineNumber(lineNum);
                int s = Math.Max(span.Start.Position, line.Start.Position);
                int e = Math.Min(span.End.Position, line.End.Position);
                if (e > s)
                {
                    yield return new SnapshotSpan(snapshot, Span.FromBounds(s, e));
                }
            }
        }

        private sealed class HighlightSpan
        {
            public SnapshotSpan Span { get; set; }
            public ToolHandler.DiffChangeType Type { get; set; }
        }
    }
}
