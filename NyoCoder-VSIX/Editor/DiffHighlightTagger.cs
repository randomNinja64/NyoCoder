using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace NyoCoder
{
    [Export(typeof(IViewTaggerProvider))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [TagType(typeof(IClassificationTag))]
    internal sealed class DiffHighlightTaggerProvider : IViewTaggerProvider
    {
        [Import]
        internal IClassificationTypeRegistryService ClassificationTypeRegistry = null;

        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView == null || buffer == null)
                return null;
            if (textView.TextBuffer != buffer)
                return null;

            return textView.Properties.GetOrCreateSingletonProperty(
                typeof(DiffHighlightTagger),
                () => new DiffHighlightTagger(textView, ClassificationTypeRegistry)) as ITagger<T>;
        }
    }

    internal sealed class DiffHighlightTagger : ITagger<IClassificationTag>
    {
        private readonly ITextView _view;
        private readonly IClassificationType _additionType;
        private readonly IClassificationType _deletionType;
        private readonly string _filePath;
        private readonly object _gate = new object();
        private List<HighlightSpan> _spans = new List<HighlightSpan>();

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public DiffHighlightTagger(ITextView view, IClassificationTypeRegistryService registry)
        {
            _view = view;
            _additionType = registry.GetClassificationType(DiffHighlightClassificationTypes.Addition);
            _deletionType = registry.GetClassificationType(DiffHighlightClassificationTypes.Deletion);
            _filePath = GetFilePath(view);

            ToolHandler.OnDiffChangesPreview += OnDiffChangesPreview;
            ToolHandler.OnDiffPreviewCleared += OnDiffPreviewCleared;
            _view.Closed += OnViewClosed;
        }

        public IEnumerable<ITagSpan<IClassificationTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans == null || spans.Count == 0)
                yield break;

            List<HighlightSpan> current;
            lock (_gate)
            {
                if (_spans.Count == 0)
                    yield break;
                current = new List<HighlightSpan>(_spans);
            }

            ITextSnapshot snapshot = spans[0].Snapshot;

            foreach (HighlightSpan highlight in current)
            {
                SnapshotSpan translated;
                try
                {
                    translated = highlight.Span.TranslateTo(snapshot, SpanTrackingMode.EdgeInclusive);
                }
                catch
                {
                    continue;
                }

                if (translated.IsEmpty)
                    continue;

                foreach (SnapshotSpan request in spans)
                {
                    SnapshotSpan? maybe = translated.Intersection(request);
                    if (!maybe.HasValue || maybe.Value.IsEmpty)
                        continue;

                    IClassificationType type = highlight.Type == SearchReplaceTool.ChangeType.Addition
                        ? _additionType
                        : _deletionType;

                    yield return new TagSpan<IClassificationTag>(
                        maybe.Value,
                        new ClassificationTag(type));
                }
            }
        }

        private void OnDiffChangesPreview(string filePath, List<SearchReplaceTool.InlineSpan> changes)
        {
            if (!string.Equals(filePath, _filePath, StringComparison.OrdinalIgnoreCase))
                return;

            BeginInvokeOnUIThread(() => ApplyHighlights(changes));
        }

        private void OnDiffPreviewCleared(string filePath)
        {
            if (!string.Equals(filePath, _filePath, StringComparison.OrdinalIgnoreCase))
                return;

            BeginInvokeOnUIThread(ClearHighlights);
        }

        private void ApplyHighlights(List<SearchReplaceTool.InlineSpan> changes)
        {
            ITextSnapshot snapshot = _view.TextSnapshot;
            List<HighlightSpan> next = new List<HighlightSpan>();

            if (changes != null)
            {
                foreach (SearchReplaceTool.InlineSpan change in changes)
                {
                    try
                    {
                        int start = Math.Max(0, Math.Min(change.Start, snapshot.Length));
                        int len = Math.Max(0, change.Length);
                        if (len == 0)
                            continue;
                        if (start + len > snapshot.Length)
                            len = snapshot.Length - start;
                        if (len <= 0)
                            continue;

                        next.Add(new HighlightSpan
                        {
                            Span = new SnapshotSpan(snapshot, new Span(start, len)),
                            Type = change.Type
                        });
                    }
                    catch
                    {
                        // Skip invalid regions
                    }
                }
            }

            lock (_gate)
            {
                _spans = next;
            }

            RaiseAllTagsChanged(snapshot);
        }

        private void ClearHighlights()
        {
            lock (_gate)
            {
                if (_spans.Count == 0)
                    return;
                _spans = new List<HighlightSpan>();
            }

            RaiseAllTagsChanged(_view.TextSnapshot);
        }

        private void RaiseAllTagsChanged(ITextSnapshot snapshot)
        {
            EventHandler<SnapshotSpanEventArgs> handler = TagsChanged;
            if (handler == null || snapshot == null)
                return;

            handler(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        private void BeginInvokeOnUIThread(Action action)
        {
            System.Windows.Threading.Dispatcher dispatcher = null;
            IWpfTextView wpfView = _view as IWpfTextView;
            if (wpfView != null)
                dispatcher = wpfView.VisualElement.Dispatcher;

            if (dispatcher != null)
                dispatcher.BeginInvoke(action);
            else
                action();
        }

        private void OnViewClosed(object sender, EventArgs e)
        {
            _view.Closed -= OnViewClosed;
            ToolHandler.OnDiffChangesPreview -= OnDiffChangesPreview;
            ToolHandler.OnDiffPreviewCleared -= OnDiffPreviewCleared;

            lock (_gate)
            {
                _spans = new List<HighlightSpan>();
            }
        }

        private static string GetFilePath(ITextView textView)
        {
            try
            {
                ITextDocument document;
                if (textView.TextDataModel.DocumentBuffer.Properties.TryGetProperty(
                    typeof(ITextDocument), out document) && document != null)
                {
                    return document.FilePath ?? string.Empty;
                }
            }
            catch
            {
                // Unable to get file path
            }
            return string.Empty;
        }

        private sealed class HighlightSpan
        {
            public SnapshotSpan Span { get; set; }
            public SearchReplaceTool.ChangeType Type { get; set; }
        }
    }
}
