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


        private async void Button_OnClicked(object sender, EventArgs e)
        {
        }

        private void Button2_OnClicked(object sender, EventArgs e)
        {
        }

        private async void Button3_OnClicked(object sender, EventArgs e)
        {
        }
    }
}