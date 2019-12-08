using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Acr.UserDialogs;
using JetBrains.Annotations;
using Newtonsoft.Json;
using SteamKit2;
using SteamKit2.Unified.Internal;
using Xamarin.Essentials;
using Xamarin.Forms;

// ReSharper disable MemberCanBePrivate.Global

namespace sc
{
    public class Avatar : INotifyPropertyChanged
    {
        public string avatarUrl { get; set; } = null;
        [JsonProperty]
        public string AvatarHash;
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName]string prop = "")
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(prop));
        }
    }
    
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal enum EUserInputType : byte
    {
        Unknown,
        DeviceID,
        Login,
        Password,
        SteamGuard,
        SteamParentalCode,
        TwoFactorAuthentication
    }


    public class Bot
    {
        public Avatar Avatar = new Avatar();
        internal const ushort CallbackSleep = 500; // In milliseconds
        private const ushort MaxMessageLength = 5000; // This is a limitation enforced by Steam
        private const byte MaxTwoFactorCodeFailures = 3;

        internal const byte
            MinPlayingBlockedTTL =
                60; // Delay in seconds added when account was occupied during our disconnect, to not disconnect other Steam client session too soon

        private const uint LoginID = 1212;
        private const byte LoginCooldownInMinutes = 25; // Captcha disappears after around 20 minutes, so we make it 25


        // internal bool HasMobileAuthenticator => BotDatabase?.MobileAuthenticator != null;


        public static readonly string CacheDir = FileSystem.CacheDirectory;
        public static readonly string MainDir = FileSystem.AppDataDirectory;

        public static readonly SemaphoreSlim BotsSemaphore = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim LoginRateLimitingSemaphore = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim LoginSemaphore = new SemaphoreSlim(1, 1);
        internal readonly BotDatabase BotDatabase;
        public readonly string BotName;
        private readonly CallbackManager CallbackManager;
        private readonly SemaphoreSlim CallbackSemaphore = new SemaphoreSlim(1, 1);
        private readonly Timer HeartBeatTimer;
        public readonly Logger Logger;
        private readonly MainPage mainPage = (MainPage) Application.Current.MainPage;

        internal readonly ConcurrentDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)>
            OwnedPackageIDs = new ConcurrentDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)>();

        internal readonly SCHandler SCHandler;
        internal readonly SteamApps SteamApps;
        private readonly SteamClient SteamClient;
        public readonly SteamConfiguration SteamConfiguration;
        internal readonly SteamFriends SteamFriends;
        private readonly SteamUser SteamUser;
        public readonly WebHandler WebHandler;

        private string AuthCode;

        private Timer ConnectionFailureTimer;

        private string DeviceID;
        private bool FirstTradeSent;


        private Timer GamesRedeemerInBackgroundTimer;
        private byte HeartBeatFailures;
        private uint ItemsCount;
        private EResult LastLogOnResult;
        private DateTime LastLogonSessionReplaced;
        private bool LibraryLocked;

        private Timer PlayingWasBlockedTimer;

        private bool ReconnectOnUserInitiated;

        //TODO
        private bool SteamParentalActive;
        private uint TradesCount;
        private string TwoFactorCode;
        private byte TwoFactorCodeFailures;

        public Bot([NotNull] string botName, [NotNull] BotConfig botConfig, [NotNull] BotDatabase botDatabase)
        {
            if (string.IsNullOrEmpty(botName) || botConfig == null || botDatabase == null)
                throw new ArgumentNullException(nameof(botName) + " || " + nameof(botConfig) + " || " +
                                                nameof(botDatabase));

            BotName = botName;
            BotConfig = botConfig;
            BotDatabase = botDatabase;

            Logger = new Logger(botName);

            /*if (HasMobileAuthenticator) {
                BotDatabase.MobileAuthenticator.Init(this);
            }*/

            WebHandler = new WebHandler(this);

            SteamConfiguration = SteamConfiguration.Create(builder =>
                builder.WithProtocolTypes(sc.GlobalConfig.SteamProtocols).WithCellID(sc.GlobalDatabase.CellID)
                    .WithHttpClientFactory(WebHandler.GenerateDisposableHttpClient));

            // Initialize
            SteamClient = new SteamClient(SteamConfiguration);

            if (Debugging.IsUserDebugging && Directory.Exists(Path.Combine(MainDir, SharedInfo.DebugDirectory)))
            {
                var debugListenerPath = Path.Combine(MainDir, SharedInfo.DebugDirectory, botName);

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
            CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
            //         
            //            SteamApps = SteamClient.GetHandler<SteamApps>();
            //            CallbackManager.Subscribe<SteamApps.GuestPassListCallback>(OnGuestPassList);
            //            CallbackManager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);
            //         
            SteamFriends = SteamClient.GetHandler<SteamFriends>();
            CallbackManager.Subscribe<SteamFriends.FriendMsgHistoryCallback>(OnFriendMsgHistory);
            CallbackManager.Subscribe<SteamFriends.FriendAddedCallback>(OnFriendAdded);
            CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);
            CallbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);
            CallbackManager.Subscribe<SteamFriends.FriendMsgCallback>(OnMessageReceived);

            CallbackManager.Subscribe<SteamUnifiedMessages.ServiceMethodNotification>(OnServiceMethod);
            //         
            SteamUser = SteamClient.GetHandler<SteamUser>();
            CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
            CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            CallbackManager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);
            CallbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);
            CallbackManager.Subscribe<SteamUser.WalletInfoCallback>(OnWalletUpdate);
            //CallbackManager.Subscribe<SCHandler.PlayingSessionStateCallback>(OnPlayingSessionState);
            //CallbackManager.Subscribe<SCHandler.SharedLibraryLockStatusCallback>(OnSharedLibraryLockStatus);
            CallbackManager.Subscribe<SCHandler.UserNotificationsCallback>(OnUserNotifications);
            //CallbackManager.Subscribe<SCHandler.VanityURLChangedCallback>(OnVanityURLChangedCallback);

            /*Actions = new Actions(this);
            CardsFarmer = new CardsFarmer(this);
            Commands = new Commands(this);
            Trading = new Trading(this);*/

            /*if (!Debugging.IsDebugBuild && ASF.GlobalConfig.Statistics) {
                Statistics = new Statistics(this);
            }*/

            HeartBeatTimer = new Timer(async e => await HeartBeat().ConfigureAwait(false), null,
                TimeSpan.FromMinutes(1) +
                TimeSpan.FromSeconds(sc.GlobalConfig.LoginLimiterDelay /** Bots.Count*/), // Delay
                TimeSpan.FromMinutes(1) // Period
            );
        }

        internal static ConcurrentDictionary<string, Bot> Bots { get; private set; }

        public BotConfig BotConfig { get; }

        public string avatarUrl => Avatar.avatarUrl;
        internal bool PlayingWasBlocked { get; private set; }
        public ulong SteamID { get; private set; }

        public long WalletBalance { get; private set; }
        public bool KeepRunning { get; private set; }
        public string Nickname { get; private set; }
        public ECurrencyCode WalletCurrency { get; private set; }
        internal bool PlayingBlocked { get; private set; }

        internal bool IsAccountLimited => AccountFlags.HasFlag(EAccountFlags.LimitedUser) ||
                                          AccountFlags.HasFlag(EAccountFlags.LimitedUserForce);

        internal bool IsAccountLocked => AccountFlags.HasFlag(EAccountFlags.Lockdown);
        public EAccountFlags AccountFlags { get; private set; }


        public bool IsConnectedAndLoggedOn => SteamClient?.SteamID != null;

        private async Task Connect(bool force = false)
        {
            if (!force && (!KeepRunning || SteamClient.IsConnected)) return;

            await LimitLoginRequestsAsync().ConfigureAwait(false);

            if (!force && (!KeepRunning || SteamClient.IsConnected)) return;

            Logger.LogGenericInfo(Strings.BotConnecting);
            InitConnectionFailureTimer();
            SteamClient.Connect();
        }

        private void Disconnect()
        {
            StopConnectionFailureTimer();
            SteamClient.Disconnect();
        }

        internal static string FormatBotResponse(string response, string botName)
        {
            if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(botName))
            {
                sc.Logger.LogNullError(nameof(response) + " || " + nameof(botName));

                return null;
            }

            return Environment.NewLine + "<" + botName + "> " + response;
        }

        private void HandleCallbacks()
        {
            TimeSpan timeSpan = TimeSpan.FromMilliseconds(CallbackSleep);

            while (KeepRunning || SteamClient.IsConnected)
            {
                if (!CallbackSemaphore.Wait(0))
                {
                    if (Debugging.IsUserDebugging)
                        Logger.LogGenericDebug(string.Format(Strings.WarningFailedWithError,
                            nameof(CallbackSemaphore)));

                    return;
                }

                try
                {
                    CallbackManager.RunWaitAllCallbacks(timeSpan);
                }
                catch (Exception e)
                {
                    Logger.LogGenericException(e);
                }
                finally
                {
                    CallbackSemaphore.Release();
                }
            }
        }

        private async Task HeartBeat()
        {
            if (!KeepRunning || !IsConnectedAndLoggedOn || HeartBeatFailures == byte.MaxValue) return;

            try
            {
                if (DateTime.UtcNow.Subtract(SCHandler.LastPacketReceived).TotalSeconds >
                    sc.GlobalConfig.ConnectionTimeout)
                    await SteamFriends.RequestProfileInfo(SteamID);

                HeartBeatFailures = 0;
            }
            catch (Exception e)
            {
                Logger.LogGenericDebuggingException(e);

                if (!KeepRunning || !IsConnectedAndLoggedOn || HeartBeatFailures == byte.MaxValue) return;

                if (++HeartBeatFailures >= (byte) Math.Ceiling(sc.GlobalConfig.ConnectionTimeout / 10.0))
                {
                    HeartBeatFailures = byte.MaxValue;
                    Logger.LogGenericWarning(Strings.BotConnectionLost);
                    Utilities.InBackground(() => Connect(true));
                }
            }
        }

        private void InitConnectionFailureTimer()
        {
            if (ConnectionFailureTimer != null) return;

            ConnectionFailureTimer = new Timer(
                async e => await InitPermanentConnectionFailure().ConfigureAwait(false),
                null,
                TimeSpan.FromMinutes(Math.Ceiling(sc.GlobalConfig.ConnectionTimeout / 30.0)), // Delay
                Timeout.InfiniteTimeSpan // Period
            );
        }

        public async Task InitModules()
        {
            AccountFlags = EAccountFlags.NormalUser;
            Avatar.AvatarHash = Nickname = null;
            WalletBalance = 0;
            WalletCurrency = ECurrencyCode.Invalid;

            /*CardsFarmer.SetInitialState(BotConfig.Paused);*/

            /*if (SendItemsTimer != null) {
                SendItemsTimer.Dispose();
                SendItemsTimer = null;
            }*/

            /*if ((BotConfig.SendTradePeriod > 0) && BotConfig.SteamUserPermissions.Values.Any(permission => permission >= BotConfig.EPermission.Master)) {
                SendItemsTimer = new Timer(
                    async e => await Actions.SendTradeOffer(wantedTypes: BotConfig.LootableTypes).ConfigureAwait(false),
                    null,
                    TimeSpan.FromHours(BotConfig.SendTradePeriod) + TimeSpan.FromSeconds(ASF.LoadBalancingDelay * Bots.Count), // Delay
                    TimeSpan.FromHours(BotConfig.SendTradePeriod) // Period
                );
            }*/

            /*await PluginsCore.OnBotInitModules(this, BotConfig.AdditionalProperties).ConfigureAwait(false);*/
        }


        private async Task InitPermanentConnectionFailure()
        {
            if (!KeepRunning) return;

            Logger.LogGenericError(Strings.BotHeartBeatFailed);
            await Destroy(true).ConfigureAwait(false);
            await RegisterBot(BotName).ConfigureAwait(false);
        }

        private void InitPlayingWasBlockedTimer()
        {
            if (PlayingWasBlockedTimer != null) return;

            PlayingWasBlockedTimer = new Timer(e => ResetPlayingWasBlockedWithTimer(), null,
                TimeSpan.FromSeconds(MinPlayingBlockedTTL), // Delay
                Timeout.InfiniteTimeSpan // Period
            );
        }

        public void InitStart()
        {
            if (!BotConfig.Enabled)
            {
                Logger.LogGenericInfo(Strings.BotInstanceNotStartingBecauseDisabled);

                return;
            }

            // Start
            Utilities.InBackground(Start);
        }

        private static async Task LimitLoginRequestsAsync()
        {
            if (sc.GlobalConfig.LoginLimiterDelay == 0)
            {
                await LoginRateLimitingSemaphore.WaitAsync().ConfigureAwait(false);
                LoginRateLimitingSemaphore.Release();

                return;
            }

            await LoginSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                await LoginRateLimitingSemaphore.WaitAsync().ConfigureAwait(false);
                LoginRateLimitingSemaphore.Release();
            }
            finally
            {
                Utilities.InBackground(async () =>
                {
                    await Task.Delay(sc.GlobalConfig.LoginLimiterDelay * 1000).ConfigureAwait(false);
                    LoginSemaphore.Release();
                });
            }
        }

        private void OnConnected(SteamClient.ConnectedCallback callback)
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

            var sentryFilePath = GetFilePath(EFileType.SentryFile);

            if (string.IsNullOrEmpty(sentryFilePath))
            {
                Logger.LogNullError(nameof(sentryFilePath));

                return;
            }

            byte[] sentryFileHash = null;

            if (File.Exists(sentryFilePath))
                try
                {
                    var sentryFileContent = File.ReadAllBytes(sentryFilePath);
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
                if (string.IsNullOrEmpty(mainPage.Password) ||
                    string.IsNullOrEmpty(AuthCode) && string.IsNullOrEmpty(TwoFactorCode))
                    loginKey = BotDatabase.LoginKey;
            }
            else
            {
                // If we're not using login keys, ensure we don't have any saved
                BotDatabase.LoginKey = null;
            }

            /*if (!await InitLoginAndPassword(string.IsNullOrEmpty(loginKey)).ConfigureAwait(false)) {
                Stop();

                return;
            }*/

            // Steam login and password fields can contain ASCII characters only, including spaces
            const string nonAsciiPattern = @"[^\u0000-\u007F]+";

            var username = Regex.Replace(mainPage.Login, nonAsciiPattern, "",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            var password = Regex.Replace(mainPage.Password, nonAsciiPattern, "",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            if (!string.IsNullOrEmpty(password))
                password = Regex.Replace(password, nonAsciiPattern, "",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            Logger.LogGenericInfo(Strings.BotLoggingIn);

            /*if (string.IsNullOrEmpty(TwoFactorCode) && HasMobileAuthenticator) {
                // We should always include 2FA token, even if it's not required
                TwoFactorCode = await BotDatabase.MobileAuthenticator.GenerateToken().ConfigureAwait(false);
            }*/

            InitConnectionFailureTimer();

            var logOnDetails = new SteamUser.LogOnDetails
            {
                AuthCode = AuthCode,
                CellID = sc.GlobalDatabase.CellID,
                LoginID = LoginID,
                LoginKey = loginKey,
                Password = password,
                SentryFileHash = sentryFileHash,
                ShouldRememberPassword = BotConfig.UseLoginKeys,
                TwoFactorCode = TwoFactorCode,
                Username = username
            };

            /*if (OSType == EOSType.Unknown) {
                OSType = logOnDetails.ClientOSType;
            }*/

            SteamUser.LogOn(logOnDetails);
        }

        private async void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            if (callback == null)
            {
                Logger.LogNullError(nameof(callback));

                return;
            }

            EResult lastLogOnResult = LastLogOnResult;
            LastLogOnResult = EResult.Invalid;
            ItemsCount = TradesCount = HeartBeatFailures = 0;
            SteamParentalActive = true;
            StopConnectionFailureTimer();
            StopPlayingWasBlockedTimer();

            Logger.LogGenericInfo(Strings.BotDisconnected);

            OwnedPackageIDs.Clear();

            //Actions.OnDisconnected();
            WebHandler.OnDisconnected();
            //CardsFarmer.OnDisconnected();
            //Trading.OnDisconnected();

            FirstTradeSent = false;

            /*await PluginsCore.OnBotDisconnected(this, callback.UserInitiated ? EResult.OK : lastLogOnResult).ConfigureAwait(false);*/

            // If we initiated disconnect, do not attempt to reconnect
            if (callback.UserInitiated && !ReconnectOnUserInitiated) return;

            switch (lastLogOnResult)
            {
                case EResult.AccountDisabled:
                case EResult.InvalidPassword when string.IsNullOrEmpty(BotDatabase.LoginKey):
                    // Do not attempt to reconnect, those failures are permanent
                    return;
                case EResult.Invalid:
                    // Invalid means that we didn't get OnLoggedOn() in the first place, so Steam is down
                    // Always reset one-time-only access tokens in this case, as OnLoggedOn() didn't do that for us
                    AuthCode = TwoFactorCode = null;

                    break;
                case EResult.InvalidPassword:
                    BotDatabase.LoginKey = null;
                    Logger.LogGenericInfo(Strings.BotRemovedExpiredLoginKey);

                    break;
                case EResult.NoConnection:
                case EResult.ServiceUnavailable:
                case EResult.Timeout:
                case EResult.TryAnotherCM:
                    await Task.Delay(5000).ConfigureAwait(false);

                    break;
                case EResult.RateLimitExceeded:
                    Logger.LogGenericInfo(string.Format(Strings.BotRateLimitExceeded,
                        TimeSpan.FromMinutes(LoginCooldownInMinutes).ToHumanReadable()));

                    if (!await LoginRateLimitingSemaphore.WaitAsync(1000 * WebBrowser.MaxTries)
                        .ConfigureAwait(false)) break;

                    try
                    {
                        await Task.Delay(LoginCooldownInMinutes * 60 * 1000).ConfigureAwait(false);
                    }
                    finally
                    {
                        LoginRateLimitingSemaphore.Release();
                    }

                    break;
            }

            if (!KeepRunning || SteamClient.IsConnected) return;

            Logger.LogGenericInfo(Strings.BotReconnecting);
            await Connect().ConfigureAwait(false);
        }

        private async void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback == null)
            {
                Logger.LogNullError(nameof(callback));

                return;
            }

            // Always reset one-time-only access tokens
            AuthCode = TwoFactorCode = null;

            // Keep LastLogOnResult for OnDisconnected()
            LastLogOnResult = callback.Result;

            HeartBeatFailures = 0;
            StopConnectionFailureTimer();

            switch (callback.Result)
            {
                case EResult.AccountDisabled:
                case EResult.InvalidPassword when string.IsNullOrEmpty(BotDatabase.LoginKey):
                    // Those failures are permanent, we should Stop() the bot if any of those happen
                    Logger.LogGenericWarning(string.Format(Strings.BotUnableToLogin, callback.Result,
                        callback.ExtendedResult));
                    Stop();

                    break;
                case EResult.AccountLogonDenied:
                    string authCode = null;
                    PromptResult pResult = await UserDialogs.Instance.PromptAsync(new PromptConfig
                    {
                        InputType = InputType.Name,
                        OkText = "Enter",
                        Title = "Enter your guard code",
                        IsCancellable = false,
                        MaxLength = 5
                    });
                    if (pResult.Ok && !string.IsNullOrWhiteSpace(pResult.Text)) authCode = pResult.Text;

                    if (string.IsNullOrEmpty(authCode))
                    {
                        Stop();

                        break;
                    }

                    AuthCode = authCode;
                    /*SetUserInput(EUserInputType.SteamGuard, authCode);*/

                    break;
                case EResult.AccountLoginDeniedNeedTwoFactor:

                    string twoFactorCode = null;
                    PromptResult aResult = await UserDialogs.Instance.PromptAsync(new PromptConfig
                    {
                        InputType = InputType.Name,
                        OkText = "Enter",
                        Title = "Enter your guard code",
                        IsCancellable = false,
                        MaxLength = 5
                    });
                    if (aResult.Ok && !string.IsNullOrWhiteSpace(aResult.Text)) twoFactorCode = aResult.Text;

                    if (string.IsNullOrEmpty(twoFactorCode))
                    {
                        Stop();

                        break;
                    }

                    TwoFactorCode = twoFactorCode;
                    /*SetUserInput(ASF.EUserInputType.TwoFactorAuthentication, twoFactorCode);*/


                    break;
                case EResult.OK:
                    AccountFlags = callback.AccountFlags;
                    SteamID = callback.ClientSteamID;

                    Logger.LogGenericInfo(string.Format(Strings.BotLoggedOn,
                        SteamID + (!string.IsNullOrEmpty(callback.VanityURL) ? "/" + callback.VanityURL : "")));

                    // Old status for these doesn't matter, we'll update them if needed
                    TwoFactorCodeFailures = 0;
                    LibraryLocked = PlayingBlocked = false;

                    if (PlayingWasBlocked && PlayingWasBlockedTimer == null) InitPlayingWasBlockedTimer();

                    if (IsAccountLimited) Logger.LogGenericWarning(Strings.BotAccountLimited);

                    if (IsAccountLocked) Logger.LogGenericWarning(Strings.BotAccountLocked);

                    if (callback.CellID != 0 && callback.CellID != sc.GlobalDatabase.CellID)
                        sc.GlobalDatabase.CellID = callback.CellID;

                    // Handle steamID-based maFile
                    /*if (!HasMobileAuthenticator) {
                        string maFilePath = Path.Combine(SharedInfo.ConfigDirectory, SteamID + SharedInfo.MobileAuthenticatorExtension);

                        if (File.Exists(maFilePath)) {
                            await ImportAuthenticator(maFilePath).ConfigureAwait(false);
                        }
                    }*/
                    /*
                    if (callback.ParentalSettings != null) {
                        (bool isSteamParentalEnabled, string steamParentalCode) = ValidateSteamParental(callback.ParentalSettings, BotConfig.SteamParentalCode);
                    
                        if (isSteamParentalEnabled) {
                            SteamParentalActive = true;
                    
                            if (!string.IsNullOrEmpty(steamParentalCode)) {
                                if (BotConfig.SteamParentalCode != steamParentalCode) {
                                    SetUserInput(ASF.EUserInputType.SteamParentalCode, steamParentalCode);
                                }
                            } else if (string.IsNullOrEmpty(BotConfig.SteamParentalCode) || (BotConfig.SteamParentalCode.Length != BotConfig.SteamParentalCodeLength)) {
                                steamParentalCode = await Logging.GetUserInput(ASF.EUserInputType.SteamParentalCode, BotName).ConfigureAwait(false);
                    
                                if (string.IsNullOrEmpty(steamParentalCode) || (steamParentalCode.Length != BotConfig.SteamParentalCodeLength)) {
                                    Stop();
                    
                                    break;
                                }
                    
                                SetUserInput(ASF.EUserInputType.SteamParentalCode, steamParentalCode);
                            }
                        } else {
                            SteamParentalActive = false;
                        }
                    } 
                    else if (SteamParentalActive && !string.IsNullOrEmpty(BotConfig.SteamParentalCode) && (BotConfig.SteamParentalCode.Length != BotConfig.SteamParentalCodeLength)) {
                        string steamParentalCode = await Logging.GetUserInput(ASF.EUserInputType.SteamParentalCode, BotName).ConfigureAwait(false);
                    
                        if (string.IsNullOrEmpty(steamParentalCode) || (steamParentalCode.Length != BotConfig.SteamParentalCodeLength)) {
                            Stop();
                    
                            break;
                        }
                    
                        SetUserInput(ASF.EUserInputType.SteamParentalCode, steamParentalCode);
                    }
                    */
                    WebHandler.OnVanityURLChanged(callback.VanityURL);

                    if (!await WebHandler.Init(SteamID, SteamClient.Universe, callback.WebAPIUserNonce,
                        SteamParentalActive ? BotConfig.SteamParentalCode : null).ConfigureAwait(false))
                        if (!await RefreshSession().ConfigureAwait(false))
                            break;

                    // Pre-fetch API key for future usage if possible
                    Utilities.InBackground(WebHandler.HasValidApiKey);

                    /*if ((GamesRedeemerInBackgroundTimer == null) && BotDatabase.HasGamesToRedeemInBackground) {
                        Utilities.InBackground(RedeemGamesInBackground);
                    }*/

                    SCHandler.SetCurrentMode(2);
                    SCHandler.RequestItemAnnouncements();

                    // Sometimes Steam won't send us our own PersonaStateCallback, so request it explicitly
                    RequestPersonaStateUpdate();

                    /*Utilities.InBackground(InitializeFamilySharing);*/

                    /*if (Statistics != null) {
                        Utilities.InBackground(Statistics.OnLoggedOn);
                    }*/

                    if (BotConfig.OnlineStatus != EPersonaState.Offline)
                        SteamFriends.SetPersonaState(BotConfig.OnlineStatus);

                    /*if (BotConfig.SteamMasterClanID != 0) {
                        Utilities.InBackground(
                            async () => {
                                if (!await WebHandler.JoinGroup(BotConfig.SteamMasterClanID).ConfigureAwait(false)) {
                                    Logger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(WebHandler.JoinGroup)));
                                }

                                await JoinMasterChatGroupID().ConfigureAwait(false);
                            }
                        );
                    }*/

                    /*await PluginsCore.OnBotLoggedOn(this).ConfigureAwait(false);*/
                    Device.BeginInvokeOnMainThread(() => Application.Current.MainPage = sc.Mainpage1);
                    RequestPersonaStateUpdate();
                    break;
                case EResult.InvalidPassword:
                case EResult.NoConnection:
                case EResult.PasswordRequiredToKickSession
                    : // Not sure about this one, it seems to be just generic "try again"? #694
                case EResult.RateLimitExceeded:
                case EResult.ServiceUnavailable:
                case EResult.Timeout:
                case EResult.TryAnotherCM:
                case EResult.TwoFactorCodeMismatch:
                    Logger.LogGenericWarning(string.Format(Strings.BotUnableToLogin, callback.Result,
                        callback.ExtendedResult));

                    /*if ((callback.Result == EResult.TwoFactorCodeMismatch) && HasMobileAuthenticator) {
                        if (++TwoFactorCodeFailures >= MaxTwoFactorCodeFailures) {
                            TwoFactorCodeFailures = 0;
                            Logger.LogGenericError(string.Format(Strings.BotInvalidAuthenticatorDuringLogin, MaxTwoFactorCodeFailures));
                            Stop();
                        }
                    }*/

                    break;
                default:
                    // Unexpected result, shutdown immediately
                    Logger.LogGenericError(string.Format(Strings.BotUnableToLogin, callback.Result,
                        callback.ExtendedResult));
                    Stop();

                    break;
            }
        }


        private void OnLoginKey(SteamUser.LoginKeyCallback callback)
        {
            if (string.IsNullOrEmpty(callback?.LoginKey))
            {
                Logger.LogNullError(nameof(callback) + " || " + nameof(callback.LoginKey));

                return;
            }

            if (!BotConfig.UseLoginKeys) return;

            var loginKey = callback.LoginKey;

            BotDatabase.LoginKey = loginKey;
            SteamUser.AcceptNewLoginKey(callback);
        }

        private async void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback)
        {
            if (callback == null)
            {
                Logger.LogNullError(nameof(callback));

                return;
            }

            var sentryFilePath = GetFilePath(EFileType.SentryFile);

            sentryFilePath = Path.Combine(MainDir, sentryFilePath);
            if (string.IsNullOrEmpty(sentryFilePath))
            {
                Logger.LogNullError(nameof(sentryFilePath));

                return;
            }

            long fileSize;
            byte[] sentryHash;

            try
            {
                FileStream fileStream = File.Open(sentryFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);

                fileStream.Seek(callback.Offset, SeekOrigin.Begin);

                await fileStream.WriteAsync(callback.Data, 0, callback.BytesToWrite).ConfigureAwait(false);

                fileSize = fileStream.Length;
                fileStream.Seek(0, SeekOrigin.Begin);

                var sha = new SHA1CryptoServiceProvider();

                sentryHash = sha.ComputeHash(fileStream);
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

                return;
            }

            // Inform the steam servers that we're accepting this sentry file
            SteamUser.SendMachineAuthResponse(
                new SteamUser.MachineAuthDetails
                {
                    BytesWritten = callback.BytesToWrite,
                    FileName = callback.FileName,
                    FileSize = (int) fileSize,
                    JobID = callback.JobID,
                    LastError = 0,
                    Offset = callback.Offset,
                    OneTimePassword = callback.OneTimePassword,
                    Result = EResult.OK,
                    SentryFileHash = sentryHash
                }
            );
        }

        private void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            if (callback == null)
            {
                Logger.LogNullError(nameof(callback));

                return;
            }

            LastLogOnResult = callback.Result;

            Logger.LogGenericInfo(string.Format(Strings.BotLoggedOff, callback.Result));

            switch (callback.Result)
            {
                case EResult.LoggedInElsewhere:
                    // This result directly indicates that playing was blocked when we got (forcefully) disconnected
                    PlayingWasBlocked = true;

                    break;
                case EResult.LogonSessionReplaced:
                    DateTime now = DateTime.UtcNow;

                    if (now.Subtract(LastLogonSessionReplaced).TotalHours < 1)
                    {
                        Logger.LogGenericError(Strings.BotLogonSessionReplaced);
                        Stop();

                        return;
                    }

                    LastLogonSessionReplaced = now;

                    break;
            }

            ReconnectOnUserInitiated = true;
            SteamClient.Disconnect();
        }

        private void OnFriendMsgHistory(SteamFriends.FriendMsgHistoryCallback callback)
        {
            if (callback.Result != EResult.OK)
                Logger.LogGenericError(nameof(callback) + " || " + nameof(callback.Result));


            sc.MsgHistory = callback;
        }

        private static void OnFriendAdded(SteamFriends.FriendAddedCallback callback)
        {
            // someone accepted our friend request, or we accepted one
            Debug.WriteLine("{0} is now a friend", callback.PersonaName);
        }

        private async void OnFriendsList(SteamFriends.FriendsListCallback callback)
        {
            if (callback?.FriendList == null)
            {
                Logger.LogNullError(nameof(callback) + " || " + nameof(callback.FriendList));

                return;
            }

            int RequestRecipient = 0;
            foreach (SteamFriends.FriendsListCallback.Friend friend in callback.FriendList.Where(friend => friend.Relationship == EFriendRelationship.Friend)) {
                RequestPersonaStateUpdate(friend.SteamID);
            }
            
            foreach (SteamFriends.FriendsListCallback.Friend friend in callback.FriendList.Where(friend => friend.Relationship == EFriendRelationship.RequestRecipient))
            {
                RequestRecipient++;
            }
            if (RequestRecipient > 0)
            {
                var acceptFriendRequest = await UserDialogs.Instance.ConfirmAsync(new ConfirmConfig
                {
                    Message = "Accept this request?",
                    Title = $"you have {RequestRecipient} friend request{(RequestRecipient > 1 ? "" : "s")}, do you want to check?",
                    CancelText = "Ignore",
                    OkText = "Accept"
                });
                if (acceptFriendRequest) sc.Mainpage1.OpenWebPage(WebHandler.SteamCommunityURL + "/my/friends/pending");
            }


           
        }

        private async void OnServiceMethod(SteamUnifiedMessages.ServiceMethodNotification notification)
        {
            if (notification == null)
            {
                Logger.LogNullError(nameof(notification));

                return;
            }

            switch (notification.MethodName)
            {
                case "ChatRoomClient.NotifyIncomingChatMessage#1":
                    await OnIncomingChatMessage((CChatRoom_IncomingChatMessage_Notification) notification.Body)
                        .ConfigureAwait(false);

                    break;
                case "FriendMessagesClient.IncomingMessage#1":
                    await OnIncomingMessage((CFriendMessages_IncomingMessage_Notification) notification.Body)
                        .ConfigureAwait(false);

                    break;
            }
        }

        private async Task OnIncomingChatMessage(CChatRoom_IncomingChatMessage_Notification notification)
        {
            if (notification == null)
            {
                Logger.LogNullError(nameof(notification));

                return;
            }

            // Under normal circumstances, timestamp must always be greater than 0, but Steam already proved that it's capable of going against the logic
            if (notification.steamid_sender != SteamID && notification.timestamp > 0 &&
                BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.MarkReceivedMessagesAsRead))
            {
                //todo MarkAsRead Utilities.InBackground(() => SCHandler.AckChatMessage(notification.chat_group_id, notification.chat_id, notification.timestamp));
            }

            string message;

            // Prefer to use message without bbcode, but only if it's available
            if (!string.IsNullOrEmpty(notification.message_no_bbcode))
                message = notification.message_no_bbcode;
            else if (!string.IsNullOrEmpty(notification.message))
                message = UnEscape(notification.message);
            else
                return;

            Logger.LogChatMessage(false, message, notification.chat_group_id, notification.chat_id,
                notification.steamid_sender);

            // Steam network broadcasts chat events also when we don't explicitly sign into Steam community
            // We'll explicitly ignore those messages when using offline mode, as it was done in the first version of Steam chat when no messages were broadcasted at all before signing in
            // Handling messages will still work correctly in invisible mode, which is how it should work in the first place
            // This goes in addition to usual logic that ignores irrelevant messages from being parsed further


            // todo await Commands.HandleMessage(notification.chat_group_id, notification.chat_id, notification.steamid_sender, message).ConfigureAwait(false);
        }

        private async Task OnIncomingMessage(CFriendMessages_IncomingMessage_Notification notification)
        {
            if (notification == null)
            {
                Logger.LogNullError(nameof(notification));

                return;
            }

            if ((EChatEntryType) notification.chat_entry_type != EChatEntryType.ChatMsg) return;

            // Under normal circumstances, timestamp must always be greater than 0, but Steam already proved that it's capable of going against the logic
            if (!notification.local_echo && notification.rtime32_server_timestamp > 0 &&
                BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.MarkReceivedMessagesAsRead))
            {
                //todo Utilities.InBackground(() => SCHandler.AckMessage(notification.steamid_friend, notification.rtime32_server_timestamp));
            }

            string message;

            // Prefer to use message without bbcode, but only if it's available
            if (!string.IsNullOrEmpty(notification.message_no_bbcode))
                message = notification.message_no_bbcode;
            else if (!string.IsNullOrEmpty(notification.message))
                message = UnEscape(notification.message);
            else
                return;

            Logger.LogChatMessage(notification.local_echo, message, steamID: notification.steamid_friend);

            // Steam network broadcasts chat events also when we don't explicitly sign into Steam community
            // We'll explicitly ignore those messages when using offline mode, as it was done in the first version of Steam chat when no messages were broadcasted at all before signing in
            // Handling messages will still work correctly in invisible mode, which is how it should work in the first place
            // This goes in addition to usual logic that ignores irrelevant messages from being parsed further
            if (notification.local_echo || BotConfig.OnlineStatus == EPersonaState.Offline) return;

            //todo await Commands.HandleMessage(notification.steamid_friend, message).ConfigureAwait(false);
        }

        private void OnPersonaState(SteamFriends.PersonaStateCallback callback)
        {
            if (callback == null)
            {
                Logger.LogNullError(nameof(callback));

                return;
            }

            if (callback.FriendID != SteamID)
            {
                bool exists =sc.Mainpage1.Friends.FriendList.Exists(friend1 =>(friend1.steamID == callback.FriendID));
                bool isFriend = SteamFriends.GetFriendRelationship(callback.FriendID) == EFriendRelationship.Friend;
                if (!exists && isFriend)
                {
                    sc.Mainpage1.Friends.FriendList.Add(new Friend(callback.FriendID,callback.AvatarHash,callback.Name,callback.State)); 
                    return;
                }

                if (exists && isFriend)
                {
                    int index = sc.Mainpage1.Friends.FriendList.FindIndex(friend1 =>
                        (friend1.steamID == callback.FriendID));
                    
                    if (sc.Mainpage1.Friends.FriendList[index].Nickname !=callback.Name)
                    {
                        sc.Mainpage1.Friends.FriendList[index].Nickname = callback.Name;
                    }
                    if (sc.Mainpage1.Friends.FriendList[index].OnlineStatus !=callback.State)
                    {
                        sc.Mainpage1.Friends.FriendList[index].OnlineStatus = callback.State;
                    }
                    if (sc.Mainpage1.Friends.FriendList[index].AvatarHash !=callback.AvatarHash)
                    {
                        sc.Mainpage1.Friends.FriendList[index].AvatarHash = callback.AvatarHash;
                    }
                    
                    
                    return;
                }
                return;
                
            }

            string avatarHash = null;

            if (callback.AvatarHash != null && callback.AvatarHash.Length > 0 &&
                callback.AvatarHash.Any(singleByte => singleByte != 0))
            {
                avatarHash = BitConverter.ToString(callback.AvatarHash).Replace("-", "").ToLowerInvariant();

                if (string.IsNullOrEmpty(avatarHash) || avatarHash.All(singleChar => singleChar == '0'))
                    avatarHash = null;
            }

            Avatar.AvatarHash = avatarHash;
            Nickname = callback.Name;
            Avatar.avatarUrl = "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/avatars/" +
                        avatarHash.Substring(0, 2).ToLower() + "/" +
                        avatarHash.ToLower() + "_full.jpg";
            sc.Mainpage1.BindingContext = this; 
        }

        private void OnMessageReceived(SteamFriends.FriendMsgCallback callback)
        {
        }

        private void OnWalletUpdate(SteamUser.WalletInfoCallback callback)
        {
            if (callback == null)
            {
                Logger.LogNullError(nameof(callback));

                return;
            }

            WalletBalance = callback.LongBalance;
            WalletCurrency = callback.Currency;
            //todo bind wallet value
        }

        private void OnUserNotifications(SCHandler.UserNotificationsCallback callback)
        {
            if (callback == null)
            {
                Logger.LogNullError(nameof(callback));

                return;
            }

            if (callback.Notifications == null || callback.Notifications.Count == 0) return;

            foreach ((SCHandler.UserNotificationsCallback.EUserNotification notification, uint count) in callback
                .Notifications)
                switch (notification)
                {
                    case SCHandler.UserNotificationsCallback.EUserNotification.Gifts:
                        /*bool newGifts = count > GiftsCount; todo gifts notify 
                        GiftsCount = count;

                        if (newGifts && BotConfig.AcceptGifts) {
                            Logger.LogGenericTrace(nameof(ArchiHandler.UserNotificationsCallback.EUserNotification.Gifts));
                            Utilities.InBackground(Actions.AcceptDigitalGiftCards);
                        }
                         */
                        break;
                    case SCHandler.UserNotificationsCallback.EUserNotification.Items:
                        var newItems = count > ItemsCount;
                        ItemsCount = count;

                        if (newItems)
                        {
                            Logger.LogGenericDebug(nameof(SCHandler.UserNotificationsCallback.EUserNotification.Items));
                            //todo Utilities.InBackground(CardsFarmer.OnNewItemsNotification);

                            if (BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.DismissInventoryNotifications))
                            {
                                //todo Utilities.InBackground(WebHandler.MarkInventory);
                            }
                        }

                        break;
                    case SCHandler.UserNotificationsCallback.EUserNotification.Trading:
                        var newTrades = count > TradesCount;
                        TradesCount = count;

                        if (newTrades)
                            Logger.LogGenericDebug(
                                nameof(SCHandler.UserNotificationsCallback.EUserNotification.Trading));
                        //todo Utilities.InBackground(Trading.OnNewTrade);

                        break;
                }
        }

        public static string ReadAllTextAsync([NotNull] string path)
        {
            return File.ReadAllText(path);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        internal async Task<bool> RefreshSession()
        {
            if (!IsConnectedAndLoggedOn) return false;

            SteamUser.WebAPIUserNonceCallback callback;

            try
            {
                callback = await SteamUser.RequestWebAPIUserNonce();
            }
            catch (Exception e)
            {
                Logger.LogGenericWarningException(e);
                await Connect(true).ConfigureAwait(false);

                return false;
            }

            if (string.IsNullOrEmpty(callback?.Nonce))
            {
                await Connect(true).ConfigureAwait(false);

                return false;
            }

            if (await WebHandler.Init(SteamID, SteamClient.Universe, callback.Nonce,
                SteamParentalActive ? BotConfig.SteamParentalCode : null).ConfigureAwait(false))
                return true;

            await Connect(true).ConfigureAwait(false);

            return false;
        }

        // ReSharper disable once MemberCanBePrivate.Global
        internal void RequestPersonaStateUpdate()
        {
            if (!IsConnectedAndLoggedOn) return;

            SteamFriends.RequestFriendInfo(SteamID,
                EClientPersonaStateFlag.PlayerName | EClientPersonaStateFlag.Presence | EClientPersonaStateFlag.Status);
        }
        
        internal void RequestPersonaStateUpdate(SteamID steamID)
        {
            if (!IsConnectedAndLoggedOn) return;

            
            SteamFriends.RequestFriendInfo(steamID,
                EClientPersonaStateFlag.PlayerName | EClientPersonaStateFlag.Presence | EClientPersonaStateFlag.Status | EClientPersonaStateFlag.GameExtraInfo);
        }

        private void ResetPlayingWasBlockedWithTimer()
        {
            PlayingWasBlocked = false;
            StopPlayingWasBlockedTimer();
        }

        private static async Task RegisterBot(string botName)
        {
            if (string.IsNullOrEmpty(botName))
            {
                sc.Logger.LogNullError(nameof(botName));

                return;
            }

            //if (Bots.ContainsKey(botName)) {
            //	return;
            //}

            var configFilePath = GetFilePath(botName, EFileType.Config);

            if (string.IsNullOrEmpty(configFilePath))
            {
                sc.Logger.LogNullError(nameof(configFilePath));

                return;
            }

            //BotConfig botConfig = await BotConfig.Load(configFilePath).ConfigureAwait(false);
            BotConfig botConfig = BotConfig.CreateOrLoad(configFilePath);
            if (botConfig == null)
            {
                sc.Logger.LogGenericError(string.Format(Strings.ErrorBotConfigInvalid, configFilePath));

                return;
            }

            if (Debugging.IsDebugConfigured)
                sc.Logger.LogGenericDebug(configFilePath + ": " +
                                          JsonConvert.SerializeObject(botConfig, Formatting.Indented));

            var databaseFilePath = GetFilePath(botName, EFileType.Database);

            if (string.IsNullOrEmpty(databaseFilePath))
            {
                sc.Logger.LogNullError(nameof(databaseFilePath));

                return;
            }

            BotDatabase botDatabase = await BotDatabase.CreateOrLoad(databaseFilePath).ConfigureAwait(false);


            if (botDatabase == null)
            {
                sc.Logger.LogGenericError(string.Format(Strings.ErrorDatabaseInvalid, databaseFilePath));

                return;
            }

            if (Debugging.IsDebugConfigured)
                sc.Logger.LogGenericDebug(databaseFilePath + ": " +
                                          JsonConvert.SerializeObject(botDatabase, Formatting.Indented));

            Bot bot;


            await BotsSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                if (Bots.ContainsKey(botName)) return;

                bot = new Bot(botName, botConfig, botDatabase);

                if (!Bots.TryAdd(botName, bot))
                {
                    sc.Logger.LogNullError(nameof(bot));
                    bot.Dispose();

                    return;
                }
            }
            finally
            {
                BotsSemaphore.Release();
            }

            await bot.InitModules().ConfigureAwait(false);
            bot.InitStart();
        }

        public void Dispose()
        {
            // Those are objects that are always being created if constructor doesn't throw exception
            //Actions.Dispose();
            CallbackSemaphore.Dispose();
            //GamesRedeemerInBackgroundSemaphore.Dispose();
            //InitializationSemaphore.Dispose();
            //MessagingSemaphore.Dispose();
            //PICSSemaphore.Dispose();

            // Those are objects that might be null and the check should be in-place
            WebHandler?.Dispose();
            BotDatabase?.Dispose();
            //CardsFarmer?.Dispose();
            ConnectionFailureTimer?.Dispose();
            GamesRedeemerInBackgroundTimer?.Dispose();
            HeartBeatTimer?.Dispose();
            PlayingWasBlockedTimer?.Dispose();
            //SendItemsTimer?.Dispose();
            //Statistics?.Dispose();
            //SteamSaleEvent?.Dispose();
            //Trading?.Dispose();
        }

        private async Task Start()
        {
            if (KeepRunning) return;

            KeepRunning = true;
            Utilities.InBackground(HandleCallbacks, true);
            Logger.LogGenericInfo(Strings.Starting);

            //      // Support and convert 2FA files
            //      if (!HasMobileAuthenticator) {
            //	        string mobileAuthenticatorFilePath = GetFilePath(EFileType.MobileAuthenticator);

            //	        if (string.IsNullOrEmpty(mobileAuthenticatorFilePath)) {
            //		        Logger.LogNullError(nameof(mobileAuthenticatorFilePath));

            //		        return;
            //	        }

            //	        if (File.Exists(mobileAuthenticatorFilePath)) {
            //		        await ImportAuthenticator(mobileAuthenticatorFilePath).ConfigureAwait(false);
            //	        }
            //      }

            //      string keysToRedeemFilePath = GetFilePath(EFileType.KeysToRedeem);

            //      if (string.IsNullOrEmpty(keysToRedeemFilePath)) {
            //	        Logger.LogNullError(nameof(keysToRedeemFilePath));

            //	        return;
            //      }

            //      if (File.Exists(keysToRedeemFilePath)) {
            //	        await ImportKeysToRedeem(keysToRedeemFilePath).ConfigureAwait(false);
            //      }

            await Connect().ConfigureAwait(false);
        }

        private async Task Destroy(bool force = false)
        {
            if (KeepRunning)
            {
                if (!force)
                    Stop();
                else
                    // Stop() will most likely block due to connection freeze, don't wait for it
                    Utilities.InBackground(() => Stop());
            }

            Bots.TryRemove(BotName, out _);
        }

        private void Stop(bool skipShutdownEvent = false)
        {
            if (!KeepRunning) return;

            KeepRunning = false;
            Logger.LogGenericInfo(Strings.BotStopping);

            if (SteamClient.IsConnected) Disconnect();

            if (!skipShutdownEvent) Utilities.InBackground(Events.OnBotShutdown);
        }

        private void StopConnectionFailureTimer()
        {
            if (ConnectionFailureTimer == null) return;

            ConnectionFailureTimer.Dispose();
            ConnectionFailureTimer = null;
        }

        private void StopPlayingWasBlockedTimer()
        {
            if (PlayingWasBlockedTimer == null) return;

            PlayingWasBlockedTimer.Dispose();
            PlayingWasBlockedTimer = null;
        }

        private string GetFilePath(EFileType fileType)
        {
            if (!Enum.IsDefined(typeof(EFileType), fileType))
            {
                sc.Logger.LogNullError(nameof(fileType));

                return null;
            }

            return GetFilePath(BotName, fileType);
        }

        private static string UnEscape(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                sc.Logger.LogNullError(nameof(message));
                return null;
            }

            return message.Replace("\\[", "[").Replace("\\\\", "\\");
        }

        internal static string GetFilePath(string botName, EFileType fileType)
        {
            if (string.IsNullOrEmpty(botName) || !Enum.IsDefined(typeof(EFileType), fileType))
            {
                sc.Logger.LogNullError(nameof(botName) + " || " + nameof(fileType));

                return null;
            }

            var botPath = Path.Combine(SharedInfo.ConfigDirectory, botName);

            switch (fileType)
            {
                case EFileType.Config:
                    return botPath + SharedInfo.JsonConfigExtension;
                case EFileType.Database:
                    return botPath + SharedInfo.DatabaseExtension;
                //case EFileType.KeysToRedeem:
                //	return botPath + SharedInfo.KeysExtension;
                //case EFileType.KeysToRedeemUnused:
                //	return botPath + SharedInfo.KeysExtension + SharedInfo.KeysUnusedExtension;
                //case EFileType.KeysToRedeemUsed:
                //	return botPath + SharedInfo.KeysExtension + SharedInfo.KeysUsedExtension;
                case EFileType.MobileAuthenticator:
                    return botPath + SharedInfo.MobileAuthenticatorExtension;
                case EFileType.SentryFile:
                    return botPath + SharedInfo.SentryHashExtension;
                default:
                    sc.Logger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(fileType),
                        fileType));

                    return null;
            }
        }

        internal enum EFileType : byte
        {
            Config,
            Database,

            //KeysToRedeem,
            //KeysToRedeemUnused,
            //KeysToRedeemUsed,
            MobileAuthenticator,
            SentryFile
        }
    }
}