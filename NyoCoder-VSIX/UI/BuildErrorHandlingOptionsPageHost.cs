using System;
using System.Drawing;
using System.Windows.Forms;

namespace NyoCoder
{
    public class BuildErrorHandlingOptionsPageHost : OptionsPageHostBase
    {
        private RadioButton rbIntelliSense;
        private RadioButton rbBuildSolution;
        private RadioButton rbOff;

        private NumericUpDown numWaitSeconds;
        private NumericUpDown numMaxAttempts;

        private Label lblWaitSeconds;

        public BuildErrorHandlingOptionsPageHost()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            InitLayout(280);

            rbIntelliSense = new RadioButton
            {
                AutoSize = true,
                Text = "IntelliSense (wait for IntelliSense errors)"
            };
            rbBuildSolution = new RadioButton
            {
                AutoSize = true,
                Text = "Build solution (read errors from build output)"
            };
            rbOff = new RadioButton
            {
                AutoSize = true,
                Text = "Off"
            };

            rbIntelliSense.CheckedChanged += ModeChanged;
            rbBuildSolution.CheckedChanged += ModeChanged;
            rbOff.CheckedChanged += ModeChanged;

            FlowLayoutPanel modePanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0)
            };
            modePanel.Controls.Add(rbIntelliSense);
            modePanel.Controls.Add(rbBuildSolution);
            modePanel.Controls.Add(rbOff);

            lblWaitSeconds = new Label { AutoSize = true, Text = "IntelliSense wait (seconds):" };
            numWaitSeconds = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 120,
                Increment = 1,
                Width = 100
            };

            Label lblMaxAttempts = new Label { AutoSize = true, Text = "Max repair attempts:" };
            numMaxAttempts = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 20,
                Width = 100
            };

            AddRow(MakeSectionTitle("Build Error Handling"), new Padding(0, 0, 0, 8), false);

            AddRow(MakeSectionTitle("Detection mode:"), new Padding(0, 0, 0, 4), false);
            AddRow(modePanel, new Padding(0, 0, 0, 12), false);

            AddRow(lblWaitSeconds, new Padding(0, 0, 0, 4), true);
            AddRow(numWaitSeconds, new Padding(0, 0, 0, 8), false);
            AddRow(lblMaxAttempts, new Padding(0, 0, 0, 4), true);
            AddRow(numMaxAttempts, new Padding(0, 0, 0, 0), false);

            this.ResumeLayout(false);
            this.PerformLayout();
            UpdateWrappingWidths();
            UpdateWaitEnabled();
        }

        public BuildErrorCheckMode Mode
        {
            get
            {
                if (rbOff.Checked) return BuildErrorCheckMode.Off;
                if (rbBuildSolution.Checked) return BuildErrorCheckMode.BuildSolution;
                return BuildErrorCheckMode.IntelliSense;
            }
            set
            {
                switch (value)
                {
                    case BuildErrorCheckMode.Off:
                        rbOff.Checked = true;
                        break;
                    case BuildErrorCheckMode.BuildSolution:
                        rbBuildSolution.Checked = true;
                        break;
                    default:
                        rbIntelliSense.Checked = true;
                        break;
                }
                UpdateWaitEnabled();
            }
        }

        public int WaitSeconds
        {
            get { return (int)numWaitSeconds.Value; }
            set { numWaitSeconds.Value = Math.Max((int)numWaitSeconds.Minimum, Math.Min((int)numWaitSeconds.Maximum, value)); }
        }

        public int MaxAttempts
        {
            get { return (int)numMaxAttempts.Value; }
            set { numMaxAttempts.Value = Math.Max((int)numMaxAttempts.Minimum, Math.Min((int)numMaxAttempts.Maximum, value)); }
        }

        private void ModeChanged(object sender, EventArgs e)
        {
            UpdateWaitEnabled();
        }

        private void UpdateWaitEnabled()
        {
            bool intelliSense = rbIntelliSense.Checked;
            lblWaitSeconds.Enabled = intelliSense;
            numWaitSeconds.Enabled = intelliSense;
        }

        public void LoadFromConfig()
        {
            Mode = ConfigHandler.GetBuildErrorCheckMode();
            WaitSeconds = ConfigHandler.GetBuildErrorCheckWaitSeconds();
            MaxAttempts = ConfigHandler.GetBuildErrorFixMaxAttempts();
        }

        public void SaveToConfig()
        {
            ConfigHandler.SetBuildErrorCheckMode(Mode);
            ConfigHandler.SetBuildErrorCheckWaitSeconds(WaitSeconds);
            ConfigHandler.SetBuildErrorFixMaxAttempts(MaxAttempts);
        }
    }
}
