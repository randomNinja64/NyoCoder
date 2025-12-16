namespace NyoCoder
{
	partial class OptionsForm
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.lblTitle = new System.Windows.Forms.Label();
			this.lblApiKey = new System.Windows.Forms.Label();
			this.txtApiKey = new System.Windows.Forms.TextBox();
			this.lblLlmServer = new System.Windows.Forms.Label();
			this.txtLlmServer = new System.Windows.Forms.TextBox();
			this.lblModel = new System.Windows.Forms.Label();
			this.txtModel = new System.Windows.Forms.TextBox();
			this.btnOK = new System.Windows.Forms.Button();
			this.btnCancel = new System.Windows.Forms.Button();
			this.SuspendLayout();
			// 
			// lblTitle
			// 
			this.lblTitle.AutoSize = true;
			this.lblTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.lblTitle.Location = new System.Drawing.Point(12, 9);
			this.lblTitle.Name = "lblTitle";
			this.lblTitle.Size = new System.Drawing.Size(59, 17);
			this.lblTitle.TabIndex = 0;
			this.lblTitle.Text = "Options:";
			// 
			// lblApiKey
			// 
			this.lblApiKey.AutoSize = true;
			this.lblApiKey.Location = new System.Drawing.Point(12, 40);
			this.lblApiKey.Name = "lblApiKey";
			this.lblApiKey.Size = new System.Drawing.Size(52, 13);
			this.lblApiKey.TabIndex = 1;
			this.lblApiKey.Text = "API Key:";
			// 
			// txtApiKey
			// 
			this.txtApiKey.Location = new System.Drawing.Point(12, 56);
			this.txtApiKey.Name = "txtApiKey";
			this.txtApiKey.Size = new System.Drawing.Size(360, 20);
			this.txtApiKey.TabIndex = 2;
			this.txtApiKey.UseSystemPasswordChar = true;
			// 
			// lblLlmServer
			// 
			this.lblLlmServer.AutoSize = true;
			this.lblLlmServer.Location = new System.Drawing.Point(12, 90);
			this.lblLlmServer.Name = "lblLlmServer";
			this.lblLlmServer.Size = new System.Drawing.Size(178, 13);
			this.lblLlmServer.TabIndex = 3;
			this.lblLlmServer.Text = "LLM Server (OpenAI Compatible):";
			// 
			// txtLlmServer
			// 
			this.txtLlmServer.Location = new System.Drawing.Point(12, 106);
			this.txtLlmServer.Name = "txtLlmServer";
			this.txtLlmServer.Size = new System.Drawing.Size(360, 20);
			this.txtLlmServer.TabIndex = 4;
			// 
			// lblModel
			// 
			this.lblModel.AutoSize = true;
			this.lblModel.Location = new System.Drawing.Point(12, 140);
			this.lblModel.Name = "lblModel";
			this.lblModel.Size = new System.Drawing.Size(39, 13);
			this.lblModel.TabIndex = 5;
			this.lblModel.Text = "Model:";
			// 
			// txtModel
			// 
			this.txtModel.Location = new System.Drawing.Point(12, 156);
			this.txtModel.Name = "txtModel";
			this.txtModel.Size = new System.Drawing.Size(360, 20);
			this.txtModel.TabIndex = 6;
			// 
			// btnOK
			// 
			this.btnOK.DialogResult = System.Windows.Forms.DialogResult.OK;
			this.btnOK.Location = new System.Drawing.Point(216, 190);
			this.btnOK.Name = "btnOK";
			this.btnOK.Size = new System.Drawing.Size(75, 23);
			this.btnOK.TabIndex = 7;
			this.btnOK.Text = "OK";
			this.btnOK.UseVisualStyleBackColor = true;
			// 
			// btnCancel
			// 
			this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.btnCancel.Location = new System.Drawing.Point(297, 190);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new System.Drawing.Size(75, 23);
			this.btnCancel.TabIndex = 8;
			this.btnCancel.Text = "Cancel";
			this.btnCancel.UseVisualStyleBackColor = true;
			// 
			// OptionsForm
			// 
			this.AcceptButton = this.btnOK;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this.btnCancel;
			this.ClientSize = new System.Drawing.Size(384, 225);
			this.Controls.Add(this.btnCancel);
			this.Controls.Add(this.btnOK);
			this.Controls.Add(this.txtModel);
			this.Controls.Add(this.lblModel);
			this.Controls.Add(this.txtLlmServer);
			this.Controls.Add(this.lblLlmServer);
			this.Controls.Add(this.txtApiKey);
			this.Controls.Add(this.lblApiKey);
			this.Controls.Add(this.lblTitle);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "OptionsForm";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "NyoCoder Options";
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label lblTitle;
		private System.Windows.Forms.Label lblApiKey;
		private System.Windows.Forms.TextBox txtApiKey;
		private System.Windows.Forms.Label lblLlmServer;
		private System.Windows.Forms.TextBox txtLlmServer;
		private System.Windows.Forms.Label lblModel;
		private System.Windows.Forms.TextBox txtModel;
		private System.Windows.Forms.Button btnOK;
		private System.Windows.Forms.Button btnCancel;
	}
}

