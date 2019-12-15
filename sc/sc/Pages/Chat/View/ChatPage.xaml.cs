using System;
using sc.Chat.ViewModel;
using SteamKit2;
using Xamarin.Forms;

namespace sc.Chat.View
{
    public partial class ChatPage : ContentPage
    {
        public ChatPageViewModel vm;
        public ChatPage(SteamID friendSteamID)
        {
            InitializeComponent();
            Title = sc.bot.SteamFriends.GetFriendPersonaName(friendSteamID);
            BindingContext = vm = new ChatPageViewModel(friendSteamID);
            

             vm.ListMessages.CollectionChanged += (sender, e) =>
             {
                 var target = vm.ListMessages[vm.ListMessages.Count - 1];
                 MessagesListView.ScrollTo(target, ScrollToPosition.End, true);
             };
        }
        
    }
}
