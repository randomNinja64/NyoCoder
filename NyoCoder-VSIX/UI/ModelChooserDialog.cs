using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    /// <summary>
    /// Dialog that fetches models from an OpenAI-compatible <c>/v1/models</c> endpoint
    /// and lets the user pick one.
    /// </summary>
    public class ModelChooserDialog : Form
    {
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _currentModel;

        private ListBox lstModels;
        private Button btnOk;
        private Button btnCancel;
        private Label lblStatus;
        private bool _loaded;

        public string SelectedModel { get; private set; }

        public ModelChooserDialog(string baseUrl, string apiKey, string currentModel)
        {
            _baseUrl = baseUrl ?? string.Empty;
            _apiKey = apiKey ?? string.Empty;
            _currentModel = currentModel ?? string.Empty;
            SelectedModel = string.Empty;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Select Model";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(300, 240);
            this.Font = SystemFonts.MessageBoxFont;

            lblStatus = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 24,
                Text = "Loading models...",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };

            lstModels = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false
            };
            lstModels.DoubleClick += OnListDoubleClick;

            btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.None,
                Enabled = false,
                Width = 80,
                Height = 26
            };
            btnOk.Click += OnOkClick;

            btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Width = 80,
                Height = 26
            };

            FlowLayoutPanel buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 40,
                Padding = new Padding(8, 6, 8, 6),
                WrapContents = false
            };
            buttonPanel.Controls.Add(btnCancel);
            buttonPanel.Controls.Add(btnOk);

            Panel listHost = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 4, 8, 0)
            };
            listHost.Controls.Add(lstModels);

            this.Controls.Add(listHost);
            this.Controls.Add(buttonPanel);
            this.Controls.Add(lblStatus);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;
            this.Shown += OnShown;
        }

        private void OnShown(object sender, EventArgs e)
        {
            if (_loaded)
                return;
            _loaded = true;
            LoadModels();
        }

        private void LoadModels()
        {
            Cursor previous = this.Cursor;
            this.Cursor = Cursors.WaitCursor;
            try
            {
                IList<string> models = ModelsClient.ListModels(_baseUrl, _apiKey);
                lstModels.BeginUpdate();
                try
                {
                    lstModels.Items.Clear();
                    foreach (string id in models)
                        lstModels.Items.Add(id);

                    if (!string.IsNullOrEmpty(_currentModel))
                    {
                        int index = lstModels.FindStringExact(_currentModel);
                        if (index >= 0)
                            lstModels.SelectedIndex = index;
                        else if (lstModels.Items.Count > 0)
                            lstModels.SelectedIndex = 0;
                    }
                    else if (lstModels.Items.Count > 0)
                    {
                        lstModels.SelectedIndex = 0;
                    }
                }
                finally
                {
                    lstModels.EndUpdate();
                }

                lblStatus.Text = models.Count + " model" + (models.Count == 1 ? "" : "s") + " found";
                btnOk.Enabled = lstModels.Items.Count > 0;
                lstModels.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Model List",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
            finally
            {
                this.Cursor = previous;
            }
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            if (lstModels.SelectedItem == null)
            {
                MessageBox.Show(
                    this,
                    "Select a model from the list.",
                    "Model List",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            SelectedModel = lstModels.SelectedItem.ToString();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void OnListDoubleClick(object sender, EventArgs e)
        {
            if (lstModels.SelectedItem != null)
                OnOkClick(sender, e);
        }
    }
}
