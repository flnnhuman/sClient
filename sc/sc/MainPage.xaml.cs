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
using static sc.Steam;
namespace sc
{
    public partial class MainPage : ContentPage
    {

        public MainPage()
        {
            InitializeComponent();
        }

        
        
        public static void Toast(string msg, EToastType type)
        {
          Device.BeginInvokeOnMainThread(() =>
           {
               switch (type)
               {
                   case EToastType.Message:CrossToastPopUp.Current.ShowToastMessage(msg);
                       break;
                   case EToastType.Error: CrossToastPopUp.Current.ShowToastError(msg);
                       break;
                   case EToastType.Warning: CrossToastPopUp.Current.ShowToastWarning(msg);
                       break;
                   case EToastType.Success:CrossToastPopUp.Current.ShowToastSuccess(msg);
                       break;
               }
           } );
        }

        static void updateManager()
        {
            while (IsRunning)
            {
                Manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }
            
        }

        private async void Button_OnClicked(object sender, EventArgs e)
        {
            Steam.SteamClient = new SteamClient {DebugNetworkListener = new NetHookNetworkListener(MainDir)};

            Manager = new CallbackManager(Steam.SteamClient);
            Steam.SteamUser = Steam.SteamClient.GetHandler<SteamUser>();

            Manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            Manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

            Manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            Manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);

            Manager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);

            IsRunning = true;

            User = Login.Text;
            Pass = Password.Text;
            Toast("Connecting to Steam..",EToastType.Message);
            
            ThreadPool.QueueUserWorkItem(o =>
            { 
                Steam.SteamClient.Connect();
                
            });
            var task = Task.Run((Action) updateManager);
            await task;
        }

        private void Button2_OnClicked(object sender, EventArgs e)
        {
            if (Steam.SteamUser != null)
            {
                Toast(Steam.SteamUser.SteamID.ToString(),EToastType.Message);
               
            }
            else{Toast("login into your account first",EToastType.Warning);}

        }
        private async  void Button3_OnClicked(object sender, EventArgs e)
        { 
            
            bool b = File.Exists(Path.Combine(MainDir, "sentry.bin"));
            using (var fs = File.Open(Path.Combine(MainDir, "sentry.bin"), FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                string fileContents;
                using (StreamReader reader = new StreamReader(fs))
                {
                    fileContents = reader.ReadToEnd();
                }
                Toast(fileContents, EToastType.Success);

            }
        }


    }
}