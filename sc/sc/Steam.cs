using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Acr.UserDialogs;
using JetBrains.Annotations;
using Newtonsoft.Json;
using SteamKit2;
using Xamarin.Essentials;
using Xamarin.Forms;
// ReSharper disable MemberCanBePrivate.Global

namespace sc {
	[SuppressMessage("ReSharper", "UnusedMember.Global")] 
	internal enum EUserInputType : byte {
		Unknown,
		DeviceID,
		Login,
		Password,
		SteamGuard,
		SteamParentalCode,
		TwoFactorAuthentication
	}


	public class Bot {
		internal const ushort CallbackSleep = 500; // In milliseconds
		private const ushort MaxMessageLength = 5000; // This is a limitation enforced by Steam
		private const byte MaxTwoFactorCodeFailures = 3;

		internal const byte MinPlayingBlockedTTL = 60; // Delay in seconds added when account was occupied during our disconnect, to not disconnect other Steam client session too soon

		private const uint LoginID = 1212;
		private const byte LoginCooldownInMinutes = 25; // Captcha disappears after around 20 minutes, so we make it 25


		// internal bool HasMobileAuthenticator => BotDatabase?.MobileAuthenticator != null;

		private const ProtocolTypes DefaultSteamProtocols = ProtocolTypes.All;

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

		internal readonly ConcurrentDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)> OwnedPackageIDs = new ConcurrentDictionary<uint, (EPaymentMethod PaymentMethod, DateTime TimeCreated)>();

		internal readonly SCHandler SCHandler;
		internal readonly SteamApps SteamApps;
		private readonly SteamClient SteamClient;
		public readonly SteamConfiguration SteamConfiguration;
		internal readonly SteamFriends SteamFriends;
		private readonly SteamUser SteamUser;
		public readonly WebHandler WebHandler;

		private string AuthCode;

		#pragma warning disable IDE0052
		[JsonProperty]
		private string AvatarHash;
		#pragma warning restore IDE0052
		private Timer ConnectionFailureTimer;

		private string DeviceID;
		private bool FirstTradeSent;


		private Timer GamesRedeemerInBackgroundTimer;
		private byte HeartBeatFailures;
		private uint ItemsCount;
		private EResult LastLogOnResult;
		private bool LibraryLocked;
		public Logger Logger;

		internal static ConcurrentDictionary<string, Bot> Bots { get; private set; }
		
		private Timer PlayingWasBlockedTimer;

		private bool ReconnectOnUserInitiated;

		//TODO
		private bool SteamParentalActive;
		private uint TradesCount;
		private string TwoFactorCode;
		private byte TwoFactorCodeFailures;

		//public static Bot bot;


		public Bot([NotNull] string botName, [NotNull] BotConfig botConfig, [NotNull] BotDatabase botDatabase) {
			if (string.IsNullOrEmpty(botName) || (botConfig == null) || (botDatabase == null)) {
				throw new ArgumentNullException(nameof(botName) + " || " + nameof(botConfig) + " || " + nameof(botDatabase));
			}

			BotName = botName;
			BotConfig = botConfig;
			BotDatabase = botDatabase;

			Logger = new Logger(botName);

			// if (HasMobileAuthenticator) BotDatabase.MobileAuthenticator.Init(this);

			WebHandler = new WebHandler(this);

			SteamConfiguration = SteamConfiguration.Create(builder => builder.WithProtocolTypes(SteamProtocols).WithCellID(sc.GlobalDatabase.CellID)
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
			//         
			//            SteamApps = SteamClient.GetHandler<SteamApps>();
			//            CallbackManager.Subscribe<SteamApps.GuestPassListCallback>(OnGuestPassList);
			//            CallbackManager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);
			//         
			SteamFriends = SteamClient.GetHandler<SteamFriends>();
			//            CallbackManager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);
			//            CallbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);
			//         
			//            CallbackManager.Subscribe<SteamUnifiedMessages.ServiceMethodNotification>(OnServiceMethod);
			//         
			SteamUser = SteamClient.GetHandler<SteamUser>();
			//            CallbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
			CallbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
			CallbackManager.Subscribe<SteamUser.LoginKeyCallback>(OnLoginKey);
			CallbackManager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);
			//            CallbackManager.Subscribe<SteamUser.WalletInfoCallback>(OnWalletUpdate);

			//CallbackManager.Subscribe<SCHandler.PlayingSessionStateCallback>(OnPlayingSessionState);
			//CallbackManager.Subscribe<SCHandler.SharedLibraryLockStatusCallback>(OnSharedLibraryLockStatus);
			//CallbackManager.Subscribe<SCHandler.UserNotificationsCallback>(OnUserNotifications);
			//CallbackManager.Subscribe<SCHandler.VanityURLChangedCallback>(OnVanityURLChangedCallback);

			//Actions = new Actions(this);
			//CardsFarmer = new CardsFarmer(this);
			//Commands = new Commands(this);
			//Trading = new Trading(this);


			HeartBeatTimer = new Timer(async e => await HeartBeat().ConfigureAwait(false), null, TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(sc.GlobalConfig.LoginLimiterDelay /** Bots.Count*/), // Delay
				TimeSpan.FromMinutes(1) // Period
			);
		}

		public BotConfig BotConfig { get; }

		internal bool PlayingWasBlocked { get; private set; }
		public ulong SteamID { get; private set; }

		public long WalletBalance { get; private set; }
		public bool KeepRunning { get; private set; }
		public string Nickname { get; private set; }
		public ECurrencyCode WalletCurrency { get; private set; }
		internal bool PlayingBlocked { get; private set; }

		internal bool IsAccountLimited => AccountFlags.HasFlag(EAccountFlags.LimitedUser) || AccountFlags.HasFlag(EAccountFlags.LimitedUserForce);

		internal bool IsAccountLocked => AccountFlags.HasFlag(EAccountFlags.Lockdown);
		public EAccountFlags AccountFlags { get; private set; }
		public ProtocolTypes SteamProtocols { get; } = DefaultSteamProtocols;


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
				sc.Logger.LogNullError(nameof(response) + " || " + nameof(botName));

				return null;
			}

			return Environment.NewLine + "<" + botName + "> " + response;
		}

		private void HandleCallbacks() {
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(CallbackSleep);

			while (KeepRunning || SteamClient.IsConnected) {
				if (!CallbackSemaphore.Wait(0)) {
					if (Debugging.IsUserDebugging) {
						Logger.LogGenericDebug(string.Format(Strings.WarningFailedWithError, nameof(CallbackSemaphore)));
					}

					return;
				}

				try {
					CallbackManager.RunWaitAllCallbacks(timeSpan);
				} catch (Exception e) {
					Logger.LogGenericException(e);
				} finally {
					CallbackSemaphore.Release();
				}
			}
		}

		private async Task HeartBeat() {
			if (!KeepRunning || !IsConnectedAndLoggedOn || (HeartBeatFailures == byte.MaxValue)) {
				return;
			}

			try {
				if (DateTime.UtcNow.Subtract(SCHandler.LastPacketReceived).TotalSeconds > sc.GlobalConfig.ConnectionTimeout) {
					await SteamFriends.RequestProfileInfo(SteamID);
				}

				HeartBeatFailures = 0;
			} catch (Exception e) {
				Logger.LogGenericDebuggingException(e);

				if (!KeepRunning || !IsConnectedAndLoggedOn || (HeartBeatFailures == byte.MaxValue)) {
					return;
				}

				if (++HeartBeatFailures >= (byte) Math.Ceiling(sc.GlobalConfig.ConnectionTimeout / 10.0)) {
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

			ConnectionFailureTimer = new Timer(async e => await InitPermanentConnectionFailure().ConfigureAwait(false), null, TimeSpan.FromMinutes(Math.Ceiling(sc.GlobalConfig.ConnectionTimeout / 30.0)), // Delay
				Timeout.InfiniteTimeSpan // Period
			);
		}

		public async Task InitModules() {
			AccountFlags = EAccountFlags.NormalUser;
			AvatarHash = Nickname = null;
			//MasterChatGroupID = 0;
			WalletBalance = 0;
			WalletCurrency = ECurrencyCode.Invalid;

			//CardsFarmer.SetInitialState(BotConfig.Paused);
		}

		private async Task InitPermanentConnectionFailure() {
			if (!KeepRunning) {
				return;
			}

			Logger.LogGenericError(Strings.BotHeartBeatFailed);
			//await Destroy(true).ConfigureAwait(false);
			 await RegisterBot(BotName).ConfigureAwait(false);
		}

		private void InitPlayingWasBlockedTimer() {
			if (PlayingWasBlockedTimer != null) {
				return;
			}

			PlayingWasBlockedTimer = new Timer(e => ResetPlayingWasBlockedWithTimer(), null, TimeSpan.FromSeconds(MinPlayingBlockedTTL), // Delay
				Timeout.InfiniteTimeSpan // Period
			);
		}

		public void InitStart() {
			if (!BotConfig.Enabled) {
				Logger.LogGenericInfo(Strings.BotInstanceNotStartingBecauseDisabled);

				return;
			}

			// Start
			Utilities.InBackground(Start);
		}

		private static async Task LimitLoginRequestsAsync() {
			if (sc.GlobalConfig.LoginLimiterDelay == 0) {
				await LoginRateLimitingSemaphore.WaitAsync().ConfigureAwait(false);
				LoginRateLimitingSemaphore.Release();

				return;
			}

			await LoginSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				await LoginRateLimitingSemaphore.WaitAsync().ConfigureAwait(false);
				LoginRateLimitingSemaphore.Release();
			} finally {
				Utilities.InBackground(async () => {
					await Task.Delay(sc.GlobalConfig.LoginLimiterDelay * 1000).ConfigureAwait(false);
					LoginSemaphore.Release();
				});
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
				if (string.IsNullOrEmpty(BotConfig.SteamPassword) || (string.IsNullOrEmpty(AuthCode) && string.IsNullOrEmpty(TwoFactorCode)) /*&& !HasMobileAuthenticator*/) {
					loginKey = BotDatabase.LoginKey;
				}
			} else {
				// If we're not using login keys, ensure we don't have any saved
				BotDatabase.LoginKey = null;
			}
			

			// Steam login and password fields can contain ASCII characters only, including spaces
			const string nonAsciiPattern = @"[^\u0000-\u007F]+";

			MainPage mainPage = (MainPage) Application.Current.MainPage;
			string username = Regex.Replace(mainPage.Login, nonAsciiPattern, "", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
			string password = Regex.Replace(mainPage.Password, nonAsciiPattern, "", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

			Logger.LogGenericInfo(Strings.BotLoggingIn);
			//TwoFactorCode = mainPage.TwoFactorCode;
			
			//  if (string.IsNullOrEmpty(TwoFactorCode) && HasMobileAuthenticator
			//  ) // We should always include 2FA token, even if it's not required
			//      TwoFactorCode = await BotDatabase.MobileAuthenticator.GenerateToken().ConfigureAwait(false);

			InitConnectionFailureTimer();

			SteamUser.LogOnDetails logOnDetails = new SteamUser.LogOnDetails {
				AuthCode = AuthCode,
				CellID = sc.GlobalDatabase.CellID,
				LoginID = LoginID,
				LoginKey = loginKey,
				Password = password,
				SentryFileHash = sentryFileHash,
				ShouldRememberPassword = BotConfig.UseLoginKeys,
				TwoFactorCode = !string.IsNullOrEmpty(TwoFactorCode) ? TwoFactorCode : null,
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
			//WebHandler.OnDisconnected();
			//CardsFarmer.OnDisconnected(); 
			//Trading.OnDisconnected();

			FirstTradeSent = false;
			
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

					string authCode=null; 
					
					PromptResult aResult = await UserDialogs.Instance.PromptAsync(new PromptConfig
					{
						InputType = InputType.Name,
						OkText = "Enter",
						Title = "Enter your guard code",
						IsCancellable = false,
						MaxLength = 5
					}).ConfigureAwait(false);
					if (aResult.Ok && !string.IsNullOrWhiteSpace(aResult.Text))
					{
						authCode = aResult.Text;
					}
					else
					{
						Stop();	
						break;
					}
					if (string.IsNullOrEmpty(authCode))
					{
					    Stop();
					    break;
					    
					}

					AuthCode = authCode;

					break;
				case EResult.AccountLoginDeniedNeedTwoFactor:
					
					string twoFactorCode=null; 
					PromptResult pResult = await UserDialogs.Instance.PromptAsync(new PromptConfig
					{
						InputType = InputType.Name,
						OkText = "Enter",
						Title = "Enter your guard code",
						IsCancellable = false,
						MaxLength = 5
					}).ConfigureAwait(false);
					if (pResult.Ok && !string.IsNullOrWhiteSpace(pResult.Text))
					{
						twoFactorCode = pResult.Text;
					}
					else
					{
						Stop();	
						break;
					}
					
					if (string.IsNullOrEmpty(twoFactorCode)) {
						Stop();
						break;
					}

					TwoFactorCode = twoFactorCode;
					break;
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

					if ((callback.CellID != 0) && (callback.CellID != sc.GlobalDatabase.CellID)) {
						sc.GlobalDatabase.CellID = callback.CellID;
					}

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

					if (!await WebHandler.Init(SteamID, SteamClient.Universe, callback.WebAPIUserNonce, SteamParentalActive ? BotConfig.SteamParentalCode : null).ConfigureAwait(false)) {
						if (!await RefreshSession().ConfigureAwait(false)) {
							break;
						}
					}

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


					if (BotConfig.OnlineStatus != EPersonaState.Offline) {
						SteamFriends.SetPersonaState(BotConfig.OnlineStatus);
					}
					Device.BeginInvokeOnMainThread(()=>Application.Current.MainPage = new NavigationPage(new second_page()));

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
					Logger.LogGenericError(string.Format(Strings.BotUnableToLogin, callback.Result, callback.ExtendedResult));
					Stop();

					break;
			}
			
			
		}

		private void OnLoginKey(SteamUser.LoginKeyCallback callback) {
			if (string.IsNullOrEmpty(callback?.LoginKey)) {
				Logger.LogNullError(nameof(callback) + " || " + nameof(callback.LoginKey));

				return;
			}

			if (!BotConfig.UseLoginKeys) {
				return;
			}
			string loginKey = callback.LoginKey;
			
			BotDatabase.LoginKey = loginKey;
			SteamUser.AcceptNewLoginKey(callback);
		}
		
		private async void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback) {
			if (callback == null) {
				Logger.LogNullError(nameof(callback));

				return;
			}

			string sentryFilePath = GetFilePath(EFileType.SentryFile);

			sentryFilePath = Path.Combine(MainDir, sentryFilePath);
			if (string.IsNullOrEmpty(sentryFilePath)) {
				Logger.LogNullError(nameof(sentryFilePath));

				return;
			}

			long fileSize;
			byte[] sentryHash;

			try {
				FileStream fileStream = File.Open(sentryFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);

				fileStream.Seek(callback.Offset, SeekOrigin.Begin);

				await fileStream.WriteAsync(callback.Data, 0, callback.BytesToWrite).ConfigureAwait(false);

				fileSize = fileStream.Length;
				fileStream.Seek(0, SeekOrigin.Begin);

				var sha = new SHA1CryptoServiceProvider();

				sentryHash = sha.ComputeHash(fileStream);
			} catch (Exception e) {
				Logger.LogGenericException(e);

				try {
					File.Delete(sentryFilePath);
				} catch {
					// Ignored, we can only try to delete faulted file at best
				}

				return;
			}

			// Inform the steam servers that we're accepting this sentry file
			SteamUser.SendMachineAuthResponse(
				new SteamUser.MachineAuthDetails {
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


		public static string ReadAllTextAsync([NotNull] string path) => File.ReadAllText(path);

		// ReSharper disable once MemberCanBePrivate.Global
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

		// ReSharper disable once MemberCanBePrivate.Global
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

		private static async Task RegisterBot(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				sc.Logger.LogNullError(nameof(botName));

				return;
			}

			//if (Bots.ContainsKey(botName)) {
			//	return;
			//}

			string configFilePath = GetFilePath(botName, EFileType.Config);

			if (string.IsNullOrEmpty(configFilePath)) {
				sc.Logger.LogNullError(nameof(configFilePath));

				return;
			}

			//BotConfig botConfig = await BotConfig.Load(configFilePath).ConfigureAwait(false);
			var botConfig = BotConfig.CreateOrLoad(configFilePath);
			if (botConfig == null) {
				sc.Logger.LogGenericError(string.Format(Strings.ErrorBotConfigInvalid, configFilePath));

				return;
			}

			if (Debugging.IsDebugConfigured) {
				sc.Logger.LogGenericDebug(configFilePath + ": " + JsonConvert.SerializeObject(botConfig, Formatting.Indented));
			}

			string databaseFilePath = GetFilePath(botName, EFileType.Database);

			if (string.IsNullOrEmpty(databaseFilePath)) {
				sc.Logger.LogNullError(nameof(databaseFilePath));

				return;
			}

			BotDatabase botDatabase = await BotDatabase.CreateOrLoad(databaseFilePath).ConfigureAwait(false);

			
			if (botDatabase == null) {
				sc.Logger.LogGenericError(string.Format(Strings.ErrorDatabaseInvalid, databaseFilePath));

				return;
			}

			if (Debugging.IsDebugConfigured) {
				sc.Logger.LogGenericDebug(databaseFilePath + ": " + JsonConvert.SerializeObject(botDatabase, Formatting.Indented));
			}

			Bot bot;
			

			await BotsSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (Bots.ContainsKey(botName)) {
					return;
				}

				bot = new Bot(botName, botConfig, botDatabase);

				if (!Bots.TryAdd(botName, bot)) {
					sc.Logger.LogNullError(nameof(bot));
					bot.Dispose();

					return;
				}
			} finally {
				BotsSemaphore.Release();
			}
			
			await bot.InitModules().ConfigureAwait(false);
			bot.InitStart();
		}
			
			public void Dispose() {
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

			private async Task Start() {
			if (KeepRunning) {
				return;
			}

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

			private void Stop(bool skipShutdownEvent = false) {
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
		internal enum EFileType : byte {
			Config,
			Database,
			//KeysToRedeem,
			//KeysToRedeemUnused,
			//KeysToRedeemUsed,
			MobileAuthenticator,
			SentryFile
		}
		private string GetFilePath(EFileType fileType) {
			if (!Enum.IsDefined(typeof(EFileType), fileType)) {
				sc.Logger.LogNullError(nameof(fileType));

				return null;
			}

			return GetFilePath(BotName, fileType);
		}

		internal static string GetFilePath(string botName, EFileType fileType) {
			if (string.IsNullOrEmpty(botName) || !Enum.IsDefined(typeof(EFileType), fileType)) {
				sc.Logger.LogNullError(nameof(botName) + " || " + nameof(fileType));

				return null;
			}

			string botPath = Path.Combine(SharedInfo.ConfigDirectory, botName);

			switch (fileType) {
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
					sc.Logger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(fileType), fileType));

					return null;
			}
		}

	}
}
