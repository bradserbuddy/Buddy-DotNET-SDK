using BuddySDK;
using System;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace PushSampleUniversal
{
    public partial class LoginPage : Page
    {
        public LoginPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameInput.Text.Trim();
            var password = PasswordInput.Password.Trim();
            StatusMsg.Text = String.Empty;

            var result = await Buddy.LoginUserAsync(username, password);
            if (result.IsSuccess)
            {
                await new MessageDialog(String.Format("Logged in as {0}", result.Value.Username)).ShowAsync();
                this.Frame.Navigate(typeof(ChatScreen));
            }
            else
            {
                StatusMsg.Text = result.Error.Message;
            }
        }
    }
}