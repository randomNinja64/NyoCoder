using System;
using System.Windows;
using System.Windows.Controls;

namespace NyoCoder.NyoCoder_VSIX
{
    /// <summary>
    /// Interaction logic for OptionsPageControl.xaml
    /// </summary>
    public partial class OptionsPageControl : UserControl
    {
        private OptionsPage optionsPage;

        public OptionsPageControl(OptionsPage page)
        {
            InitializeComponent();
            this.optionsPage = page;
        }

        public string ApiKey
        {
            get { return txtApiKey.Password; }
            set { txtApiKey.Password = value ?? string.Empty; }
        }

        public string LlmServer
        {
            get { return txtLlmServer.Text; }
            set { txtLlmServer.Text = value ?? string.Empty; }
        }

        public string Model
        {
            get { return txtModel.Text; }
            set { txtModel.Text = value ?? string.Empty; }
        }

        private void txtApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (optionsPage != null)
            {
                optionsPage.ApiKey = this.ApiKey;
            }
        }

        private void txtLlmServer_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (optionsPage != null)
            {
                optionsPage.LlmServer = this.LlmServer;
            }
        }

        private void txtModel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (optionsPage != null)
            {
                optionsPage.Model = this.Model;
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure keyboard input works by setting focus
            System.Windows.Input.Keyboard.Focus(txtApiKey);
        }
    }
}
