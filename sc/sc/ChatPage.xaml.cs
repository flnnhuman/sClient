using SteamKit2;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace sc
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ChatPage : ContentPage
    {
        public ChatPage(SteamID steamID)
        {
            InitializeComponent();
            sc.bot.SteamFriends.RequestMessageHistory(steamID);
        }
    }
}