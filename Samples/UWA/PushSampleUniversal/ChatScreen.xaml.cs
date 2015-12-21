using BuddySDK;
using BuddySDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Core;

namespace PushSampleUniversal
{
    public partial class ChatScreen : Page
    {
        private string Recipient { get; set; }

        public ChatScreen()
        {
            InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            Buddy.AuthorizationNeedsUserLogin += Buddy_AuthorizationNeedsUserLogin;

            //Buddy.RecordNotificationReceived(e.Content);

            await LoadUsers();
        }

        private async Task LoadUsers()
        {
            var user = await Buddy.GetCurrentUserAsync();

            if (user != null)
            {
                userList.ItemsSource = App.BuddyUsers;
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            DisableBuddyAuthLoginUI();
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            base.OnNavigatingFrom(e);

            DisableBuddyAuthLoginUI();
        }

        private void DisableBuddyAuthLoginUI()
        {
            Buddy.AuthorizationNeedsUserLogin -= Buddy_AuthorizationNeedsUserLogin;
        }

        public void DisplaySend(object sender, RoutedEventArgs args)
        {
            string sendId = (sender as Button).Tag as string;
            Recipient = sendId;

            MessagePopup.IsOpen = true;
        }

        public async void SendMessage(object sender, RoutedEventArgs args)
        {
            MessagePopup.IsOpen = false;

            var user = await Buddy.GetCurrentUserAsync();

            await new MessageDialog(String.Format("Sending to {0}", Recipient), "Sending...").ShowAsync();


            var toast =
           @"<? xml version=""1.0"" encoding=""utf-8""?>
<wp:Notification xmlns:wp =""WPNotification"">
<wp:Toast>
<wp:Text1>string1</wp:Text1>
<wp:Text2>string2</wp:Text2>
<wp:Param>/ChatScreen.xaml?NavigatedFrom=Toast Notification</wp:Param>
</wp:Toast>
</wp:Notification>";

            var tile =
                @"<? xml version=""1.0"" encoding=""utf-8""?>
<wp:Notification xmlns:wp =""WPNotification"">
<wp:Tile>
<wp:Count>2</wp:Count>
<wp:Title>New Title</ wp:Title>
</wp:Tile>
</wp:Notification>";

            var badge =
    @"<? xml version=""1.0"" encoding=""utf-8""?>
<wp:Notification xmlns:wp =""WPNotification"">
<wp:Badge value = ""alarm"">
</wp:Badge>
</wp:Notification>";

            //osCustomData["MPNS"] = "<wp:Notification xmlns:wp=\"WPNotification\"><wp:Toast><wp:Text1>From WP</wp:Text1><wp:Text2>From WP!</wp:Text2></wp:Toast></wp:Notification>";


            var result = await Buddy.PostAsync<NotificationResult>("/notifications", new
            {
                recipients = new string[] { Recipient },
                //pushType = "Raw",
                //payload = "my payload",
                title = String.Format("Message from {0}", user.FirstName ?? user.Username),
                message = MessageBody.Text
            });
            
            if (result.IsSuccess)
            {
                var pushedAggregate = result.Value.SentByPlatform.Aggregate<KeyValuePair<string, int>, int>(0, (agg, pushType) => agg + pushType.Value);

                if (pushedAggregate < 1)
                {
                    await new MessageDialog("This person isn't logged into any devices that support chat").ShowAsync();
                }
                else
                {
                    await new MessageDialog("Sent").ShowAsync();
                }
            }
        }

        private void Buddy_AuthorizationNeedsUserLogin(object sender, EventArgs e)
        {
            // Needed because we can't call Frame.Navigate() from within an OnNavigatedTo() call chain.
            Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var x = Frame.Navigate(typeof(LoginPage));
            });
        }
    }
}