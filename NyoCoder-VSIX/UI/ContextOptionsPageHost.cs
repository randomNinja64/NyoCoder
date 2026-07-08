using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class ContextOptionsPageHost : OptionsPageHostBase
    {
        private Label lblMaxReadLines;
        private TextBox txtMaxReadLines;
        private Label lblContextWindowSize;
        private TextBox txtContextWindowSize;

        public ContextOptionsPageHost()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(320);

            this.lblMaxReadLines = new Label();
            this.lblMaxReadLines.AutoSize = true;
            this.lblMaxReadLines.Text = "Max File Lines to Read:";

            this.txtMaxReadLines = new TextBox();
            this.txtMaxReadLines.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            this.lblContextWindowSize = new Label();
            this.lblContextWindowSize.AutoSize = true;
            this.lblContextWindowSize.Text = "Context Window Size (tokens):";

            this.txtContextWindowSize = new TextBox();
            this.txtContextWindowSize.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            AddRow(MakeSectionTitle("Context:"), new Padding(0, 0, 0, 12), false);
            AddRow(this.lblMaxReadLines, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtMaxReadLines, new Padding(0, 0, 0, 12), false);
            AddRow(this.lblContextWindowSize, new Padding(0, 0, 0, 4), true);
            AddRow(this.txtContextWindowSize, new Padding(0, 0, 0, 0), false);

            this.ResumeLayout(false);
            this.PerformLayout();
            UpdateWrappingWidths();
        }

        public int MaxReadLines
        {
            get
            {
                int result;
                if (int.TryParse(txtMaxReadLines.Text, out result) && result > 0)
                    return result;
                return 500;
            }
            set { txtMaxReadLines.Text = value.ToString(); }
        }

        public int? ContextWindowSize
        {
            get
            {
                string text = txtContextWindowSize.Text != null ? txtContextWindowSize.Text.Trim() : null;
                if (string.IsNullOrEmpty(text))
                    return null;
                int result;
                return (int.TryParse(text, out result) && result > 0) ? (int?)result : null;
            }
            set
            {
                txtContextWindowSize.Text = value.HasValue ? value.Value.ToString() : string.Empty;
            }
        }
    }
}
