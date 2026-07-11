using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace NyoCoder
{
    internal static class DiffHighlightClassificationTypes
    {
        public const string Addition = "NyoCoder.diff.addition";
        public const string Deletion = "NyoCoder.diff.deletion";
    }

    internal static class DiffHighlightClassificationTypeDefinitions
    {
        [Export(typeof(ClassificationTypeDefinition))]
        [Name(DiffHighlightClassificationTypes.Addition)]
        internal static ClassificationTypeDefinition AdditionType = null;

        [Export(typeof(ClassificationTypeDefinition))]
        [Name(DiffHighlightClassificationTypes.Deletion)]
        internal static ClassificationTypeDefinition DeletionType = null;
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = DiffHighlightClassificationTypes.Addition)]
    [Name(DiffHighlightClassificationTypes.Addition)]
    [UserVisible(true)]
    [Order(After = Priority.Default)]
    internal sealed class DiffAdditionFormatDefinition : ClassificationFormatDefinition
    {
        public DiffAdditionFormatDefinition()
        {
            this.DisplayName = "NyoCoder Diff Addition";
            this.BackgroundColor = Color.FromRgb(0xB0, 0xF0, 0xB0);
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = DiffHighlightClassificationTypes.Deletion)]
    [Name(DiffHighlightClassificationTypes.Deletion)]
    [UserVisible(true)]
    [Order(After = Priority.Default)]
    internal sealed class DiffDeletionFormatDefinition : ClassificationFormatDefinition
    {
        public DiffDeletionFormatDefinition()
        {
            this.DisplayName = "NyoCoder Diff Deletion";
            this.BackgroundColor = Color.FromRgb(0xF5, 0xB0, 0xB0);
            this.TextDecorations = System.Windows.TextDecorations.Strikethrough;
        }
    }
}
