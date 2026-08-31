using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    /// <summary>
    /// Shared base for options page host controls.
    /// Provides the TableLayoutPanel, AddRow helper, and auto-wrapping control infrastructure.
    /// </summary>
    public abstract class OptionsPageHostBase : UserControl
    {
        protected TableLayoutPanel layout;
        private readonly List<Control> _wrappingControls = new List<Control>();

        /// <summary>Loads current config values into the page controls.</summary>
        public abstract void LoadFromConfig();

        /// <summary>Writes page control values to ConfigHandler.</summary>
        public abstract void SaveToConfig();

        /// <summary>
        /// Initialises the shared TableLayoutPanel and sets default control sizing.
        /// Call this from the subclass constructor inside a SuspendLayout/ResumeLayout pair.
        /// </summary>
        protected void InitLayout(int initialHeight = 320)
        {
            this.AutoScaleMode = AutoScaleMode.Font;
            this.AutoScroll = true;
            this.BackColor = SystemColors.Control;

            this.layout = new TableLayoutPanel();
            this.layout.AutoSize = true;
            this.layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.layout.ColumnCount = 1;
            this.layout.Dock = DockStyle.Top;
            this.layout.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
            this.layout.Padding = new Padding(20, 10, 20, 10);
            this.layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            this.Controls.Add(this.layout);
            this.MinimumSize = new Size(420, 0);
            this.Size = new Size(420, initialHeight);
        }

        /// <summary>Creates a bold section-title Label.</summary>
        protected Label MakeSectionTitle(string text)
        {
            return new Label
            {
                AutoSize = true,
                Font = new Font(this.Font, FontStyle.Bold),
                Text = text
            };
        }

        /// <summary>
        /// Appends a control as a new row in the layout.
        /// When <paramref name="wrap"/> is <c>true</c> the control's MaximumSize width is kept
        /// in sync with the available panel width on resize.
        /// </summary>
        protected void AddRow(Control control, Padding margin, bool wrap)
        {
            control.Margin = margin;
            this.layout.RowStyles.Add(new RowStyle());
            this.layout.Controls.Add(control, 0, this.layout.RowCount);
            this.layout.RowCount++;

            if (wrap)
                _wrappingControls.Add(control);
        }

        /// <summary>Updates the MaximumSize of all wrapping controls to fit the current panel width.</summary>
        protected void UpdateWrappingWidths()
        {
            if (layout == null) return;
            int availableWidth = Math.Max(120, this.ClientSize.Width - this.layout.Padding.Left - this.layout.Padding.Right - 8);
            foreach (Control control in _wrappingControls)
                control.MaximumSize = new Size(availableWidth, 0);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateWrappingWidths();
        }
    }
}
