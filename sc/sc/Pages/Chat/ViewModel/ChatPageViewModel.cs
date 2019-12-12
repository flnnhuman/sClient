using System;
using System.Collections.Generic;
using System.Windows.Input;
using MvvmHelpers;
using sc.Chat.Model;
using SteamKit2;
using Xamarin.Forms;

namespace sc.Chat.ViewModel
{
    public class ChatPageViewModel : BaseViewModel
    {
        public List<Message> ListMessages { get; }
        public ICommand SendCommand { get; set; }

        private SteamID FriendSteamID;

        public ChatPageViewModel(SteamID friendSteamID)
        {
            FriendSteamID = friendSteamID;
            ListMessages = new List<Message>();
            
            
            foreach (var message in sc.MsgHistory.Messages)
            {
                ListMessages.Add(new Message(message));
            }
            
            SendCommand = new Command(() =>
            {
                if (!String.IsNullOrWhiteSpace(OutText))
                {
                    sc.bot.SteamFriends.SendChatMessage(friendSteamID,EChatEntryType.ChatMsg,OutText);
                    var message = new Message(OutText,DateTime.Now,false);
                    
                    ListMessages.Add(message);
                    OutText = "";
                }

            });
            
        }


        public string OutText
        {
            get { return _outText; }
            set { SetProperty(ref _outText, value); }
        }
        string _outText = string.Empty;
    }
}
