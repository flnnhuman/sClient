using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Acr.UserDialogs;
using SteamKit2;
using Xamarin.Essentials;
using Xamarin.Forms;
using static sc.MainPage;

namespace sc    
{
    public static class Steam
    {
        public enum EToastType
        {
            Message,
            Warning,
            Error,
            Success
            
        };
        
        public static SteamClient SteamClient;
        public static CallbackManager Manager; 
        //public static readonly string CacheDir = FileSystem.CacheDirectory;
        //public static readonly string MainDir = FileSystem.AppDataDirectory;
        public static readonly string MainDir = FileSystem.CacheDirectory;
        
        public static SteamUser SteamUser;

        public static bool IsRunning;

        public static string User, Pass;
        private static string _authCode, _twoFactorAuth;
        
        public static void OnConnected(SteamClient.ConnectedCallback callback)
        {
            Toast($"Connected to Steam! Logging in '{User}'...",EToastType.Message);
            
                byte[] sentryHash = null;
                if (File.Exists(Path.Combine(MainDir,"sentry.bin")))
                {
                    byte[] sentryFile = File.ReadAllBytes(Path.Combine(MainDir, "sentry.bin"));
                    sentryHash = CryptoHelper.SHAHash(sentryFile);
                }

                SteamUser.LogOn(new SteamUser.LogOnDetails
                {
                    Username = User,
                    Password = Pass,
                    AuthCode = _authCode,
                    TwoFactorCode = _twoFactorAuth,
                    SentryFileHash = sentryHash
                });
            
        }

        public static void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            
            Toast("Disconnected from Steam",EToastType.Error);
        }

        public static async void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            switch (callback.Result)
            {
                case EResult.AccountLogonDenied: 
                    Toast($"Please enter the auth code sent to the email at {callback.EmailDomain}",EToastType.Message);
                    PromptResult result = await UserDialogs.Instance.PromptAsync(new PromptConfig
                    {
                        InputType = InputType.Name,
                        OkText = "input",
                        Title = "Enter Guard code",
                    });
                    _authCode = result.Text;
                    ThreadPool.QueueUserWorkItem(o => { SteamClient.Connect(); });
                    break;
                case EResult.AccountLoginDeniedNeedTwoFactor:Toast("Please enter your 2 factor auth code from your authenticator app: ",EToastType.Message);


                    PromptResult pResult = await UserDialogs.Instance.PromptAsync(new PromptConfig
                    {
                        InputType = InputType.Name,
                        OkText = "input",
                        Title = "Enter Guard code",
                    });
                    
                    _twoFactorAuth = pResult.Text;
                    ThreadPool.QueueUserWorkItem(o => { SteamClient.Connect(); });
                    return;
            }

            if (callback.Result != EResult.OK)
            {
                Toast($"Unable to logon to Steam: {callback.Result} / {callback.ExtendedResult}",EToastType.Error);
                IsRunning = false;
                return;
            }

            Toast("Successfully logged on!",EToastType.Success);
            //Device.BeginInvokeOnMainThread(() => { Application.Current.MainPage = new second_page(); });
        }

        public static void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Toast($"Logged off of Steam: {callback.Result}",EToastType.Success);
        }

        public static void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback)
        {
            
            Toast("Updating sentry file...",EToastType.Message);
            
                int fileSize;
                byte[] sentryHash;
                using (var fs = File.Open(Path.Combine(MainDir, "sentry.bin"), FileMode.OpenOrCreate, FileAccess.ReadWrite))
                {
                    fs.Seek(callback.Offset, SeekOrigin.Begin);
                    fs.Write(callback.Data, 0, callback.BytesToWrite);
                    fileSize = (int) fs.Length;

                    fs.Seek(0, SeekOrigin.Begin);
                    using (var sha = SHA1.Create())
                    {
                        sentryHash = sha.ComputeHash(fs);
                    }
                    fs.Close();
                }

                SteamUser.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
                {
                    JobID = callback.JobID,

                    FileName = callback.FileName,

                    BytesWritten = callback.BytesToWrite,
                    FileSize = fileSize,
                    Offset = callback.Offset,

                    Result = EResult.OK,
                    LastError = 0,

                    OneTimePassword = callback.OneTimePassword,

                    SentryFileHash = sentryHash,
                });
            
            Toast("Done!",EToastType.Success);
        }
    }
}