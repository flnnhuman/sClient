using System;
using sc.Chat.ViewModel;
using SteamKit2;
using Xamarin.Forms;
using SQLite;


namespace sc.Chat.View
{
    public partial class ChatPage : ContentPage
    {
        ChatPageViewModel vm;
        public ChatPage(SteamID friendSteamID)
        {
            InitializeComponent();
            Title = sc.bot.SteamFriends.GetFriendPersonaName(friendSteamID);
            BindingContext = vm = new ChatPageViewModel(friendSteamID);
            

            vm.ListMessages.Value.CollectionChanged += (sender, e) =>
            {
                var target = vm.ListMessages.Value[vm.ListMessages.Value.Count - 1];
                MessagesListView.ScrollTo(target, ScrollToPosition.End, true);
            };
        }
        
    }
}
