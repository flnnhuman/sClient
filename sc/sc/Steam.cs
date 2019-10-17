using System;
using System.Collections.Concurrent;
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
using Newtonsoft.Json;
using sc;

namespace sc
{
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
        public BotConfig BotConfig { get; private set; }
        internal readonly BotDatabase BotDatabase;
        public readonly WebHandler WebHandler;


        internal const ushort CallbackSleep = 500; // In milliseconds

        private static readonly string CacheDir = FileSystem.CacheDirectory;
        private static readonly string MainDir = FileSystem.AppDataDirectory;
        private const ushort MaxMessageLength = 5000; // This is a limitation enforced by Steam
        private const byte MaxTwoFactorCodeFailures = 3;
        public readonly string BotName;

        internal const byte
            MinPlayingBlockedTTL =
                60; // Delay in seconds added when account was occupied during our disconnect, to not disconnect other Steam client session too soon

        private const uint LoginID = 1212;
        public Logger Logger;
        private const byte LoginCooldownInMinutes = 25; // Captcha disappears after around 20 minutes, so we make it 25

        private static readonly SemaphoreSlim BotsSemaphore = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim LoginRateLimitingSemaphore = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim LoginSemaphore = new SemaphoreSlim(1, 1);

        private readonly Timer HeartBeatTimer;
        private byte HeartBeatFailures;
        private Timer ConnectionFailureTimer;

        internal bool PlayingWasBlocked { get; private set; }
        private bool FirstTradeSent;

        internal readonly ConcurrentDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)>
            OwnedPackageIDs = new ConcurrentDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)>();

        internal readonly SCHandler SCHandler;
        public ulong SteamID { get; private set; }

        public long WalletBalance { get; private set; }
        private EResult LastLogOnResult;
        public bool KeepRunning { get; private set; }

        private Timer PlayingWasBlockedTimer;
        private bool ReconnectOnUserInitiated;
        public readonly SteamConfiguration SteamConfiguration;
        private readonly SteamClient SteamClient;
        private readonly CallbackManager CallbackManager;
        internal readonly SteamApps SteamApps;
        internal readonly SteamFriends SteamFriends;
        private readonly SteamUser SteamUser;
        private uint ItemsCount;
        private uint TradesCount;
        private bool SteamParentalActive = true;
        private readonly SemaphoreSlim CallbackSemaphore = new SemaphoreSlim(1, 1);

#pragma warning disable IDE0052
        [JsonProperty] private string AvatarHash;
#pragma warning restore IDE0052
        public string Nickname { get; private set; }


        private Timer GamesRedeemerInBackgroundTimer;
        private bool LibraryLocked;

        private string AuthCode;
        private string TwoFactorCode;
        private byte TwoFactorCodeFailures;
        public ECurrencyCode WalletCurrency { get; private set; }
        internal bool PlayingBlocked { get; private set; }

        internal bool IsAccountLimited => AccountFlags.HasFlag(EAccountFlags.LimitedUser) ||
                                          AccountFlags.HasFlag(EAccountFlags.LimitedUserForce);

        internal bool IsAccountLocked => AccountFlags.HasFlag(EAccountFlags.Lockdown);

        private string DeviceID;
        public EAccountFlags AccountFlags { get; private set; }


        // internal bool HasMobileAuthenticator => BotDatabase?.MobileAuthenticator != null;

        private const ProtocolTypes DefaultSteamProtocols = ProtocolTypes.All;
        public ProtocolTypes SteamProtocols { get; private set; } = DefaultSteamProtocols;


        public bool IsConnectedAndLoggedOn => SteamClient?.SteamID != null;


        public Bot([NotNull] string botName, [NotNull] BotConfig botConfig, [NotNull] BotDatabase botDatabase)
        {
            if (string.IsNullOrEmpty(botName) || botConfig == null || botDatabase == null)
                throw new ArgumentNullException(nameof(botName) + " || " + nameof(botConfig) + " || " +
                                                nameof(botDatabase));

            BotName = botName;
            BotConfig = botConfig;
            BotDatabase = botDatabase;

            Logger = new Logger(botName);

            // if (HasMobileAuthenticator) BotDatabase.MobileAuthenticator.Init(this);

            WebHandler = new WebHandler();

            SteamConfiguration = SteamConfiguration.Create(builder =>
                    builder.WithProtocolTypes(SteamProtocols).WithCellID(GlobalDatabase.CellID)
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
            CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
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
            SteamUser = SteamClient.GetHandler<SteamUser>();
            //            CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
            CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
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
                TimeSpan.FromMinutes(1) +
                TimeSpan.FromSeconds(GlobalConfig.LoginLimiterDelay /** Bots.Count*/), // Delay
                TimeSpan.FromMinutes(1) // Period
            );
        }

        public async Task InitModules()
        {
            AccountFlags = EAccountFlags.NormalUser;
            AvatarHash = Nickname = null;
            //MasterChatGroupID = 0;
            WalletBalance = 0;
            WalletCurrency = ECurrencyCode.Invalid;

            //CardsFarmer.SetInitialState(BotConfig.Paused);
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

        internal async Task Start()
        {
            if (KeepRunning) return;

            KeepRunning = true;
            Utilities.InBackground(HandleCallbacks, true);
            Logger.LogGenericInfo(Strings.Starting);

            //      // Support and convert 2FA files
            //      if (!HasMobileAuthenticator) {
            //	        string mobileAuthenticatorFilePath = GetFilePath(EFileType.MobileAuthenticator);

            //	        if (string.IsNullOrEmpty(mobileAuthenticatorFilePath)) {
            //		        ArchiLogger.LogNullError(nameof(mobileAuthenticatorFilePath));

            //		        return;
            //	        }

            //	        if (File.Exists(mobileAuthenticatorFilePath)) {
            //		        await ImportAuthenticator(mobileAuthenticatorFilePath).ConfigureAwait(false);
            //	        }
            //      }

            //      string keysToRedeemFilePath = GetFilePath(EFileType.KeysToRedeem);

            //      if (string.IsNullOrEmpty(keysToRedeemFilePath)) {
            //	        ArchiLogger.LogNullError(nameof(keysToRedeemFilePath));

            //	        return;
            //      }

            //      if (File.Exists(keysToRedeemFilePath)) {
            //	        await ImportKeysToRedeem(keysToRedeemFilePath).ConfigureAwait(false);
            //      }

            await Connect().ConfigureAwait(false);
        }

        private void HandleCallbacks()
        {
            var timeSpan = TimeSpan.FromMilliseconds(CallbackSleep);

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

            var sentryFilePath = Path.Combine(MainDir, "sentry");

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
                if (string.IsNullOrEmpty(BotConfig.SteamPassword) || string.IsNullOrEmpty(AuthCode) &&
                    string.IsNullOrEmpty(TwoFactorCode) /*&& !HasMobileAuthenticator*/)
                    loginKey = BotDatabase.LoginKey;
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

            var username = Regex.Replace(BotConfig.SteamLogin, nonAsciiPattern, "",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            var password = Regex.Replace(BotConfig.SteamPassword, nonAsciiPattern, "",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            Logger.LogGenericInfo(Strings.BotLoggingIn);

            //  if (string.IsNullOrEmpty(TwoFactorCode) && HasMobileAuthenticator
            //  ) // We should always include 2FA token, even if it's not required
            //      TwoFactorCode = await BotDatabase.MobileAuthenticator.GenerateToken().ConfigureAwait(false);

            InitConnectionFailureTimer();

            var logOnDetails = new SteamUser.LogOnDetails
            {
                AuthCode = AuthCode,
                CellID = GlobalDatabase.CellID,
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

        private async void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            if (callback == null)
            {
                Logger.LogNullError(nameof(callback));

                return;
            }

            var lastLogOnResult = LastLogOnResult;
            LastLogOnResult = EResult.Invalid;
            ItemsCount = TradesCount = HeartBeatFailures = 0;
            SteamParentalActive = true;
            StopConnectionFailureTimer();
            StopPlayingWasBlockedTimer();

            Logger.LogGenericInfo(Strings.BotDisconnected);

            OwnedPackageIDs.Clear();

            //Actions.OnDisconnected();
            //ArchiWebHandler.OnDisconnected();
            //CardsFarmer.OnDisconnected(); 
            //Trading.OnDisconnected();

            FirstTradeSent = false;

            //	await PluginsCore.OnBotDisconnected(this, callback.UserInitiated ? EResult.OK : lastLogOnResult).ConfigureAwait(false);

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
                    var authCode = await Logging.GetUserInput(sc.EUserInputType.SteamGuard, BotName)
                        .ConfigureAwait(false);

                    if (string.IsNullOrEmpty(authCode))
                    {
                        Stop();

                        break;
                    }

                    SetUserInput(EUserInputType.SteamGuard, authCode);

                    break;
                case EResult.AccountLoginDeniedNeedTwoFactor:
                    //if (!HasMobileAuthenticator) {
                    //	string twoFactorCode = await Logging.GetUserInput(ASF.EUserInputType.TwoFactorAuthentication, BotName).ConfigureAwait(false);
                    //
                    //	if (string.IsNullOrEmpty(twoFactorCode)) {
                    //		Stop();
                    //
                    //		break;
                    //	}
                    //
                    //	SetUserInput(ASF.EUserInputType.TwoFactorAuthentication, twoFactorCode);
                    //}

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

                    if (callback.CellID != 0 && callback.CellID != GlobalDatabase.CellID)
                        GlobalDatabase.CellID = callback.CellID;

                    // Handle steamID-based maFile
                    //	if (!HasMobileAuthenticator) {
                    //		string maFilePath = Path.Combine(SharedInfo.ConfigDirectory, SteamID + SharedInfo.MobileAuthenticatorExtension);
                    //
                    //		if (File.Exists(maFilePath)) {
                    //			await ImportAuthenticator(maFilePath).ConfigureAwait(false);
                    //		}
                    //	}

                    //	if (callback.ParentalSettings != null) {
                    //		(bool isSteamParentalEnabled, string steamParentalCode) = ValidateSteamParental(callback.ParentalSettings, BotConfig.SteamParentalCode);
                    //
                    //		if (isSteamParentalEnabled) {
                    //			SteamParentalActive = true;
                    //
                    //			if (!string.IsNullOrEmpty(steamParentalCode)) {
                    //				if (BotConfig.SteamParentalCode != steamParentalCode) {
                    //					SetUserInput(ASF.EUserInputType.SteamParentalCode, steamParentalCode);
                    //				}
                    //			} else if (string.IsNullOrEmpty(BotConfig.SteamParentalCode) || (BotConfig.SteamParentalCode.Length != BotConfig.SteamParentalCodeLength)) {
                    //				steamParentalCode = await Logging.GetUserInput(ASF.EUserInputType.SteamParentalCode, BotName).ConfigureAwait(false);
                    //
                    //				if (string.IsNullOrEmpty(steamParentalCode) || (steamParentalCode.Length != BotConfig.SteamParentalCodeLength)) {
                    //					Stop();
                    //
                    //					break;
                    //				}
                    //
                    //				SetUserInput(ASF.EUserInputType.SteamParentalCode, steamParentalCode);
                    //			}
                    //		} else {
                    //			SteamParentalActive = false;
                    //		}
                    //	} else if (SteamParentalActive && !string.IsNullOrEmpty(BotConfig.SteamParentalCode) && (BotConfig.SteamParentalCode.Length != BotConfig.SteamParentalCodeLength)) {
                    //		string steamParentalCode = await Logging.GetUserInput(ASF.EUserInputType.SteamParentalCode, BotName).ConfigureAwait(false);
                    //
                    //		if (string.IsNullOrEmpty(steamParentalCode) || (steamParentalCode.Length != BotConfig.SteamParentalCodeLength)) {
                    //			Stop();
                    //
                    //			break;
                    //		}
                    //
                    //		SetUserInput(ASF.EUserInputType.SteamParentalCode, steamParentalCode);
                    //	}

                    WebHandler.OnVanityURLChanged(callback.VanityURL);

                    if (!await WebHandler.Init(SteamID, SteamClient.Universe, callback.WebAPIUserNonce,
                        SteamParentalActive ? BotConfig.SteamParentalCode : null).ConfigureAwait(false))
                        if (!await RefreshSession().ConfigureAwait(false))
                            break;

                    // Pre-fetch API key for future usage if possible
                    Utilities.InBackground(WebHandler.HasValidApiKey);

                    //	if ((GamesRedeemerInBackgroundTimer == null) && BotDatabase.HasGamesToRedeemInBackground) {
                    //		Utilities.InBackground(RedeemGamesInBackground);
                    //	}

                    SCHandler.SetCurrentMode(2);
                    SCHandler.RequestItemAnnouncements();

                    // Sometimes Steam won't send us our own PersonaStateCallback, so request it explicitly
                    RequestPersonaStateUpdate();

                    //	Utilities.InBackground(InitializeFamilySharing);


                    if (BotConfig.OnlineStatus != EPersonaState.Offline)
                        SteamFriends.SetPersonaState(BotConfig.OnlineStatus);

                    //	if (BotConfig.SteamMasterClanID != 0) {
                    //		Utilities.InBackground(
                    //			async () => {
                    //				if (!await ArchiWebHandler.JoinGroup(BotConfig.SteamMasterClanID).ConfigureAwait(false)) {
                    //					Logger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(ArchiWebHandler.JoinGroup)));
                    //				}
                    //	
                    //				await JoinMasterChatGroupID().ConfigureAwait(false);
                    //			}
                    //		);
                    //	}

                    //await PluginsCore.OnBotLoggedOn(this).ConfigureAwait(false);

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

                    //		if ((callback.Result == EResult.TwoFactorCodeMismatch) && HasMobileAuthenticator) {
                    //			if (++TwoFactorCodeFailures >= MaxTwoFactorCodeFailures) {
                    //				TwoFactorCodeFailures = 0;
                    //				Logger.LogGenericError(string.Format(Strings.BotInvalidAuthenticatorDuringLogin, MaxTwoFactorCodeFailures));
                    //				Stop();
                    //			}
                    //		}

                    break;
                default:
                    // Unexpected result, shutdown immediately
                    Logger.LogGenericError(string.Format(Strings.BotUnableToLogin, callback.Result,
                        callback.ExtendedResult));
                    Stop();

                    break;
            }
        }

        private void StopConnectionFailureTimer()
        {
            if (ConnectionFailureTimer == null) return;

            ConnectionFailureTimer.Dispose();
            ConnectionFailureTimer = null;
        }

        public static async Task<string> ReadAllTextAsync([NotNull] string path)
        {
            return File.ReadAllText(path);
        }

        private void Disconnect()
        {
            StopConnectionFailureTimer();
            SteamClient.Disconnect();
        }

        private void InitPlayingWasBlockedTimer()
        {
            if (PlayingWasBlockedTimer != null) return;

            PlayingWasBlockedTimer = new Timer(
                e => ResetPlayingWasBlockedWithTimer(),
                null,
                TimeSpan.FromSeconds(MinPlayingBlockedTTL), // Delay
                Timeout.InfiniteTimeSpan // Period
            );
        }

        private void ResetPlayingWasBlockedWithTimer()
        {
            PlayingWasBlocked = false;
            StopPlayingWasBlockedTimer();
        }

        private void InitConnectionFailureTimer()
        {
            if (ConnectionFailureTimer != null) return;

            ConnectionFailureTimer = new Timer(
                async e => await InitPermanentConnectionFailure().ConfigureAwait(false),
                null,
                TimeSpan.FromMinutes(Math.Ceiling(GlobalConfig.ConnectionTimeout / 30.0)), // Delay
                Timeout.InfiniteTimeSpan // Period
            );
        }

        private static async Task LimitLoginRequestsAsync()
        {
            if (GlobalConfig.LoginLimiterDelay == 0)
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
                Utilities.InBackground(
                    async () =>
                    {
                        await Task.Delay(GlobalConfig.LoginLimiterDelay * 1000).ConfigureAwait(false);
                        LoginSemaphore.Release();
                    }
                );
            }
        }

        private async Task InitPermanentConnectionFailure()
        {
            if (!KeepRunning) return;

            Logger.LogGenericError(Strings.BotHeartBeatFailed);
            //await Destroy(true).ConfigureAwait(false);
            // await RegisterBot(BotName).ConfigureAwait(false);
        }

        internal void RequestPersonaStateUpdate()
        {
            if (!IsConnectedAndLoggedOn) return;

            SteamFriends.RequestFriendInfo(SteamID,
                EClientPersonaStateFlag.PlayerName | EClientPersonaStateFlag.Presence);
        }

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
                SteamParentalActive ? BotConfig.SteamParentalCode : null).ConfigureAwait(false)) return true;

            await Connect(true).ConfigureAwait(false);

            return false;
        }

        private async Task Connect(bool force = false)
        {
            if (!force && (!KeepRunning || SteamClient.IsConnected)) return;

            await LimitLoginRequestsAsync().ConfigureAwait(false);

            if (!force && (!KeepRunning || SteamClient.IsConnected)) return;

            Logger.LogGenericInfo(Strings.BotConnecting);
            InitConnectionFailureTimer();
            SteamClient.Connect();
        }

        private async Task HeartBeat()
        {
            if (!KeepRunning || !IsConnectedAndLoggedOn || HeartBeatFailures == byte.MaxValue) return;

            try
            {
                if (DateTime.UtcNow.Subtract(SCHandler.LastPacketReceived).TotalSeconds > GlobalConfig.ConnectionTimeout
                ) await SteamFriends.RequestProfileInfo(SteamID);

                HeartBeatFailures = 0;
            }
            catch (Exception e)
            {
                Logger.LogGenericDebuggingException(e);

                if (!KeepRunning || !IsConnectedAndLoggedOn || HeartBeatFailures == byte.MaxValue) return;

                if (++HeartBeatFailures >= (byte) Math.Ceiling(GlobalConfig.ConnectionTimeout / 10.0))
                {
                    HeartBeatFailures = byte.MaxValue;
                    Logger.LogGenericWarning(Strings.BotConnectionLost);
                    Utilities.InBackground(() => Connect(true));
                }
            }
        }

        internal void Stop(bool skipShutdownEvent = false)
        {
            if (!KeepRunning) return;

            KeepRunning = false;
            Logger.LogGenericInfo(Strings.BotStopping);

            if (SteamClient.IsConnected) Disconnect();

            if (!skipShutdownEvent) Utilities.InBackground(Events.OnBotShutdown);
        }

        private void StopPlayingWasBlockedTimer()
        {
            if (PlayingWasBlockedTimer == null) return;

            PlayingWasBlockedTimer.Dispose();
            PlayingWasBlockedTimer = null;
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

        internal void SetUserInput(EUserInputType inputType, string inputValue)
        {
            if (inputType == EUserInputType.Unknown || string.IsNullOrEmpty(inputValue))
                Logger.LogNullError(nameof(inputType) + " || " + nameof(inputValue));

            // This switch should cover ONLY bot properties
            switch (inputType)
            {
                case EUserInputType.DeviceID:
                    DeviceID = inputValue;

                    break;
                case EUserInputType.Login:
                    if (BotConfig != null) BotConfig.SteamLogin = inputValue;

                    break;
                case EUserInputType.Password:
                    if (BotConfig != null) BotConfig.DecryptedSteamPassword = inputValue;

                    break;
                case EUserInputType.SteamGuard:
                    AuthCode = inputValue;

                    break;
                case EUserInputType.SteamParentalCode:
                    if (BotConfig != null) BotConfig.SteamParentalCode = inputValue;

                    break;
                case EUserInputType.TwoFactorAuthentication:
                    TwoFactorCode = inputValue;

                    break;
                default:
                    Logger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(inputType),
                        inputType));

                    break;
            }
        }
    }
}