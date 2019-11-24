using System;
using System.Collections.Generic;
using System.Linq;
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
        public IList<Friend> FriendList { get; private set; }
       
        public Friends()
        {
            InitializeComponent();
            FriendList=new List<Friend>();
            for (int index = 0; index < sc.bot.SteamFriends.GetFriendCount(); index++)
            { 
                if(sc.bot.SteamFriends.GetFriendRelationship(sc.bot.SteamFriends.GetFriendByIndex(index))==EFriendRelationship.Friend) 
                    FriendList.Add(new Friend(sc.bot.SteamFriends.GetFriendByIndex(index)));
            }
            BindingContext = this;
            
        }
        
        void OnListViewItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            Friend selectedItem = e.SelectedItem as Friend;
        }

        void OnListViewItemTapped(object sender, ItemTappedEventArgs e)
        {
            Friend tappedItem = e.Item as Friend;
        }

       
        public static string ByteArrayToHexString(byte[] Bytes)
        {
            StringBuilder Result = new StringBuilder(Bytes.Length * 2);
            string HexAlphabet = "0123456789ABCDEF";

            foreach (byte B in Bytes)
            {
                Result.Append(HexAlphabet[(int)(B >> 4)]);
                Result.Append(HexAlphabet[(int)(B & 0xF)]);
            }

            return Result.ToString();
        } 
    }
   
    public class Friend
    {
        public string Nickname{ get; set; }
        public EPersonaState OnlineStatus{ get; set; }
        public SteamID steamID;
        public byte[] avatarHash;
        public string avatarUrl{ get; set; }
       
        public EFriendRelationship Relationship;
        public Friend(SteamID steamID)
        {
               
            this.steamID = steamID;
            Nickname = sc.bot.SteamFriends.GetFriendPersonaName(steamID);
            OnlineStatus = sc.bot.SteamFriends.GetPersonaState();
            avatarHash  = sc.bot.SteamFriends.GetFriendAvatar(steamID);
            avatarUrl = "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/avatars/" + Friends.ByteArrayToHexString(avatarHash).Substring(0, 2).ToLower() + "/" + Friends.ByteArrayToHexString(avatarHash).ToLower() + "_full.jpg";
            Relationship = sc.bot.SteamFriends.GetFriendRelationship(steamID);
        }
    }
}