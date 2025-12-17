using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
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

		public NyoCoderToolWindow()
		{
			InitializeComponent();
		}

	private void InitializeComponent()
	{
		this.SuspendLayout();

		// Button panel for action buttons - add to UserControl directly
		_buttonPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			Height = 36,
			Padding = new Padding(2),
			AutoSize = false,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Visible = false // Hidden by default until buttons are added
		};

		// Output text box - add to UserControl directly
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

		// Add controls directly to UserControl in correct order:
		// 1. Button panel docks to bottom first
		// 2. Textbox fills remaining space
		this.Controls.Add(_buttonPanel);
		this.Controls.Add(_outputTextBox);
		
		// Ensure proper z-order: textbox behind, button panel in front
		_outputTextBox.SendToBack();
		_buttonPanel.BringToFront();

		this.Name = "NyoCoderToolWindow";
		this.Size = new Size(300, 200);
		this.Padding = new Padding(4);
		this.BackColor = SystemColors.Window;

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
		// Ensure the caret is at the end and scroll to it
		_outputTextBox.SelectionStart = _outputTextBox.Text.Length;
		_outputTextBox.SelectionLength = 0;
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

	// Synchronization for tool approval
	private ManualResetEvent _approvalWaitHandle;
	private bool _approvalResult;

	/// <summary>
	/// Requests user approval for a tool execution using Yes/No buttons.
	/// This method blocks until the user clicks Yes or No.
	/// Thread-safe: can be called from background threads.
	/// </summary>
	/// <param name="toolName">Name of the tool requesting approval</param>
	/// <param name="arguments">Arguments to display to the user</param>
	/// <returns>True if approved, false if rejected</returns>
	public bool RequestToolApproval(string toolName, string arguments)
	{
		_approvalWaitHandle = new ManualResetEvent(false);
		_approvalResult = false;

		// Show approval UI on the UI thread
		if (this.InvokeRequired)
		{
			this.Invoke(new Action(() => ShowApprovalUI(toolName, arguments)));
		}
		else
		{
			ShowApprovalUI(toolName, arguments);
		}

		// Block until user responds
		_approvalWaitHandle.WaitOne();

		return _approvalResult;
	}

	private void ShowApprovalUI(string toolName, string arguments)
	{
		// Display the approval request in the output
		AppendText("\n[Approval Required] " + toolName + "\n");
		AppendText(arguments + "\n");

		// Clear any existing buttons and add Yes/No
		ClearButtons();

		Button yesButton = new Button
		{
			Text = "Yes",
			AutoSize = true,
			Margin = new Padding(2),
			MinimumSize = new Size(75, 25)
		};
		yesButton.Click += OnApprovalYes;

		Button noButton = new Button
		{
			Text = "No",
			AutoSize = true,
			Margin = new Padding(2),
			MinimumSize = new Size(75, 25)
		};
		noButton.Click += OnApprovalNo;

		_buttonPanel.Controls.Add(yesButton);
		_buttonPanel.Controls.Add(noButton);
		_buttonPanel.Visible = true;
	}

	private void OnApprovalYes(object sender, EventArgs e)
	{
		HideApprovalUI();
		_approvalResult = true;
		_approvalWaitHandle.Set();
	}

	private void OnApprovalNo(object sender, EventArgs e)
	{
		HideApprovalUI();
		_approvalResult = false;
		_approvalWaitHandle.Set();
	}

	private void HideApprovalUI()
	{
		if (_buttonPanel.InvokeRequired)
		{
			_buttonPanel.Invoke(new Action(HideApprovalUI));
			return;
		}

		ClearButtons();
		_buttonPanel.Visible = false;
	}

	}
}
