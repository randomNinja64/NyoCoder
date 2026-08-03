using System;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    /// <summary>
    /// First-run wizard for essential Options when NyoCoder.ini is missing.
    /// </summary>
    public class OnboardingWizardForm : Form
    {
        private static bool _isShowing;
        /// <summary>
        /// After Cancel, suppress nested ShowIfNeeded calls (e.g. CreateFromConfig after Ask)
        /// until the next user entry point resets this flag.
        /// </summary>
        private static bool _suppressUntilNextEntry;

        private readonly Label _lblTitle;
        private readonly Label _lblSubtitle;
        private readonly Panel _contentPanel;
        private readonly Button _btnBack;
        private readonly Button _btnNext;
        private readonly Button _btnCancel;

        private readonly Control[] _hosts;
        private readonly string[] _stepTitles;
        private int _stepIndex;

        private OnboardingWizardForm()
        {
            Text = "NyoCoder Setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            ClientSize = new Size(500, 360);
            Font = SystemFonts.MessageBoxFont;

            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 58,
                BackColor = Color.White,
                Padding = new Padding(14, 10, 14, 8)
            };

            _lblTitle = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 20,
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = SystemColors.ControlText,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lblSubtitle = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = SystemColors.GrayText,
                Text = "Defaults are selected. You can change these later in Tools > Options.",
                TextAlign = ContentAlignment.TopLeft
            };

            header.Controls.Add(_lblSubtitle);
            header.Controls.Add(_lblTitle);

            Panel headerRule = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = SystemColors.ControlDark
            };

            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(4, 2, 4, 2),
                AutoScroll = false,
                BackColor = SystemColors.Control
            };

            Panel footerRule = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = SystemColors.ControlDark
            };

            Panel buttonBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                BackColor = SystemColors.Control
            };

            _btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.None,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                Size = new Size(75, 23),
                TabIndex = 2
            };
            _btnCancel.Click += OnCancelClick;

            _btnNext = new Button
            {
                Text = "Next >",
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                Size = new Size(75, 23),
                TabIndex = 1
            };
            _btnNext.Click += OnNextClick;

            _btnBack = new Button
            {
                Text = "< Back",
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                Size = new Size(75, 23),
                Enabled = false,
                TabIndex = 0
            };
            _btnBack.Click += OnBackClick;

            // Tab order: Back → Next → Cancel (Cancel last so it is not the first focused button)
            buttonBar.Controls.Add(_btnBack);
            buttonBar.Controls.Add(_btnNext);
            buttonBar.Controls.Add(_btnCancel);
            buttonBar.Resize += (s, e) => LayoutButtons(buttonBar);

            // Z-order: fill first, then docked edges (last added = outermost for Top/Bottom)
            Controls.Add(_contentPanel);
            Controls.Add(footerRule);
            Controls.Add(buttonBar);
            Controls.Add(headerRule);
            Controls.Add(header);

            AcceptButton = _btnNext;
            CancelButton = _btnCancel;

            var general = CreateHost(new OptionsPageHost());
            var context = CreateHost(new ContextOptionsPageHost());
            var buildErrors = CreateHost(new BuildErrorHandlingOptionsPageHost());
            var indexing = CreateHost(new IndexingOptionsPageHost());
            indexing.IndexNowClicked += OnIndexNow;
            indexing.ClearClicked += OnClearIndex;

            _hosts = new Control[]
            {
                general,
                context,
                buildErrors,
                indexing
            };

            _stepTitles = new[]
            {
                "General",
                "Context",
                "Build Error Handling",
                "Indexing"
            };

            general.LoadFromConfig();
            context.LoadFromConfig();
            buildErrors.LoadFromConfig();
            indexing.LoadFromConfig();

            foreach (Control host in _hosts)
            {
                host.Visible = false;
                _contentPanel.Controls.Add(host);
            }

            _stepIndex = 0;
            ShowStep(0);
            LayoutButtons(buttonBar);
        }

        private static T CreateHost<T>(T host) where T : ScrollableControl
        {
            // Fill the content area; host AutoScroll (from InitLayout) only appears when needed.
            host.Dock = DockStyle.Fill;
            host.AutoScroll = true;
            host.MinimumSize = Size.Empty;
            host.AutoSize = false;
            return host;
        }

        /// <summary>
        /// Shows the onboarding wizard if config is missing. No-op when already configured or already open.
        /// </summary>
        /// <param name="isUserEntryPoint">
        /// True when called from a menu/tool-window action (resets cancel suppression).
        /// False for nested calls such as <see cref="LLMClient.CreateFromConfig"/>.
        /// </param>
        public static void ShowIfNeeded(bool isUserEntryPoint = false)
        {
            if (isUserEntryPoint)
                _suppressUntilNextEntry = false;

            if (!ConfigHandler.NeedsOnboarding || _isShowing || _suppressUntilNextEntry)
                return;

            _isShowing = true;
            try
            {
                using (OnboardingWizardForm form = new OnboardingWizardForm())
                {
                    form.ShowDialog();
                }

                // If the user cancelled, avoid immediately re-showing from nested callers.
                if (ConfigHandler.NeedsOnboarding)
                    _suppressUntilNextEntry = true;
            }
            finally
            {
                _isShowing = false;
            }
        }

        private void LayoutButtons(Panel buttonBar)
        {
            const int gap = 6;
            const int margin = 10;
            int y = (buttonBar.ClientSize.Height - _btnNext.Height) / 2;
            int right = buttonBar.ClientSize.Width - margin;

            _btnCancel.Location = new Point(right - _btnCancel.Width, y);
            right = _btnCancel.Left - gap;
            _btnNext.Location = new Point(right - _btnNext.Width, y);
            // Classic wizard: Back sits flush against Next with a small gap
            right = _btnNext.Left - 2;
            _btnBack.Location = new Point(right - _btnBack.Width, y);
        }

        private void ShowStep(int index)
        {
            for (int i = 0; i < _hosts.Length; i++)
                _hosts[i].Visible = (i == index);

            _stepIndex = index;
            _lblTitle.Text = string.Format("{0} ({1} of {2})", _stepTitles[index], index + 1, _hosts.Length);
            _btnBack.Enabled = index > 0;
            _btnNext.Text = (index == _hosts.Length - 1) ? "Finish" : "Next >";

            // Keep Next/Finish as the Enter default (focus on Cancel would otherwise steal Enter).
            AcceptButton = _btnNext;
            _btnNext.NotifyDefault(true);
            _btnCancel.NotifyDefault(false);
            _btnBack.NotifyDefault(false);

            if (IsHandleCreated)
                SelectNextControl(_hosts[index], true, true, true, false);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            AcceptButton = _btnNext;
            _btnNext.NotifyDefault(true);
            // Prefer the first field on the step; Enter still activates AcceptButton (Next).
            Control host = _hosts[_stepIndex];
            SelectNextControl(host, true, true, true, false);
        }

        private void OnBackClick(object sender, EventArgs e)
        {
            if (_stepIndex > 0)
                ShowStep(_stepIndex - 1);
        }

        private void OnNextClick(object sender, EventArgs e)
        {
            if (_stepIndex < _hosts.Length - 1)
            {
                ShowStep(_stepIndex + 1);
                return;
            }

            FinishAndSave();
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void FinishAndSave()
        {
            ((OptionsPageHost)_hosts[0]).SaveToConfig();
            ((ContextOptionsPageHost)_hosts[1]).SaveToConfig();
            ((BuildErrorHandlingOptionsPageHost)_hosts[2]).SaveToConfig();
            ((IndexingOptionsPageHost)_hosts[3]).SaveToConfig();

            ConfigHandler.SaveConfig();
            ConfigHandler.ReloadConfig();
            ConfigHandler.CompleteOnboarding();

            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnIndexNow()
        {
            ((IndexingOptionsPageHost)_hosts[3]).SaveToConfig();
            ConfigHandler.SaveConfig();
            ConfigHandler.ReloadConfig();
            CodebaseIndexer.RequestFullIndex();
        }

        private void OnClearIndex()
        {
            CodebaseIndexer.RequestClearIndex();
        }
    }
}
