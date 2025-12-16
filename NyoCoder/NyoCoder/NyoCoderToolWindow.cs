using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NyoCoder
{
	/// <summary>
	/// UserControl that hosts the NyoCoder output pane content.
	/// This control is hosted inside a Visual Studio tool window.
	/// </summary>
	[ComVisible(true)]
	[ProgId("NyoCoder.NyoCoderToolWindow")]
	[Guid("8D3A5B2C-9F4E-4A1D-B7C6-3E8F2D1A5B9C")]
	public partial class NyoCoderToolWindow : UserControl
	{
		private RichTextBox _outputTextBox;
		private FlowLayoutPanel _buttonPanel;
		private Panel _contentPanel;

		public NyoCoderToolWindow()
		{
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			this.SuspendLayout();

			// Main content panel
			_contentPanel = new Panel
			{
				Dock = DockStyle.Fill,
				BackColor = SystemColors.Window,
				Padding = new Padding(4)
			};

			// Output text box
			_outputTextBox = new RichTextBox
			{
				Dock = DockStyle.Fill,
				ReadOnly = true,
				BackColor = SystemColors.Window,
				ForeColor = SystemColors.WindowText,
				BorderStyle = BorderStyle.None,
				Font = new Font("Consolas", 9.75F, FontStyle.Regular),
				Text = "NyoCoder output will appear here..."
			};

			// Button panel for action buttons
			_buttonPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Bottom,
				Height = 36,
				Padding = new Padding(2),
				AutoSize = false,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false
			};

			_contentPanel.Controls.Add(_outputTextBox);
			_contentPanel.Controls.Add(_buttonPanel);

			this.Controls.Add(_contentPanel);
			this.Name = "NyoCoderToolWindow";
			this.Size = new Size(300, 200);

			this.ResumeLayout(false);
		}

		/// <summary>
		/// Appends text to the output pane.
		/// </summary>
		public void AppendText(string text)
		{
			if (_outputTextBox.InvokeRequired)
			{
				_outputTextBox.Invoke(new Action(() => AppendText(text)));
				return;
			}

			_outputTextBox.AppendText(text);
			_outputTextBox.ScrollToCaret();
		}

		/// <summary>
		/// Appends a line of text to the output pane.
		/// </summary>
		public void AppendLine(string text)
		{
			AppendText(text + Environment.NewLine);
		}

		/// <summary>
		/// Clears all text from the output pane.
		/// </summary>
		public void ClearOutput()
		{
			if (_outputTextBox.InvokeRequired)
			{
				_outputTextBox.Invoke(new Action(ClearOutput));
				return;
			}

			_outputTextBox.Clear();
		}

		/// <summary>
		/// Sets the output text, replacing any existing content.
		/// </summary>
		public void SetOutput(string text)
		{
			if (_outputTextBox.InvokeRequired)
			{
				_outputTextBox.Invoke(new Action(() => SetOutput(text)));
				return;
			}

			_outputTextBox.Text = text;
		}

		/// <summary>
		/// Adds an action button to the button panel.
		/// </summary>
		public Button AddButton(string text, EventHandler clickHandler)
		{
			if (_buttonPanel.InvokeRequired)
			{
				return (Button)_buttonPanel.Invoke(new Func<Button>(() => AddButton(text, clickHandler)));
			}

			var button = new Button
			{
				Text = text,
				AutoSize = true,
				Margin = new Padding(2)
			};

			if (clickHandler != null)
			{
				button.Click += clickHandler;
			}

			_buttonPanel.Controls.Add(button);
			_buttonPanel.Visible = true;

			return button;
		}

		/// <summary>
		/// Clears all buttons from the button panel.
		/// </summary>
		public void ClearButtons()
		{
			if (_buttonPanel.InvokeRequired)
			{
				_buttonPanel.Invoke(new Action(ClearButtons));
				return;
			}

			_buttonPanel.Controls.Clear();
		}

		/// <summary>
		/// Shows or hides the button panel.
		/// </summary>
		public void SetButtonPanelVisible(bool visible)
		{
			if (_buttonPanel.InvokeRequired)
			{
				_buttonPanel.Invoke(new Action(() => SetButtonPanelVisible(visible)));
				return;
			}

			_buttonPanel.Visible = visible;
		}
	}
}
