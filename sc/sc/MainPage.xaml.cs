using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Acr.UserDialogs;
using Plugin.Toast;
using SteamKit2;
using Xamarin.Forms;
using Xamarin.Essentials;
using static sc.Bot;

namespace sc
{
    public partial class MainPage : ContentPage
    {
        private static readonly string CacheDir = FileSystem.CacheDirectory;
        private static readonly string MainDir = FileSystem.AppDataDirectory;

        public MainPage()
        {
            InitializeComponent();
        }

        public enum EToastType
        {
            Message,
            Warning,
            Error,
            Success
        };

        public static void Toast(string msg, EToastType type)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                switch (type)
                {
                    case EToastType.Message:
                        CrossToastPopUp.Current.ShowToastMessage(msg);
                        break;
                    case EToastType.Error:
                        CrossToastPopUp.Current.ShowToastError(msg);
                        break;
                    case EToastType.Warning:
                        CrossToastPopUp.Current.ShowToastWarning(msg);
                        break;
                    case EToastType.Success:
                        CrossToastPopUp.Current.ShowToastSuccess(msg);
                        break;
                }
            });
        }

        private BotConfig BotConfig = new BotConfig();
        private BotDatabase BotDatabase = new BotDatabase(Path.Combine(MainDir, "bot"));

        private async void Button_OnClicked(object sender, EventArgs e)
        {
            ThreadPool.QueueUserWorkItem(o =>
            {
                var bot = new Bot("bot", BotConfig, BotDatabase);
                bot.InitModules().ConfigureAwait(false);
                bot.InitStart();
            });
        }

        private void Button2_OnClicked(object sender, EventArgs e)
        {
        }

        private async void Button3_OnClicked(object sender, EventArgs e)
        {
        }
    }
}