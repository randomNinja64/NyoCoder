using System;
using System.Windows.Forms;

namespace NyoCoder
{
	public partial class OptionsForm : Form
	{
		private ConfigHandler configHandler;

		public OptionsForm()
		{
			InitializeComponent();
			configHandler = new ConfigHandler();
			LoadConfig();
		}

		private void LoadConfig()
		{
			ApiKey = configHandler.GetApiKey();
			LlmServer = configHandler.GetLlmServer();
			Model = configHandler.GetModel();
		}

		private void SaveConfig()
		{
			configHandler.SetApiKey(ApiKey);
			configHandler.SetLlmServer(LlmServer);
			configHandler.SetModel(Model);
			configHandler.SaveConfig();
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

		private void btnOK_Click(object sender, EventArgs e)
		{
			SaveConfig();
			this.DialogResult = DialogResult.OK;
		}
	}
}
