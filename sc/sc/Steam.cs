using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using SteamKit2;
using Xamarin.Essentials;

namespace sc {
	public class Bot {
		private const ushort MaxMessageLength = 5000; // This is a limitation enforced by Steam
		private const byte MaxTwoFactorCodeFailures = 3;
		internal const byte MinPlayingBlockedTTL = 60; // Delay in seconds added when account was occupied during our disconnect, to not disconnect other Steam client session too soon
		private const uint LoginID = 1212;
		private const byte LoginCooldownInMinutes = 25; // Captcha disappears after around 20 minutes, so we make it 25


		// internal bool HasMobileAuthenticator => BotDatabase?.MobileAuthenticator != null;

		private const ProtocolTypes DefaultSteamProtocols = ProtocolTypes.All;
		private static readonly string CacheDir = FileSystem.CacheDirectory;
		private static readonly string MainDir = FileSystem.AppDataDirectory;
		public static readonly Logger Logger;

		private static readonly SemaphoreSlim BotsSemaphore = new SemaphoreSlim(1, 1);
		private static readonly SemaphoreSlim LoginRateLimitingSemaphore = new SemaphoreSlim(1, 1);
		private static readonly SemaphoreSlim LoginSemaphore = new SemaphoreSlim(1, 1);
		internal readonly BotDatabase BotDatabase;
		public readonly string BotName;
		private readonly CallbackManager CallbackManager;

		private readonly Timer HeartBeatTimer;
		internal readonly ConcurrentDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)> OwnedPackageIDs = new ConcurrentDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)>();
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
		private bool LibraryLocked;

		private Timer PlayingWasBlockedTimer;
		private bool ReconnectOnUserInitiated;
		private bool SteamParentalActive = true;
		private uint TradesCount;
		private string TwoFactorCode;
		private byte TwoFactorCodeFailures;


		private Bot([NotNull] string botName, [NotNull] BotConfig botConfig, [NotNull] BotDatabase botDatabase) {
			if (string.IsNullOrEmpty(botName) || (botConfig == null) || (botDatabase == null)) {
				throw new ArgumentNullException(nameof(botName) + " || " + nameof(botConfig) + " || " +
				                                nameof(botDatabase));
			}

			BotName = botName;
			BotConfig = botConfig;
			BotDatabase = botDatabase;

			//ArchiLogger = new ArchiLogger(botName);

			// if (HasMobileAuthenticator) BotDatabase.MobileAuthenticator.Init(this);

			//ArchiWebHandler = new ArchiWebHandler(this);

			SteamConfiguration = SteamConfiguration.Create(builder =>
					builder.WithProtocolTypes(SteamProtocols).WithCellID(GlobalDatabase.CellID)
				/*.WithHttpClientFactory(ArchiWebHandler.GenerateDisposableHttpClient)*/);

			// Initialize
			SteamClient = new SteamClient(SteamConfiguration);

			if (Directory.Exists(MainDir)) {
				string debugListenerPath = Path.Combine(MainDir, "debug", botName);

				try {
					Directory.CreateDirectory(debugListenerPath);
					SteamClient.DebugNetworkListener = new NetHookNetworkListener(debugListenerPath);
				} catch (Exception e) {
					Logger.LogGenericException(e);
				}
			}

			SteamUnifiedMessages steamUnifiedMessages = SteamClient.GetHandler<SteamUnifiedMessages>();

			SCHandler = new SCHandler(Logger, steamUnifiedMessages);
			SteamClient.AddHandler(SCHandler);
			CallbackManager = new CallbackManager(SteamClient);
			CallbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
			CallbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
			// SteamApps = SteamClient.GetHandler<SteamApps>();
			// CallbackManager.Subscribe<SteamApps.GuestPassListCallback>(OnGuestPassList);
			// CallbackManager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);
			// SteamFriends = SteamClient.GetHandler<SteamFriends>();
			// CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);
			// CallbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);
			// CallbackManager.Subscribe<SteamUnifiedMessages.ServiceMethodNotification>(OnServiceMethod);
			// SteamUser = SteamClient.GetHandler<SteamUser>();
			// CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
			CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
			// CallbackManager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);
			// CallbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);
			// CallbackManager.Subscribe<SteamUser.WalletInfoCallback>(OnWalletUpdate);

			// CallbackManager.Subscribe<SCHandler.PlayingSessionStateCallback>(OnPlayingSessionState);
			// CallbackManager.Subscribe<SCHandler.SharedLibraryLockStatusCallback>(OnSharedLibraryLockStatus);
			// CallbackManager.Subscribe<SCHandler.UserNotificationsCallback>(OnUserNotifications);
			// CallbackManager.Subscribe<SCHandler.VanityURLChangedCallback>(OnVanityURLChangedCallback);

			// Actions = new Actions(this);
			// CardsFarmer = new CardsFarmer(this);
			// Commands = new Commands(this);
			// Trading = new Trading(this);


			HeartBeatTimer = new Timer(
				async e => await HeartBeat().ConfigureAwait(false),
				null,
				TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(GlobalConfig.LoginLimiterDelay /** Bots.Count*/), // Delay
				TimeSpan.FromMinutes(1) // Period
			);
		}

		internal bool PlayingWasBlocked { get; private set; }
		public ulong SteamID { get; private set; }
		public bool KeepRunning { get; private set; }
		internal bool PlayingBlocked { get; private set; }
		internal bool IsAccountLimited => AccountFlags.HasFlag(EAccountFlags.LimitedUser) || AccountFlags.HasFlag(EAccountFlags.LimitedUserForce);
		internal bool IsAccountLocked => AccountFlags.HasFlag(EAccountFlags.Lockdown);
		public EAccountFlags AccountFlags { get; private set; }
		public ProtocolTypes SteamProtocols { get; } = DefaultSteamProtocols;


		public BotConfig BotConfig { get; }

		public bool IsConnectedAndLoggedOn => SteamClient?.SteamID != null;

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

		private void Disconnect() {
			StopConnectionFailureTimer();
			SteamClient.Disconnect();
		}

		internal static string FormatBotResponse(string response, string botName) {
			if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(botName)) {
				Logger.LogNullError(nameof(response) + " || " + nameof(botName));

				return null;
			}

			return Environment.NewLine + "<" + botName + "> " + response;
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

		private async Task InitPermanentConnectionFailure() {
			if (!KeepRunning) {
				return;
			}

			Logger.LogGenericError(Strings.BotHeartBeatFailed);
			//await Destroy(true).ConfigureAwait(false);
			// await RegisterBot(BotName).ConfigureAwait(false);
		}

		private void InitPlayingWasBlockedTimer() {
			if (PlayingWasBlockedTimer != null) {
				return;
			}

			PlayingWasBlockedTimer = new Timer(
				e => ResetPlayingWasBlockedWithTimer(),
				null,
				TimeSpan.FromSeconds(MinPlayingBlockedTTL), // Delay
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


		private async void OnConnected(SteamClient.ConnectedCallback callback) {
			if (callback == null) {
				Logger.LogNullError(nameof(callback));


				return;
			}

			HeartBeatFailures = 0;
			ReconnectOnUserInitiated = false;
			StopConnectionFailureTimer();

			Logger.LogGenericInfo(Strings.BotConnected);


			if (!KeepRunning) {
				Logger.LogGenericInfo(Strings.BotDisconnecting);
				Disconnect();

				return;
			}

			string sentryFilePath = Path.Combine(MainDir, "sentry");

			if (string.IsNullOrEmpty(sentryFilePath)) {
				Logger.LogNullError(nameof(sentryFilePath));

				return;
			}

			byte[] sentryFileHash = null;

			if (File.Exists(sentryFilePath)) {
				try {
					byte[] sentryFileContent = File.ReadAllBytes(sentryFilePath);
					sentryFileHash = CryptoHelper.SHAHash(sentryFileContent);
				} catch (Exception e) {
					Logger.LogGenericException(e);

					try {
						File.Delete(sentryFilePath);
					} catch {
						// Ignored, we can only try to delete faulted file at best
					}
				}
			}

			string loginKey = null;

			if (BotConfig.UseLoginKeys) {
				// Login keys are not guaranteed to be valid, we should use them only if we don't have full details available from the user
				if (string.IsNullOrEmpty(BotConfig.SteamPassword) || (string.IsNullOrEmpty(AuthCode) &&
				                                                      string.IsNullOrEmpty(TwoFactorCode)) /*&& !HasMobileAuthenticator*/) {
					loginKey = BotDatabase.LoginKey;
				}
			} else {
				// If we're not using login keys, ensure we don't have any saved
				BotDatabase.LoginKey = null;
			}

			// if (!await InitLoginAndPassword(string.IsNullOrEmpty(loginKey)).ConfigureAwait(false))
			// Stop();
			// return;
			// Steam login and password fields can contain ASCII characters only, including spaces
			const string nonAsciiPattern = @"[^\u0000-\u007F]+";

			string username = Regex.Replace(BotConfig.SteamLogin, nonAsciiPattern, "",
				RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
			string password = BotConfig.DecryptedSteamPassword;

			if (!string.IsNullOrEmpty(password)) {
				password = Regex.Replace(password, nonAsciiPattern, "",
					RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
			}

			Logger.LogGenericInfo(Strings.BotLoggingIn);

			// if (string.IsNullOrEmpty(TwoFactorCode) && HasMobileAuthenticator
			// We should always include 2FA token, even if it's not required
			// TwoFactorCode = await BotDatabase.MobileAuthenticator.GenerateToken().ConfigureAwait(false);

			InitConnectionFailureTimer();

			SteamUser.LogOnDetails logOnDetails = new SteamUser.LogOnDetails {
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

		private async void OnDisconnected(SteamClient.DisconnectedCallback callback) {
			if (callback == null) {
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
			//ArchiWebHandler.OnDisconnected();
			//CardsFarmer.OnDisconnected(); 
			//Trading.OnDisconnected();

			FirstTradeSent = false;

			//	await PluginsCore.OnBotDisconnected(this, callback.UserInitiated ? EResult.OK : lastLogOnResult).ConfigureAwait(false);

			// If we initiated disconnect, do not attempt to reconnect
			if (callback.UserInitiated && !ReconnectOnUserInitiated) {
				return;
			}

			switch (lastLogOnResult) {
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
					Logger.LogGenericInfo(string.Format(Strings.BotRateLimitExceeded, TimeSpan.FromMinutes(LoginCooldownInMinutes).ToHumanReadable()));

					if (!await LoginRateLimitingSemaphore.WaitAsync(1000 * WebBrowser.MaxTries).ConfigureAwait(false)) {
						break;
					}

					try {
						await Task.Delay(LoginCooldownInMinutes * 60 * 1000).ConfigureAwait(false);
					} finally {
						LoginRateLimitingSemaphore.Release();
					}

					break;
			}

			if (!KeepRunning || SteamClient.IsConnected) {
				return;
			}

			Logger.LogGenericInfo(Strings.BotReconnecting);
			await Connect().ConfigureAwait(false);
		}

		private async void OnLoggedOn(SteamUser.LoggedOnCallback callback) {
			if (callback == null) {
				Logger.LogNullError(nameof(callback));

				return;
			}

			// Always reset one-time-only access tokens
			AuthCode = TwoFactorCode = null;

			// Keep LastLogOnResult for OnDisconnected()
			LastLogOnResult = callback.Result;

			HeartBeatFailures = 0;
			StopConnectionFailureTimer();

			switch (callback.Result) {
				case EResult.AccountDisabled:
				case EResult.InvalidPassword when string.IsNullOrEmpty(BotDatabase.LoginKey):
					// Those failures are permanent, we should Stop() the bot if any of those happen
					Logger.LogGenericWarning(string.Format(Strings.BotUnableToLogin, callback.Result, callback.ExtendedResult));
					Stop();

					break;
				case EResult.AccountLogonDenied:
					// TODO: request a 2FA code from user
					string authCode = "";

					if (string.IsNullOrEmpty(authCode)) {
						Stop();
					}

					break;
				case EResult.AccountLoginDeniedNeedTwoFactor:
					//if (!HasMobileAuthenticator) {
					//	string twoFactorCode = await Logging.GetUserInput(ASF.EUserInputType.TwoFactorAuthentication, BotName).ConfigureAwait(false);
					// if (string.IsNullOrEmpty(twoFactorCode)) {
					// Stop();
					// break;
					// SetUserInput(ASF.EUserInputType.TwoFactorAuthentication, twoFactorCode);
					// break;
				case EResult.OK:
					AccountFlags = callback.AccountFlags;
					SteamID = callback.ClientSteamID;

					Logger.LogGenericInfo(string.Format(Strings.BotLoggedOn, SteamID + (!string.IsNullOrEmpty(callback.VanityURL) ? "/" + callback.VanityURL : "")));

					// Old status for these doesn't matter, we'll update them if needed
					TwoFactorCodeFailures = 0;
					LibraryLocked = PlayingBlocked = false;

					if (PlayingWasBlocked && (PlayingWasBlockedTimer == null)) {
						InitPlayingWasBlockedTimer();
					}

					if (IsAccountLimited) {
						Logger.LogGenericWarning(Strings.BotAccountLimited);
					}

					if (IsAccountLocked) {
						Logger.LogGenericWarning(Strings.BotAccountLocked);
					}

					if ((callback.CellID != 0) && (callback.CellID != GlobalDatabase.CellID)) {
						GlobalDatabase.CellID = callback.CellID;
					}

					// Handle steamID-based maFile
					//	if (!HasMobileAuthenticator) {
					// string maFilePath = Path.Combine(SharedInfo.ConfigDirectory, SteamID + SharedInfo.MobileAuthenticatorExtension);
					// if (File.Exists(maFilePath)) {
					// await ImportAuthenticator(maFilePath).ConfigureAwait(false);
					// if (callback.ParentalSettings != null) {
					// bool isSteamParentalEnabled, string steamParentalCode) = ValidateSteamParental(callback.ParentalSettings, BotConfig.SteamParentalCode);
					// if (isSteamParentalEnabled) {
					// SteamParentalActive = true;
					// if (!string.IsNullOrEmpty(steamParentalCode)) {
					// if (BotConfig.SteamParentalCode != steamParentalCode) {
					// SetUserInput(ASF.EUserInputType.SteamParentalCode, steamParentalCode);
					// else if (string.IsNullOrEmpty(BotConfig.SteamParentalCode) || (BotConfig.SteamParentalCode.Length != BotConfig.SteamParentalCodeLength)) {
					// steamParentalCode = await Logging.GetUserInput(ASF.EUserInputType.SteamParentalCode, BotName).ConfigureAwait(false);
					// if (string.IsNullOrEmpty(steamParentalCode) || (steamParentalCode.Length != BotConfig.SteamParentalCodeLength)) {
					// Stop();
					// break;
					// SetUserInput(ASF.EUserInputType.SteamParentalCode, steamParentalCode);
					// else {
					// SteamParentalActive = false;
					// else if (SteamParentalActive && !string.IsNullOrEmpty(BotConfig.SteamParentalCode) && (BotConfig.SteamParentalCode.Length != BotConfig.SteamParentalCodeLength)) {
					// string steamParentalCode = await Logging.GetUserInput(ASF.EUserInputType.SteamParentalCode, BotName).ConfigureAwait(false);
					// if (string.IsNullOrEmpty(steamParentalCode) || (steamParentalCode.Length != BotConfig.SteamParentalCodeLength)) {
					// Stop();
					// break;
					// SetUserInput(ASF.EUserInputType.SteamParentalCode, steamParentalCode);
					// WebHandler.OnVanityURLChanged(callback.VanityURL);

					if (!await WebHandler.Init(SteamID, SteamClient.Universe, callback.WebAPIUserNonce, SteamParentalActive ? BotConfig.SteamParentalCode : null).ConfigureAwait(false)) {
						if (!await RefreshSession().ConfigureAwait(false)) {
							break;
						}
					}

					// Pre-fetch API key for future usage if possible
					Utilities.InBackground(WebHandler.HasValidApiKey);

					//	if ((GamesRedeemerInBackgroundTimer == null) && BotDatabase.HasGamesToRedeemInBackground) {
					// Utilities.InBackground(RedeemGamesInBackground);
					// SCHandler.SetCurrentMode(2);
					SCHandler.RequestItemAnnouncements();

					// Sometimes Steam won't send us our own PersonaStateCallback, so request it explicitly
					RequestPersonaStateUpdate();

					//	Utilities.InBackground(InitializeFamilySharing);


					if (BotConfig.OnlineStatus != EPersonaState.Offline) {
						SteamFriends.SetPersonaState(BotConfig.OnlineStatus);
					}

					//	if (BotConfig.SteamMasterClanID != 0) {
					// Utilities.InBackground(
					// async () => {
					// if (!await ArchiWebHandler.JoinGroup(BotConfig.SteamMasterClanID).ConfigureAwait(false)) {
					// Logger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(ArchiWebHandler.JoinGroup)));
					// await JoinMasterChatGroupID().ConfigureAwait(false);
					// await PluginsCore.OnBotLoggedOn(this).ConfigureAwait(false);

					break;
				case EResult.InvalidPassword:
				case EResult.NoConnection:
				case EResult.PasswordRequiredToKickSession: // Not sure about this one, it seems to be just generic "try again"? #694
				case EResult.RateLimitExceeded:
				case EResult.ServiceUnavailable:
				case EResult.Timeout:
				case EResult.TryAnotherCM:
				case EResult.TwoFactorCodeMismatch:
					Logger.LogGenericWarning(string.Format(Strings.BotUnableToLogin, callback.Result, callback.ExtendedResult));

					// if ((callback.Result == EResult.TwoFactorCodeMismatch) && HasMobileAuthenticator) {
					// if (++TwoFactorCodeFailures >= MaxTwoFactorCodeFailures) {
					// TwoFactorCodeFailures = 0;
					// Logger.LogGenericError(string.Format(Strings.BotInvalidAuthenticatorDuringLogin, MaxTwoFactorCodeFailures));
					// Stop();
					break;
				default:
					// Unexpected result, shutdown immediately
					Logger.LogGenericError(string.Format(Strings.BotUnableToLogin, callback.Result, callback.ExtendedResult));
					Stop();

					break;
			}
		}

		internal async Task<bool> RefreshSession() {
			if (!IsConnectedAndLoggedOn) {
				return false;
			}

			SteamUser.WebAPIUserNonceCallback callback;

			try {
				callback = await SteamUser.RequestWebAPIUserNonce();
			} catch (Exception e) {
				Logger.LogGenericWarningException(e);
				await Connect(true).ConfigureAwait(false);

				return false;
			}

			if (string.IsNullOrEmpty(callback?.Nonce)) {
				await Connect(true).ConfigureAwait(false);

				return false;
			}

			if (await WebHandler.Init(SteamID, SteamClient.Universe, callback.Nonce, SteamParentalActive ? BotConfig.SteamParentalCode : null).ConfigureAwait(false)) {
				return true;
			}

			await Connect(true).ConfigureAwait(false);

			return false;
		}

		internal void RequestPersonaStateUpdate() {
			if (!IsConnectedAndLoggedOn) {
				return;
			}

			SteamFriends.RequestFriendInfo(SteamID, EClientPersonaStateFlag.PlayerName | EClientPersonaStateFlag.Presence);
		}

		private void ResetPlayingWasBlockedWithTimer() {
			PlayingWasBlocked = false;
			StopPlayingWasBlockedTimer();
		}

		internal void Stop(bool skipShutdownEvent = false) {
			if (!KeepRunning) {
				return;
			}

			KeepRunning = false;
			Logger.LogGenericInfo(Strings.BotStopping);

			if (SteamClient.IsConnected) {
				Disconnect();
			}

			if (!skipShutdownEvent) {
				Utilities.InBackground(Events.OnBotShutdown);
			}
		}

		private void StopConnectionFailureTimer() {
			if (ConnectionFailureTimer == null) {
				return;
			}

			ConnectionFailureTimer.Dispose();
			ConnectionFailureTimer = null;
		}

		private void StopPlayingWasBlockedTimer() {
			if (PlayingWasBlockedTimer == null) {
				return;
			}

			PlayingWasBlockedTimer.Dispose();
			PlayingWasBlockedTimer = null;
		}
	}
}
