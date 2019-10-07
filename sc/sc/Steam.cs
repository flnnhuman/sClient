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
        public static readonly string CacheDir = FileSystem.CacheDirectory;
        public static readonly string MainDir = FileSystem.AppDataDirectory;

    }
}