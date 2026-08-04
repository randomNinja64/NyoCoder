using System;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    /// <summary>
    /// WinForms host for the Indexing options page: a 3-way mode selector (Semantic / Symbol /
    /// Off)
    public class IndexingOptionsPageHost : OptionsPageHostBase
    {
        private RadioButton rbSemantic;
        private RadioButton rbSymbol;
        private RadioButton rbOff;

        private TextBox txtEndpoint;
        private TextBox txtModel;
        private Button btnModelList;
        private TextBox txtApiKey;
        private NumericUpDown numChunkLines;
        private NumericUpDown numMaxChars;
        private NumericUpDown numMaxChunksTotal;

        private Label lblStatusBrief;
        private Label lblStatusDetail;

        private Button btnIndexNow;
        private Button btnClear;

        private CheckBox chkOnSolutionOpen;
        private CheckBox chkOnSave;

        /// <summary>Raised when the user clicks "Index Now".</summary>
        public event Action IndexNowClicked;
        /// <summary>Raised when the user clicks "Clear Index".</summary>
        public event Action ClearClicked;

        public IndexingOptionsPageHost()
        {
            InitializeComponent();
            IndexingStatusReporter.StatusChanged += OnStatusChanged;
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(420);

            rbSemantic = new RadioButton { AutoSize = true, Text = "Semantic (embeddings-based search, API required)" };
            rbSymbol = new RadioButton { AutoSize = true, Text = "Symbol (offline symbol map search, no embeddings)" };
            rbOff = new RadioButton { AutoSize = true, Text = "Off (codebase_search falls back to grep)" };

            FlowLayoutPanel modePanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };
            modePanel.Controls.Add(rbSemantic);
            modePanel.Controls.Add(rbSymbol);
            modePanel.Controls.Add(rbOff);

            Label lblEndpoint = new Label { AutoSize = true, Text = "Embeddings endpoint (OpenAI-compatible; blank = use default LLM server):" };
            txtEndpoint = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };

            Label lblModel = new Label { AutoSize = true, Text = "Embeddings model:" };
            txtModel = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 6, 0)
            };
            btnModelList = new Button
            {
                Text = "Model List",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0),
                FlatStyle = FlatStyle.System
            };
            btnModelList.Click += OnModelListClick;

            TableLayoutPanel modelRow = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            modelRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            modelRow.Controls.Add(txtModel, 0, 0);
            modelRow.Controls.Add(btnModelList, 1, 0);

            Label lblApiKey = new Label { AutoSize = true, Text = "Embeddings API key (optional; blank = use default API key):" };
            txtApiKey = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, UseSystemPasswordChar = true };

            Label lblChunkLines = new Label { AutoSize = true, Text = "Chunk size (lines per embedding chunk):" };
            numChunkLines = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 500,
                Increment = 5,
                Width = 120
            };

            Label lblMaxChars = new Label { AutoSize = true, Text = "Max characters per embedding (longer text is truncated):" };
            numMaxChars = new NumericUpDown
            {
                Minimum = 256,
                Maximum = 100000,
                Increment = 256,
                Width = 120
            };

            Label lblMaxChunksTotal = new Label { AutoSize = true, Text = "Max embedding chunks per full index (semantic mode):" };
            numMaxChunksTotal = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 1000000,
                Increment = 1000,
                Width = 120
            };

            lblStatusBrief = new Label { AutoSize = true, Font = new Font(this.Font, FontStyle.Bold), Text = "Index: (loading...)" };
            lblStatusDetail = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = "" };

            btnIndexNow = new Button { AutoSize = true, Text = "Index Now" };
            btnIndexNow.Click += (s, e) => { RaiseIndexNow(); };
            btnClear = new Button { AutoSize = true, Text = "Clear Index" };
            btnClear.Click += (s, e) => { RaiseClear(); };

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0)
            };
            buttonPanel.Controls.Add(btnIndexNow);
            buttonPanel.Controls.Add(btnClear);

            chkOnSolutionOpen = new CheckBox { AutoSize = true, Text = "Index on solution open" };
            chkOnSave = new CheckBox { AutoSize = true, Text = "Re-index changed files on save" };

            AddRow(MakeSectionTitle("Indexing:"), new Padding(0, 0, 0, 12), false);

            AddRow(MakeSectionTitle("Mode:"), new Padding(0, 0, 0, 4), false);
            AddRow(modePanel, new Padding(0, 0, 0, 12), false);

            AddRow(MakeSectionTitle("Embeddings:"), new Padding(0, 0, 0, 4), false);
            AddRow(lblEndpoint, new Padding(0, 0, 0, 4), true);
            AddRow(txtEndpoint, new Padding(0, 0, 0, 8), false);
            AddRow(lblModel, new Padding(0, 0, 0, 4), true);
            AddRow(modelRow, new Padding(0, 0, 0, 8), false);
            AddRow(lblApiKey, new Padding(0, 0, 0, 4), true);
            AddRow(txtApiKey, new Padding(0, 0, 0, 8), false);
            AddRow(lblChunkLines, new Padding(0, 0, 0, 4), true);
            AddRow(numChunkLines, new Padding(0, 0, 0, 8), false);
            AddRow(lblMaxChars, new Padding(0, 0, 0, 4), true);
            AddRow(numMaxChars, new Padding(0, 0, 0, 8), false);
            AddRow(lblMaxChunksTotal, new Padding(0, 0, 0, 4), true);
            AddRow(numMaxChunksTotal, new Padding(0, 0, 0, 12), false);

            AddRow(MakeSectionTitle("Status:"), new Padding(0, 0, 0, 4), false);
            AddRow(lblStatusBrief, new Padding(0, 0, 0, 4), true);
            AddRow(lblStatusDetail, new Padding(0, 0, 0, 8), true);
            AddRow(buttonPanel, new Padding(0, 0, 0, 12), false);

            AddRow(MakeSectionTitle("When to index:"), new Padding(0, 0, 0, 4), false);
            AddRow(chkOnSolutionOpen, new Padding(0, 0, 0, 4), true);
            AddRow(chkOnSave, new Padding(0, 0, 0, 0), true);

            this.ResumeLayout(false);
            this.PerformLayout();
            UpdateWrappingWidths();
        }

        // ── Public properties bound by the page ────────────────────────

        public IndexingMode Mode
        {
            get
            {
                if (rbSemantic.Checked) return IndexingMode.Semantic;
                if (rbOff.Checked) return IndexingMode.Off;
                return IndexingMode.Symbol;
            }
            set
            {
                if (value == IndexingMode.Semantic)
                    rbSemantic.Checked = true;
                else if (value == IndexingMode.Off)
                    rbOff.Checked = true;
                else
                    rbSymbol.Checked = true;
            }
        }

        public string Endpoint
        {
            get { return txtEndpoint.Text != null ? txtEndpoint.Text.Trim() : string.Empty; }
            set { txtEndpoint.Text = value ?? string.Empty; }
        }

        public string Model
        {
            get { return txtModel.Text != null ? txtModel.Text.Trim() : string.Empty; }
            set { txtModel.Text = value ?? string.Empty; }
        }

        public string ApiKey
        {
            get { return txtApiKey.Text != null ? txtApiKey.Text.Trim() : string.Empty; }
            set { txtApiKey.Text = value ?? string.Empty; }
        }

        public int ChunkLines
        {
            get { return (int)numChunkLines.Value; }
            set { numChunkLines.Value = Math.Max(numChunkLines.Minimum, Math.Min(numChunkLines.Maximum, value)); }
        }

        public int MaxEmbedChars
        {
            get { return (int)numMaxChars.Value; }
            set { numMaxChars.Value = Math.Max(numMaxChars.Minimum, Math.Min(numMaxChars.Maximum, value)); }
        }

        public int MaxChunksTotal
        {
            get { return (int)numMaxChunksTotal.Value; }
            set { numMaxChunksTotal.Value = Math.Max(numMaxChunksTotal.Minimum, Math.Min(numMaxChunksTotal.Maximum, value)); }
        }

        public bool IndexOnSolutionOpen
        {
            get { return chkOnSolutionOpen.Checked; }
            set { chkOnSolutionOpen.Checked = value; }
        }

        public bool IndexOnSave
        {
            get { return chkOnSave.Checked; }
            set { chkOnSave.Checked = value; }
        }

        public void LoadFromConfig()
        {
            Endpoint = ConfigHandler.GetEmbeddingsEndpointRaw();
            Model = ConfigHandler.GetEmbeddingsModel();
            ApiKey = ConfigHandler.GetEmbeddingsApiKeyRaw();
            IndexOnSolutionOpen = ConfigHandler.GetIndexOnSolutionOpen();
            IndexOnSave = ConfigHandler.GetIndexOnSave();
            ChunkLines = ConfigHandler.GetIndexChunkLines();
            MaxEmbedChars = ConfigHandler.GetEmbeddingsMaxChars();
            MaxChunksTotal = ConfigHandler.GetIndexMaxChunksTotal();
            Mode = ConfigHandler.GetIndexingMode();

            CodebaseIndex.PublishStatus();
            RefreshStatus();
        }

        public void SaveToConfig()
        {
            ConfigHandler.SetIndexingMode(Mode);
            ConfigHandler.SetConfigValue("embeddingsEndpoint", Endpoint);
            ConfigHandler.SetConfigValue("embeddingsModel", Model);
            ConfigHandler.SetConfigValue("embeddingsApiKey", ApiKey);
            ConfigHandler.SetConfigValue("indexOnSolutionOpen", IndexOnSolutionOpen ? "1" : "0");
            ConfigHandler.SetConfigValue("indexOnSave", IndexOnSave ? "1" : "0");
            ConfigHandler.SetIndexChunkLines(ChunkLines);
            ConfigHandler.SetEmbeddingsMaxChars(MaxEmbedChars);
            ConfigHandler.SetIndexMaxChunksTotal(MaxChunksTotal);
        }

        private void OnModelListClick(object sender, EventArgs e)
        {
            string baseUrl = Endpoint;
            if (string.IsNullOrEmpty(baseUrl))
                baseUrl = ConfigHandler.GetLlmServer();
            if (baseUrl != null)
                baseUrl = baseUrl.Trim();

            if (string.IsNullOrEmpty(baseUrl))
            {
                MessageBox.Show(
                    this,
                    "Enter an embeddings endpoint (or configure an LLM server) before listing models.",
                    "Model List",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string apiKey = ApiKey;
            if (string.IsNullOrEmpty(apiKey))
                apiKey = ConfigHandler.GetApiKey();

            using (ModelChooserDialog dialog = new ModelChooserDialog(baseUrl, apiKey, Model))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedModel))
                    Model = dialog.SelectedModel;
            }
        }

        // ── Behavior ───────────────────────────────────────────────────

        private void RaiseIndexNow()
        {
            Action handler = IndexNowClicked;
            if (handler != null) handler();
        }

        private void RaiseClear()
        {
            Action handler = ClearClicked;
            if (handler != null) handler();
        }

        // ── Status display ─────────────────────────────────────────────

        public void RefreshStatus()
        {
            IndexingStatusSnapshot snapshot = IndexingStatusReporter.Current;
            lblStatusBrief.Text = string.IsNullOrEmpty(snapshot.BriefText) ? "Index: (unknown)" : snapshot.BriefText;
            lblStatusDetail.Text = snapshot.DetailText ?? string.Empty;
            UpdateWrappingWidths();
        }

        private void OnStatusChanged()
        {
            try
            {
                if (InvokeRequired)
                    BeginInvoke(new Action(RefreshStatus));
                else
                    RefreshStatus();
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { IndexingStatusReporter.StatusChanged -= OnStatusChanged; }
                catch { }
            }
            base.Dispose(disposing);
        }
    }
}
