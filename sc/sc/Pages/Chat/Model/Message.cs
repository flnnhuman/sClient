using System;
using MvvmHelpers;
using SteamKit2;

namespace sc.Chat.Model
{
    public class Message : ObservableObject
    {
        public string Text
        {
            get { return _text; }
            set { SetProperty(ref _text, value); }
        }
        string _text;

        public DateTime MessageDateTime
        {
            get { return _messageDateTime; }
            set { SetProperty(ref _messageDateTime, value); }
        }

        DateTime _messageDateTime;

        public string TimeDisplay => MessageDateTime.ToLocalTime().ToString();

        public bool IsTextIn
        {
            get { return _isTextIn; }
            set { SetProperty(ref _isTextIn, value); }
        }
        bool _isTextIn;

        public Message(SteamFriends.FriendMsgHistoryCallback.FriendMessage friendMessage)
        {
            Text = friendMessage.Message;
            MessageDateTime = friendMessage.Timestamp;
            IsTextIn = sc.bot.SteamID != friendMessage.SteamID;
        }
        public Message(string text,DateTime messageDateTime,bool isTextIn )
        {
            Text = text;
            MessageDateTime =messageDateTime;
            IsTextIn = isTextIn;
        }
        
    }
}
