using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SteamKit2;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace sc
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class Friends : ContentPage
    {
        public Friends()
        {
            InitializeComponent();
            FriendList = new List<Friend>();
            FriendCycle:
            if (sc.bot == null)
            {
                Task.Delay(TimeSpan.FromSeconds(1));
                goto FriendCycle;
            }

            for (var index = 0; index < sc.bot.SteamFriends.GetFriendCount(); index++)
                if (sc.bot.SteamFriends.GetFriendRelationship(sc.bot.SteamFriends.GetFriendByIndex(index)) ==
                    EFriendRelationship.Friend)
                    FriendList.Add(new Friend(sc.bot.SteamFriends.GetFriendByIndex(index)));
            BindingContext = this;
        }

        public IList<Friend> FriendList { get; }

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
        }


        public static string ByteArrayToHexString(byte[] Bytes)
        {
            var Result = new StringBuilder(Bytes.Length * 2);
            var HexAlphabet = "0123456789ABCDEF";

            foreach (var B in Bytes)
            {
                Result.Append(HexAlphabet[B >> 4]);
                Result.Append(HexAlphabet[B & 0xF]);
            }

            return Result.ToString();
        }
    }

    public class Friend
    {
        public byte[] avatarHash;

        public EFriendRelationship Relationship;
        public SteamID steamID;

        public Friend(SteamID steamID)
        {
            this.steamID = steamID;
            Nickname = sc.bot.SteamFriends.GetFriendPersonaName(steamID);
            OnlineStatus = sc.bot.SteamFriends.GetPersonaState();
            avatarHash = sc.bot.SteamFriends.GetFriendAvatar(steamID);
            avatarUrl = "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/avatars/" +
                        Friends.ByteArrayToHexString(avatarHash).Substring(0, 2).ToLower() + "/" +
                        Friends.ByteArrayToHexString(avatarHash).ToLower() + "_full.jpg";
            Relationship = sc.bot.SteamFriends.GetFriendRelationship(steamID);
        }

        public string Nickname { get; }
        public EPersonaState OnlineStatus { get; }
        public string avatarUrl { get; }
    }
}