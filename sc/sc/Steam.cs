using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Acr.UserDialogs;
using SteamKit2;
using Xamarin.Essentials;
using Xamarin.Forms;
using static sc.MainPage;
using JetBrains.Annotations;

namespace sc
{
    public class Bot
    {
        private static readonly string CacheDir = FileSystem.CacheDirectory;
        private static readonly string MainDir = FileSystem.AppDataDirectory;
        private const ushort MaxMessageLength = 5000; // This is a limitation enforced by Steam
        private const byte MaxTwoFactorCodeFailures = 3;
        public readonly string BotName;
        private const uint LoginID = 1212;
        public readonly Logger Logger;
        
        private static readonly SemaphoreSlim BotsSemaphore = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim LoginRateLimitingSemaphore = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim LoginSemaphore = new SemaphoreSlim(1, 1);
        
        private readonly Timer HeartBeatTimer;
        private byte HeartBeatFailures;
        private Timer ConnectionFailureTimer;

        internal readonly SCHandler SCHandler;
        public ulong SteamID { get; private set; }
        
        public bool KeepRunning { get; private set; }
        
        private bool ReconnectOnUserInitiated;
        public readonly SteamConfiguration SteamConfiguration;
        private readonly SteamClient SteamClient;
        private readonly CallbackManager CallbackManager;
        internal readonly SteamApps SteamApps;
        internal readonly SteamFriends SteamFriends;
        private readonly SteamUser SteamUser;

        internal readonly BotDatabase BotDatabase;
        
        private string AuthCode;
        private string TwoFactorCode;
        private byte TwoFactorCodeFailures;
        
       // internal bool HasMobileAuthenticator => BotDatabase?.MobileAuthenticator != null;

        private const ProtocolTypes DefaultSteamProtocols = ProtocolTypes.All;
        public ProtocolTypes SteamProtocols { get; private set; } = DefaultSteamProtocols;

        
        public BotConfig BotConfig { get; private set; }
        
        public bool IsConnectedAndLoggedOn => SteamClient?.SteamID != null;
        private uint BackingCellID;


        private Bot([NotNull] string botName , [NotNull] BotConfig botConfig , [NotNull] BotDatabase botDatabase)
        {
              if (string.IsNullOrEmpty(botName) || botConfig == null || botDatabase == null)
                  throw new ArgumentNullException(nameof(botName) + " || " + nameof(botConfig) + " || " +
                                                  nameof(botDatabase));

            BotName = botName;
            BotConfig = botConfig;
            BotDatabase = botDatabase;

            //ArchiLogger = new ArchiLogger(botName);

           // if (HasMobileAuthenticator) BotDatabase.MobileAuthenticator.Init(this);

            //ArchiWebHandler = new ArchiWebHandler(this);

            SteamConfiguration = SteamConfiguration.Create(builder =>
                    builder.WithProtocolTypes(SteamProtocols).WithCellID(BotDatabase.CellID)
                /*.WithHttpClientFactory(ArchiWebHandler.GenerateDisposableHttpClient)*/);

            // Initialize
            SteamClient = new SteamClient(SteamConfiguration);

            if (Directory.Exists(MainDir))
            {
                var debugListenerPath = Path.Combine(MainDir, "debug", botName);

                try
                {
                    Directory.CreateDirectory(debugListenerPath);
                    SteamClient.DebugNetworkListener = new NetHookNetworkListener(debugListenerPath);
                }
                catch (Exception e)
                {
                    Logger.LogGenericException(e);
                    
                }
            }

            var steamUnifiedMessages = SteamClient.GetHandler<SteamUnifiedMessages>();

            SCHandler = new SCHandler(Logger, steamUnifiedMessages);
            SteamClient.AddHandler(SCHandler);
            CallbackManager = new CallbackManager(SteamClient);
            CallbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
         //            CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
         //         
         //            SteamApps = SteamClient.GetHandler<SteamApps>();
         //            CallbackManager.Subscribe<SteamApps.GuestPassListCallback>(OnGuestPassList);
         //            CallbackManager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);
         //         
         //            SteamFriends = SteamClient.GetHandler<SteamFriends>();
         //            CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);
         //            CallbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);
         //         
         //            CallbackManager.Subscribe<SteamUnifiedMessages.ServiceMethodNotification>(OnServiceMethod);
         //         
         //            SteamUser = SteamClient.GetHandler<SteamUser>();
         //            CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
         //            CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
         //            CallbackManager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);
         //            CallbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);
         //            CallbackManager.Subscribe<SteamUser.WalletInfoCallback>(OnWalletUpdate);

            //CallbackManager.Subscribe<SCHandler.PlayingSessionStateCallback>(OnPlayingSessionState);
            //CallbackManager.Subscribe<SCHandler.SharedLibraryLockStatusCallback>(OnSharedLibraryLockStatus);
            //CallbackManager.Subscribe<SCHandler.UserNotificationsCallback>(OnUserNotifications);
            //CallbackManager.Subscribe<SCHandler.VanityURLChangedCallback>(OnVanityURLChangedCallback);

            //Actions = new Actions(this);
            //CardsFarmer = new CardsFarmer(this);
            //Commands = new Commands(this);
            //Trading = new Trading(this);


            HeartBeatTimer = new Timer(
                async e => await HeartBeat().ConfigureAwait(false),
                null,
                TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(GlobalConfig.LoginLimiterDelay /** Bots.Count*/), // Delay
                TimeSpan.FromMinutes(1) // Period
            );
        }


        private async void OnConnected(SteamClient.ConnectedCallback callback)
        {
            if (callback == null)
            {
                Logger.LogNullError(nameof(callback));
                

                return;
            }

            HeartBeatFailures = 0;
            ReconnectOnUserInitiated = false;
            StopConnectionFailureTimer();

            Logger.LogGenericInfo(Strings.BotConnected);
            
            
            if (!KeepRunning)
            {
                Logger.LogGenericInfo(Strings.BotDisconnecting);
                Disconnect();

                return;
            }

            string sentryFilePath = Path.Combine(MainDir,"sentry");

            if (string.IsNullOrEmpty(sentryFilePath))
            {
                Logger.LogNullError(nameof(sentryFilePath));

                return;
            }

            byte[] sentryFileHash = null;

            if (File.Exists(sentryFilePath))
                try
                {
                    byte[] sentryFileContent = File.ReadAllBytes(sentryFilePath);
                        sentryFileHash = CryptoHelper.SHAHash(sentryFileContent);
                }
                catch (Exception e)
                {
                    Logger.LogGenericException(e);

                    try
                    {
                        File.Delete(sentryFilePath);
                    }
                    catch
                    {
                        // Ignored, we can only try to delete faulted file at best
                    }
                }

            string loginKey = null;

            if (BotConfig.UseLoginKeys)
            {
                // Login keys are not guaranteed to be valid, we should use them only if we don't have full details available from the user
                if (string.IsNullOrEmpty(BotConfig.SteamPassword) || string.IsNullOrEmpty(AuthCode) &&
                    string.IsNullOrEmpty(TwoFactorCode) /*&& !HasMobileAuthenticator*/)
                {
                    loginKey = BotDatabase.LoginKey;

                }
            }
            else
            {
                // If we're not using login keys, ensure we don't have any saved
                BotDatabase.LoginKey = null;
            }

         // if (!await InitLoginAndPassword(string.IsNullOrEmpty(loginKey)).ConfigureAwait(false))
         // {
         //     Stop();
                                                                // не нужно
         //     return;
         // }

            // Steam login and password fields can contain ASCII characters only, including spaces
            const string nonAsciiPattern = @"[^\u0000-\u007F]+";

            string username = Regex.Replace(BotConfig.SteamLogin, nonAsciiPattern, "",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            string password = BotConfig.DecryptedSteamPassword;

            if (!string.IsNullOrEmpty(password))
                password = Regex.Replace(password, nonAsciiPattern, "",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            Logger.LogGenericInfo(Strings.BotLoggingIn);
            
          //  if (string.IsNullOrEmpty(TwoFactorCode) && HasMobileAuthenticator
          //  ) // We should always include 2FA token, even if it's not required
          //      TwoFactorCode = await BotDatabase.MobileAuthenticator.GenerateToken().ConfigureAwait(false);

            InitConnectionFailureTimer();

            var logOnDetails = new SteamUser.LogOnDetails
            {
                AuthCode = AuthCode,
                CellID = BotDatabase.CellID,
                LoginID = LoginID,
                LoginKey = loginKey,
                Password = password,
                SentryFileHash = sentryFileHash,
                ShouldRememberPassword = BotConfig.UseLoginKeys,
                TwoFactorCode = TwoFactorCode,
                Username = username
            };

            //if (OSType == EOSType.Unknown) OSType = logOnDetails.ClientOSType;

            SteamUser.LogOn(logOnDetails);
        }
        private void StopConnectionFailureTimer() {
            if (ConnectionFailureTimer == null) {
                return;
            }

            ConnectionFailureTimer.Dispose();
            ConnectionFailureTimer = null;
        }
        public static async Task<string> ReadAllTextAsync([NotNull] string path) => System.IO.File.ReadAllText(path);
        private void Disconnect() {
            StopConnectionFailureTimer();
            SteamClient.Disconnect();
        }
        private void InitConnectionFailureTimer() {
            if (ConnectionFailureTimer != null) {
                return;
            }

            ConnectionFailureTimer = new Timer(
                async e => await InitPermanentConnectionFailure().ConfigureAwait(false),
                null,
                TimeSpan.FromMinutes(Math.Ceiling(GlobalConfig.ConnectionTimeout / 30.0)), // Delay
                Timeout.InfiniteTimeSpan // Period
            );
        }
        private static async Task LimitLoginRequestsAsync() {
            if (GlobalConfig.LoginLimiterDelay == 0) {
                await LoginRateLimitingSemaphore.WaitAsync().ConfigureAwait(false);
                LoginRateLimitingSemaphore.Release();

                return;
            }

            await LoginSemaphore.WaitAsync().ConfigureAwait(false);

            try {
                await LoginRateLimitingSemaphore.WaitAsync().ConfigureAwait(false);
                LoginRateLimitingSemaphore.Release();
            } finally {
                Utilities.InBackground(
                    async () => {
                        await Task.Delay(GlobalConfig.LoginLimiterDelay * 1000).ConfigureAwait(false);
                        LoginSemaphore.Release();
                    }
                );
            }
        }
        private async Task InitPermanentConnectionFailure() {
            if (!KeepRunning) {
                return;
            }
            
            Logger.LogGenericError(Strings.BotHeartBeatFailed);
            //await Destroy(true).ConfigureAwait(false);
           // await RegisterBot(BotName).ConfigureAwait(false);
        }
        
        private async Task Connect(bool force = false) {
            if (!force && (!KeepRunning || SteamClient.IsConnected)) {
                return;
            }

            await LimitLoginRequestsAsync().ConfigureAwait(false);

            if (!force && (!KeepRunning || SteamClient.IsConnected)) {
                return;
            }

            Logger.LogGenericInfo(Strings.BotConnecting);
            InitConnectionFailureTimer();
            SteamClient.Connect();
        }
        private async Task HeartBeat() {
            if (!KeepRunning || !IsConnectedAndLoggedOn || (HeartBeatFailures == byte.MaxValue)) {
                return;
            }

            try {
                if (DateTime.UtcNow.Subtract(SCHandler.LastPacketReceived).TotalSeconds > GlobalConfig.ConnectionTimeout) {
                    await SteamFriends.RequestProfileInfo(SteamID);
                }

                HeartBeatFailures = 0;

            } catch (Exception e) {
                Logger.LogGenericDebuggingException(e);

                if (!KeepRunning || !IsConnectedAndLoggedOn || (HeartBeatFailures == byte.MaxValue)) {
                    return;
                }

                if (++HeartBeatFailures >= (byte) Math.Ceiling(GlobalConfig.ConnectionTimeout / 10.0)) {
                    HeartBeatFailures = byte.MaxValue;
                    Logger.LogGenericWarning(Strings.BotConnectionLost);
                    Utilities.InBackground(() => Connect(true));
                }
            }
        }

    }
}