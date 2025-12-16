using System;
using System.Windows.Forms;

namespace NyoCoder
{
	public partial class OptionsForm : Form
	{
		public OptionsForm()
		{
			InitializeComponent();
		}

		public string ApiKey
		{
			get { return txtApiKey.Text; }
			set { txtApiKey.Text = value; }
		}

		public string LlmServer
		{
			get { return txtLlmServer.Text; }
			set { txtLlmServer.Text = value; }
		}

		public string Model
		{
			get { return txtModel.Text; }
			set { txtModel.Text = value; }
		}
	}
}
