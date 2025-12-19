using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace NyoCoder
{
    /// <summary>
    /// Establishes an <see cref="IAdornmentLayer"/> to place the diff highlight adornment on 
    /// and exports the <see cref="IWpfTextViewCreationListener"/> that instantiates the adornment on text views.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class DiffHighlightAdornmentFactory : IWpfTextViewCreationListener
    {
        /// <summary>
        /// Defines the adornment layer for the diff highlight adornment.
        /// This layer is ordered after the selection layer (to appear above selections).
        /// </summary>
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("DiffHighlightAdornment")]
        [Order(After = PredefinedAdornmentLayers.Selection, Before = PredefinedAdornmentLayers.Text)]
        public AdornmentLayerDefinition EditorAdornmentLayer = null;

        /// <summary>
        /// Called when a text view having matching roles is created over a text data model having a matching content type.
        /// Instantiates a DiffHighlightAdornment when the text view is created.
        /// </summary>
        /// <param name="textView">The <see cref="IWpfTextView"/> upon which the adornment should be placed</param>
        public void TextViewCreated(IWpfTextView textView)
        {
            // Get the file path for this text view
            string filePath = GetFilePath(textView);
            
            // Create the adornment - it will attach to the text view
            new DiffHighlightAdornment(textView, filePath);
        }

        private string GetFilePath(IWpfTextView textView)
        {
            try
            {
                Microsoft.VisualStudio.Text.ITextDocument document;
                if (textView.TextDataModel.DocumentBuffer.Properties.TryGetProperty(
                    typeof(Microsoft.VisualStudio.Text.ITextDocument), out document))
                {
                    return document.FilePath;
                }
            }
            catch
            {
                // Unable to get file path
            }
            return string.Empty;
        }
    }
}
