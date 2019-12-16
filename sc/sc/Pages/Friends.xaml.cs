using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using SteamKit2;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using System.Runtime.CompilerServices;
using MvvmHelpers;
using sc.Chat.View;

namespace sc
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Friends : ContentPage
    {
        public ChatPage CurrentChatPage;
        public Friends()
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(sc.GlobalConfig.Language);
            InitializeComponent();
            FriendList = new ObservableRangeCollection<Friend>();
            BindingContext = this;
        }

        public ObservableRangeCollection<Friend> FriendList { get; set; }

        private void OnListViewItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            var selectedItem = e.SelectedItem as Friend;
        }

        private async void OnListViewItemTapped(object sender, ItemTappedEventArgs e)
        {
            var tappedItem = e.Item as Friend;
            sc.bot.SteamFriends.RequestMessageHistory(tappedItem.steamID);
            for (var i = 0; i < 10; i++)
            {
                if (sc.MsgHistory?.SteamID == tappedItem.steamID) break;
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            foreach (SteamFriends.FriendMsgHistoryCallback.FriendMessage message in sc.MsgHistory.Messages)
                sc.Logger.LogChatMessage(false, message.Message, steamID: message.SteamID);

            CurrentChatPage = new ChatPage(tappedItem.steamID);
            await sc.Mainpage1.Detail.Navigation.PushAsync(CurrentChatPage);
        }
    }

    public class Friend : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged = delegate { };
        public EFriendRelationship Relationship;
        public SteamID steamID;
        public byte[] AvatarHash { get; set; }
        public string Nickname { get {return nickname; }
            set
            {
                nickname = value;
                PropertyChanged(this, new PropertyChangedEventArgs("Nickname"));
            }
        }
        private string nickname;
        public EPersonaState OnlineStatus
        {
            get { return onlineStatus;}
            set
            {
                onlineStatus = value;
                PropertyChanged(this, new PropertyChangedEventArgs("OnlineStatus"));  
            } 
        }
        private EPersonaState onlineStatus;
        public string AvatarUrl { get; set; }
        
        public Friend(SteamID steamID)
        {
            this.steamID = steamID;
            Nickname = sc.bot.SteamFriends.GetFriendPersonaName(steamID);
            OnlineStatus = sc.bot.SteamFriends.GetPersonaState();
            AvatarHash = sc.bot.SteamFriends.GetFriendAvatar(steamID);
            if (AvatarHash!=null)
            {
                AvatarUrl = "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/avatars/" +
                            Utilities.ByteArrayToHexString(AvatarHash).Substring(0, 2).ToLower() + "/" +
                            Utilities.ByteArrayToHexString(AvatarHash).ToLower() + "_full.jpg";  
            }
           
            
            
        }
        public Friend(SteamID steamID,byte[] avatarHash, string nickname,EPersonaState state)
        {
            this.steamID = steamID;
            Nickname = nickname;
            OnlineStatus = state;
            AvatarHash = avatarHash;
            if (avatarHash!=null)
            {
                AvatarUrl = "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/avatars/" +
                            Utilities.ByteArrayToHexString(avatarHash).Substring(0, 2).ToLower() + "/" +
                            Utilities.ByteArrayToHexString(avatarHash).ToLower() + "_full.jpg";  
            }
            
            
        }

     
    }
}