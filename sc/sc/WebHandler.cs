using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using sc.Helpers;
using SteamKit2;

namespace sc {
	public class WebHandler
	{
		public const string SteamCommunityURL = "https://" + SteamCommunityHost;
		public const string SteamHelpURL = "https://" + SteamHelpHost;
		public const string SteamStoreURL = "https://" + SteamStoreHost;

		private const string IEconService = "IEconService";
		private const string IPlayerService = "IPlayerService";
		private const string ISteamApps = "ISteamApps";
		private const string ISteamUserAuth = "ISteamUserAuth";
		private const string ITwoFactorService = "ITwoFactorService";
		private const ushort MaxItemsInSingleInventoryRequest = 5000;
		private const byte MinSessionValidityInSeconds = GlobalConfig.DefaultConnectionTimeout / 6;
		private const string SteamCommunityHost = "steamcommunity.com";
		private const string SteamHelpHost = "help.steampowered.com";
		private const string SteamStoreHost = "store.steampowered.com";

		public WebBrowser WebBrowser;


		private static readonly
			ImmutableDictionary<string, (SemaphoreSlim RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore)>
			WebLimitingSemaphores =
				new Dictionary<string, (SemaphoreSlim RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore)>(4,
					StringComparer.Ordinal)
				{
					{
						nameof(WebHandler),
						(new SemaphoreSlim(1, 1),
							new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections))
					},
					{
						SteamCommunityURL,
						(new SemaphoreSlim(1, 1),
							new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections))
					},
					{
						SteamHelpURL,
						(new SemaphoreSlim(1, 1),
							new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections))
					},
					{
						SteamStoreURL,
						(new SemaphoreSlim(1, 1),
							new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections))
					},
					{
						WebAPI.DefaultBaseAddress.Host,
						(new SemaphoreSlim(1, 1),
							new SemaphoreSlim(WebBrowser.MaxConnections, WebBrowser.MaxConnections))
					}
				}.ToImmutableDictionary(StringComparer.Ordinal);

		private readonly Bot Bot;
		public readonly ArchiCacheable<string> CachedApiKey;
		private bool Initialized;
		private DateTime LastSessionCheck;
		private DateTime LastSessionRefresh;
		private string VanityURL;

		public WebHandler(Bot bot)
		{
			Bot = bot ?? throw new ArgumentNullException(nameof(bot));
			WebBrowser = new WebBrowser(bot.Logger);
			
		}
		public void Dispose() {
			CachedApiKey.Dispose();
			//CachedPublicInventory.Dispose();
			//SessionSemaphore.Dispose();
			WebBrowser.Dispose();
		}

	private Logger Logger => Bot.Logger;

		public async Task<string> GetAbsoluteProfileURL(bool waitForInitialization = true) {
			if (waitForInitialization && !Initialized) {
				for (byte i = 0; (i < sc.GlobalConfig.ConnectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (!Initialized) {
					Logger.LogGenericWarning(Strings.WarningFailed);

					return null;
				}
			}

			return string.IsNullOrEmpty(VanityURL) ? "/profiles/" + Bot.SteamID : "/id/" + VanityURL;
		}

		public async Task<bool?> HasValidApiKey() {
			(bool success, string steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

			return success ? !string.IsNullOrEmpty(steamApiKey) : (bool?) null;
		}

		internal async Task<bool> Init(ulong steamID, EUniverse universe, string webAPIUserNonce, string parentalCode = null) {
			if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount || (universe == EUniverse.Invalid) || !Enum.IsDefined(typeof(EUniverse), universe) || string.IsNullOrEmpty(webAPIUserNonce)) {
				Logger.LogNullError(nameof(steamID) + " || " + nameof(universe) + " || " + nameof(webAPIUserNonce));

				return false;
			}

			string sessionID = Convert.ToBase64String(Encoding.UTF8.GetBytes(steamID.ToString()));


			// Generate a random 32-byte session key
			byte[] sessionKey = CryptoHelper.GenerateRandomBlock(32);

			// RSA encrypt our session key with the public key for the universe we're on
			byte[] encryptedSessionKey;

			using (RSACrypto rsa = new RSACrypto(KeyDictionary.GetPublicKey(universe))) {
				encryptedSessionKey = rsa.Encrypt(sessionKey);
			}

			// Generate login key from the user nonce that we've received from Steam network
			byte[] loginKey = Encoding.UTF8.GetBytes(webAPIUserNonce);

			// AES encrypt our login key with our session key
			byte[] encryptedLoginKey = CryptoHelper.SymmetricEncrypt(loginKey, sessionKey);

			// We're now ready to send the data to Steam API
			Logger.LogGenericInfo(string.Format(Strings.LoggingIn, ISteamUserAuth));

			KeyValue response;

			// We do not use usual retry pattern here as webAPIUserNonce is valid only for a single request
			// Even during timeout, webAPIUserNonce is most likely already invalid
			// Instead, the caller is supposed to ask for new webAPIUserNonce and call Init() again on failure
			using (WebAPI.AsyncInterface iSteamUserAuth = Bot.SteamConfiguration.GetAsyncWebAPIInterface(ISteamUserAuth)) {
				iSteamUserAuth.Timeout = WebBrowser.Timeout;

				try {
					response = await WebLimitRequest(WebAPI.DefaultBaseAddress.Host,

						// ReSharper disable once AccessToDisposedClosure
						async () => await iSteamUserAuth.CallAsync(HttpMethod.Post, "AuthenticateUser", args: new Dictionary<string, object>(3, StringComparer.Ordinal) {
							{"encrypted_loginkey", encryptedLoginKey},
							{"sessionkey", encryptedSessionKey},
							{"steamid", steamID}
						}).ConfigureAwait(false)).ConfigureAwait(false);
				} catch (TaskCanceledException e) {
					Logger.LogGenericDebuggingException(e);

					return false;
				} catch (Exception e) {
					Logger.LogGenericWarningException(e);

					return false;
				}
			}

			if (response == null) {
				return false;
			}

			string steamLogin = response["token"].AsString();

			if (string.IsNullOrEmpty(steamLogin)) {
				Logger.LogNullError(nameof(steamLogin));

				return false;
			}

			string steamLoginSecure = response["tokensecure"].AsString();

			if (string.IsNullOrEmpty(steamLoginSecure)) {
				Logger.LogNullError(nameof(steamLoginSecure));

				return false;
			}

			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamHelpHost));
			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamStoreHost));

			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamHelpHost));
			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamStoreHost));

			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamHelpHost));
			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamStoreHost));

			// Report proper time when doing timezone-based calculations, see setTimezoneCookies() from https://steamcommunity-a.akamaihd.net/public/shared/javascript/shared_global.js
			string timeZoneOffset = DateTimeOffset.Now.Offset.TotalSeconds + WebUtility.UrlEncode(",") + "0";

			WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", "." + SteamHelpHost));
			WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", "." + SteamStoreHost));

			WebBrowser.Cookies.Add(new MyCookie(sessionID,"sessionid"));
			WebBrowser.Cookies.Add(new MyCookie(steamLogin,"steamLogin"));
			WebBrowser.Cookies.Add(new MyCookie(steamLoginSecure,"steamLoginSecure"));
			WebBrowser.Cookies.Add(new MyCookie(timeZoneOffset,"timezoneOffset"));
			Logger.LogGenericInfo(Strings.Success);

			// Unlock Steam Parental if needed
			if ((parentalCode != null) && (parentalCode.Length == 4)) {
				if (!await UnlockParentalAccount(parentalCode).ConfigureAwait(false)) {
					return false;
				}
			}

			LastSessionCheck = LastSessionRefresh = DateTime.UtcNow;
			Initialized = true;

			return true;
		}

		private async Task<bool> IsProfileUri(Uri uri, bool waitForInitialization = true) {
			if (uri == null) {
				Logger.LogNullError(nameof(uri));

				return false;
			}

			string profileURL = await GetAbsoluteProfileURL(waitForInitialization).ConfigureAwait(false);

			if (string.IsNullOrEmpty(profileURL)) {
				Logger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			return uri.AbsolutePath.Equals(profileURL);
		}

		private static bool IsSessionExpiredUri(Uri uri) {
			if (uri == null) {
				sc.Logger.LogNullError(nameof(uri));

				return false;
			}

			return uri.AbsolutePath.StartsWith("/login", StringComparison.Ordinal) || uri.Host.Equals("lostauth");
		}

		internal void OnVanityURLChanged(string vanityURL = null) {
			VanityURL = !string.IsNullOrEmpty(vanityURL) ? vanityURL : null;
		}

		private async Task<bool> UnlockParentalAccount(string parentalCode) {
			if (string.IsNullOrEmpty(parentalCode)) {
				Logger.LogNullError(nameof(parentalCode));

				return false;
			}

			Logger.LogGenericInfo(Strings.UnlockingParentalAccount);

			if (!await UnlockParentalAccountForService(SteamCommunityURL, parentalCode).ConfigureAwait(false)) {
				Logger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			if (!await UnlockParentalAccountForService(SteamStoreURL, parentalCode).ConfigureAwait(false)) {
				Logger.LogGenericWarning(Strings.WarningFailed);

				return false;
			}

			Logger.LogGenericInfo(Strings.Success);

			return true;
		}


		private async Task<bool> UnlockParentalAccountForService(string serviceURL, string parentalCode, byte maxTries = WebBrowser.MaxTries) {
			if (string.IsNullOrEmpty(serviceURL) || string.IsNullOrEmpty(parentalCode)) {
				Logger.LogNullError(nameof(serviceURL) + " || " + nameof(parentalCode));

				return false;
			}

			const string request = "/parental/ajaxunlock";

			if (maxTries == 0) {
				Logger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, serviceURL + request));

				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(serviceURL, "sessionid");

			if (string.IsNullOrEmpty(sessionID)) {
				Logger.LogNullError(nameof(sessionID));

				return false;
			}

			Dictionary<string, string> data = new Dictionary<string, string>(2, StringComparer.Ordinal) {
				{"pin", parentalCode},
				{"sessionid", sessionID}
			};

			// This request doesn't go through UrlPostRetryWithSession as we have no access to session refresh capability (this is in fact session initialization)
			WebBrowser.BasicResponse response = await WebLimitRequest(serviceURL, async () => await WebBrowser.UrlPost(serviceURL + request, data, serviceURL).ConfigureAwait(false)).ConfigureAwait(false);

			if ((response == null) || IsSessionExpiredUri(response.FinalUri)) // There is no session refresh capability at this stage
			{
				return false;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri, false).ConfigureAwait(false)) {
				Logger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UnlockParentalAccountForService(serviceURL, parentalCode, --maxTries).ConfigureAwait(false);
			}

			return true;
		}

		public static async Task<T> WebLimitRequest<T>(string service, Func<Task<T>> function) {
			if (string.IsNullOrEmpty(service) || (function == null)) {
				sc.Logger.LogNullError(nameof(service) + " || " + nameof(function));

				return default;
			}

			if (sc.GlobalConfig.WebLimiterDelay == 0) {
				return await function().ConfigureAwait(false);
			}

			if (!WebLimitingSemaphores.TryGetValue(service, out (SemaphoreSlim RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore) limiters)) {
				sc.Logger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(service), service));

				if (!WebLimitingSemaphores.TryGetValue(nameof(WebHandler), out limiters)) {
					sc.Logger.LogNullError(nameof(limiters));

					return await function().ConfigureAwait(false);
				}
			}

			// Sending a request opens a new connection
			await limiters.OpenConnectionsSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				// It also increases number of requests
				await limiters.RateLimitingSemaphore.WaitAsync().ConfigureAwait(false);

				// We release rate-limiter semaphore regardless of our task completion, since we use that one only to guarantee rate-limiting of their creation
				Utilities.InBackground(async () => {
					await Task.Delay(sc.GlobalConfig.WebLimiterDelay).ConfigureAwait(false);
					limiters.RateLimitingSemaphore.Release();
				});

				return await function().ConfigureAwait(false);
			} finally {
				// We release open connections semaphore only once we're indeed done sending a particular request
				limiters.OpenConnectionsSemaphore.Release();
			}
		}
	}
}
