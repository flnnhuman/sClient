using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using JetBrains.Annotations;
using SteamKit2;

namespace sc
{
    public class WebHandler
    {
        public enum ESession : byte
        {
            None,
            Lowercase,
            CamelCase,
            PascalCase
        }

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

        private readonly SemaphoreSlim SessionSemaphore = new SemaphoreSlim(1, 1);
        private bool Initialized;
        private DateTime LastSessionCheck;
        private DateTime LastSessionRefresh;
        private string VanityURL;

        public WebBrowser WebBrowser;

        public WebHandler([NotNull] Bot bot)
        {
            CachedApiKey = new ArchiCacheable<string>(ResolveApiKey);
            Bot = bot ?? throw new ArgumentNullException(nameof(bot));
            WebBrowser = new WebBrowser(bot.Logger);
        }

        private Logger Logger => Bot.Logger;

        public void Dispose()
        {
            CachedApiKey.Dispose();
            //CachedPublicInventory.Dispose();
            //SessionSemaphore.Dispose();
            WebBrowser.Dispose();
        }

        public async Task<string> GetAbsoluteProfileURL(bool waitForInitialization = true)
        {
            if (!waitForInitialization || Initialized)
                return string.IsNullOrEmpty(VanityURL) ? "/profiles/" + Bot.SteamID : "/id/" + VanityURL;
            for (byte i = 0; i < sc.GlobalConfig.ConnectionTimeout && !Initialized && Bot.IsConnectedAndLoggedOn; i++)
                await Task.Delay(1000).ConfigureAwait(false);

            if (Initialized)
                return string.IsNullOrEmpty(VanityURL) ? "/profiles/" + Bot.SteamID : "/id/" + VanityURL;
            Logger.LogGenericWarning(Strings.WarningFailed);

            return null;
        }

        public async Task<bool?> HasValidApiKey()
        {
            var (success, steamApiKey) = await CachedApiKey.GetValue().ConfigureAwait(false);

            return success ? !string.IsNullOrEmpty(steamApiKey) : (bool?) null;
        }

        internal HttpClient GenerateDisposableHttpClient()
        {
            return WebBrowser.GenerateDisposableHttpClient();
        }

        internal async Task<bool> Init(ulong steamID, EUniverse universe, string webAPIUserNonce,
            string parentalCode = null)
        {
            if (steamID == 0 || !new SteamID(steamID).IsIndividualAccount || universe == EUniverse.Invalid ||
                !Enum.IsDefined(typeof(EUniverse), universe) || string.IsNullOrEmpty(webAPIUserNonce))
            {
                Logger.LogNullError(nameof(steamID) + " || " + nameof(universe) + " || " + nameof(webAPIUserNonce));

                return false;
            }

            var sessionID = Convert.ToBase64String(Encoding.UTF8.GetBytes(steamID.ToString()));


            // Generate a random 32-byte session key
            var sessionKey = CryptoHelper.GenerateRandomBlock(32);

            // RSA encrypt our session key with the public key for the universe we're on
            byte[] encryptedSessionKey;

            using (var rsa = new RSACrypto(KeyDictionary.GetPublicKey(universe)))
            {
                encryptedSessionKey = rsa.Encrypt(sessionKey);
            }

            // Generate login key from the user nonce that we've received from Steam network
            var loginKey = Encoding.UTF8.GetBytes(webAPIUserNonce);

            // AES encrypt our login key with our session key
            var encryptedLoginKey = CryptoHelper.SymmetricEncrypt(loginKey, sessionKey);

            // We're now ready to send the data to Steam API
            Logger.LogGenericInfo(string.Format(Strings.LoggingIn, ISteamUserAuth));

            KeyValue response;

            // We do not use usual retry pattern here as webAPIUserNonce is valid only for a single request
            // Even during timeout, webAPIUserNonce is most likely already invalid
            // Instead, the caller is supposed to ask for new webAPIUserNonce and call Init() again on failure
            using (WebAPI.AsyncInterface iSteamUserAuth = Bot.SteamConfiguration.GetAsyncWebAPIInterface(ISteamUserAuth)
            )
            {
                iSteamUserAuth.Timeout = WebBrowser.Timeout;

                try
                {
                    response = await WebLimitRequest(WebAPI.DefaultBaseAddress.Host,

                        // ReSharper disable once AccessToDisposedClosure
                        async () => await iSteamUserAuth.CallAsync(HttpMethod.Post, "AuthenticateUser",
                            args: new Dictionary<string, object>(3, StringComparer.Ordinal)
                            {
                                {"encrypted_loginkey", encryptedLoginKey},
                                {"sessionkey", encryptedSessionKey},
                                {"steamid", steamID}
                            }).ConfigureAwait(false)).ConfigureAwait(false);
                }
                catch (TaskCanceledException e)
                {
                    Logger.LogGenericDebuggingException(e);

                    return false;
                }
                catch (Exception e)
                {
                    Logger.LogGenericWarningException(e);

                    return false;
                }
            }

            if (response == null) return false;

            var steamLogin = response["token"].AsString();

            if (string.IsNullOrEmpty(steamLogin))
            {
                Logger.LogNullError(nameof(steamLogin));

                return false;
            }

            var steamLoginSecure = response["tokensecure"].AsString();

            if (string.IsNullOrEmpty(steamLoginSecure))
            {
                Logger.LogNullError(nameof(steamLoginSecure));

                return false;
            }

            WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamCommunityHost));
            WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamHelpHost));
            WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamStoreHost));

            WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamCommunityHost));
            WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamHelpHost));
            WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamStoreHost));

            WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/",
                "." + SteamCommunityHost));
            WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamHelpHost));
            WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamStoreHost));

            // Report proper time when doing timezone-based calculations, see setTimezoneCookies() from https://steamcommunity-a.akamaihd.net/public/shared/javascript/shared_global.js
            var timeZoneOffset = DateTimeOffset.Now.Offset.TotalSeconds + WebUtility.UrlEncode(",") + "0";

            WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", "." + SteamCommunityHost));
            WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", "." + SteamHelpHost));
            WebBrowser.CookieContainer.Add(new Cookie("timezoneOffset", timeZoneOffset, "/", "." + SteamStoreHost));

            WebBrowser.Cookies.Add(new MyCookie(sessionID, "sessionid"));
            WebBrowser.Cookies.Add(new MyCookie(steamLogin, "steamLogin"));
            WebBrowser.Cookies.Add(new MyCookie(steamLoginSecure, "steamLoginSecure"));
            WebBrowser.Cookies.Add(new MyCookie(timeZoneOffset, "timezoneOffset"));
            Logger.LogGenericInfo(Strings.Success);

            // Unlock Steam Parental if needed
            if (parentalCode != null && parentalCode.Length == 4)
                if (!await UnlockParentalAccount(parentalCode).ConfigureAwait(false))
                    return false;

            LastSessionCheck = LastSessionRefresh = DateTime.UtcNow;
            Initialized = true;

            return true;
        }

        private async Task<bool> IsProfileUri(Uri uri, bool waitForInitialization = true)
        {
            if (uri == null)
            {
                Logger.LogNullError(nameof(uri));

                return false;
            }

            var profileURL = await GetAbsoluteProfileURL(waitForInitialization).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(profileURL)) return uri.AbsolutePath.Equals(profileURL);
            Logger.LogGenericWarning(Strings.WarningFailed);

            return false;
        }

        private static bool IsSessionExpiredUri(Uri uri)
        {
            if (uri != null)
                return uri.AbsolutePath.StartsWith("/login", StringComparison.Ordinal) || uri.Host.Equals("lostauth");
            sc.Logger.LogNullError(nameof(uri));

            return false;
        }

        internal void OnVanityURLChanged(string vanityURL = null)
        {
            VanityURL = !string.IsNullOrEmpty(vanityURL) ? vanityURL : null;
        }

        private async Task<bool> UnlockParentalAccount(string parentalCode)
        {
            if (string.IsNullOrEmpty(parentalCode))
            {
                Logger.LogNullError(nameof(parentalCode));

                return false;
            }

            Logger.LogGenericInfo(Strings.UnlockingParentalAccount);

            if (!await UnlockParentalAccountForService(SteamCommunityURL, parentalCode).ConfigureAwait(false))
            {
                Logger.LogGenericWarning(Strings.WarningFailed);

                return false;
            }

            if (!await UnlockParentalAccountForService(SteamStoreURL, parentalCode).ConfigureAwait(false))
            {
                Logger.LogGenericWarning(Strings.WarningFailed);

                return false;
            }

            Logger.LogGenericInfo(Strings.Success);

            return true;
        }


        private async Task<bool> UnlockParentalAccountForService(string serviceURL, string parentalCode,
            byte maxTries = WebBrowser.MaxTries)
        {
            if (string.IsNullOrEmpty(serviceURL) || string.IsNullOrEmpty(parentalCode))
            {
                Logger.LogNullError(nameof(serviceURL) + " || " + nameof(parentalCode));

                return false;
            }

            const string request = "/parental/ajaxunlock";

            if (maxTries == 0)
            {
                Logger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
                Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, serviceURL + request));

                return false;
            }

            var sessionID = WebBrowser.CookieContainer.GetCookieValue(serviceURL, "sessionid");

            if (string.IsNullOrEmpty(sessionID))
            {
                Logger.LogNullError(nameof(sessionID));

                return false;
            }

            var data = new Dictionary<string, string>(2, StringComparer.Ordinal)
            {
                {"pin", parentalCode},
                {"sessionid", sessionID}
            };

            // This request doesn't go through UrlPostRetryWithSession as we have no access to session refresh capability (this is in fact session initialization)
            BasicResponse response = await WebLimitRequest(serviceURL,
                    async () => await WebBrowser.UrlPost(serviceURL + request, data, serviceURL).ConfigureAwait(false))
                .ConfigureAwait(false);

            if (response == null || IsSessionExpiredUri(response.FinalUri)
            ) // There is no session refresh capability at this stage
                return false;

            // Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
            if (!await IsProfileUri(response.FinalUri, false).ConfigureAwait(false)) return true;
            Logger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

            return await UnlockParentalAccountForService(serviceURL, parentalCode, --maxTries).ConfigureAwait(false);
        }

        internal void OnDisconnected()
        {
            Initialized = false;
            Utilities.InBackground(CachedApiKey.Reset);
            // TODO Utilities.InBackground(CachedPublicInventory.Reset);
        }

        public static async Task<T> WebLimitRequest<T>(string service, Func<Task<T>> function)
        {
            if (string.IsNullOrEmpty(service) || function == null)
            {
                sc.Logger.LogNullError(nameof(service) + " || " + nameof(function));

                return default;
            }

            if (sc.GlobalConfig.WebLimiterDelay == 0) return await function().ConfigureAwait(false);

            if (!WebLimitingSemaphores.TryGetValue(service,
                out (SemaphoreSlim RateLimitingSemaphore, SemaphoreSlim OpenConnectionsSemaphore) limiters))
            {
                sc.Logger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(service),
                    service));

                if (!WebLimitingSemaphores.TryGetValue(nameof(WebHandler), out limiters))
                {
                    sc.Logger.LogNullError(nameof(limiters));

                    return await function().ConfigureAwait(false);
                }
            }

            // Sending a request opens a new connection
            await limiters.OpenConnectionsSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                // It also increases number of requests
                await limiters.RateLimitingSemaphore.WaitAsync().ConfigureAwait(false);

                // We release rate-limiter semaphore regardless of our task completion, since we use that one only to guarantee rate-limiting of their creation
                Utilities.InBackground(async () =>
                {
                    await Task.Delay(sc.GlobalConfig.WebLimiterDelay).ConfigureAwait(false);
                    limiters.RateLimitingSemaphore.Release();
                });

                return await function().ConfigureAwait(false);
            }
            finally
            {
                // We release open connections semaphore only once we're indeed done sending a particular request
                limiters.OpenConnectionsSemaphore.Release();
            }
        }

        private async Task<bool> RegisterApiKey()
        {
            const string request = "/dev/registerkey";

            // Extra entry for sessionID
            var data = new Dictionary<string, string>(4, StringComparer.Ordinal)
            {
                {"agreeToTerms", "agreed"},
                {"domain", "localhost"},
                {"Submit", "Register"}
            };

            return await UrlPostWithSession(SteamCommunityURL, request, data).ConfigureAwait(false);
        }

        public async Task<bool> UrlPostWithSession(string host, string request, Dictionary<string, string> data = null,
            string referer = null, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true,
            byte maxTries = WebBrowser.MaxTries)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request) ||
                !Enum.IsDefined(typeof(ESession), session))
            {
                Bot.Logger.LogNullError(nameof(host) + " || " + nameof(request) + " || " + nameof(session));

                return false;
            }

            if (maxTries == 0)
            {
                Bot.Logger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes,
                    WebBrowser.MaxTries));
                Bot.Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

                return false;
            }

            if (checkSessionPreemptively)
            {
                // Check session preemptively as this request might not get redirected to expiration
                var sessionExpired = await IsSessionExpired().ConfigureAwait(false);

                if (sessionExpired.GetValueOrDefault(true))
                {
                    if (await RefreshSession().ConfigureAwait(false))
                        return await UrlPostWithSession(host, request, data, referer, session, true, --maxTries)
                            .ConfigureAwait(false);

                    Bot.Logger.LogGenericWarning(Strings.WarningFailed);
                    Bot.Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

                    return false;
                }
            }
            else
            {
                // If session refresh is already in progress, just wait for it
                await SessionSemaphore.WaitAsync().ConfigureAwait(false);
                SessionSemaphore.Release();
            }

            if (!Initialized)
            {
                for (byte i = 0;
                    i < sc.GlobalConfig.ConnectionTimeout && !Initialized && Bot.IsConnectedAndLoggedOn;
                    i++) await Task.Delay(1000).ConfigureAwait(false);

                if (!Initialized)
                {
                    Bot.Logger.LogGenericWarning(Strings.WarningFailed);
                    Bot.Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

                    return false;
                }
            }

            if (session != ESession.None)
            {
                var sessionID = WebBrowser.CookieContainer.GetCookieValue(host, "sessionid");

                if (string.IsNullOrEmpty(sessionID))
                {
                    Bot.Logger.LogNullError(nameof(sessionID));

                    return false;
                }

                string sessionName;

                switch (session)
                {
                    case ESession.CamelCase:
                        sessionName = "sessionID";

                        break;
                    case ESession.Lowercase:
                        sessionName = "sessionid";

                        break;
                    case ESession.PascalCase:
                        sessionName = "SessionID";

                        break;
                    default:
                        Bot.Logger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport,
                            nameof(session), session));

                        return false;
                }

                if (data != null)
                    data[sessionName] = sessionID;
                else
                    data = new Dictionary<string, string>(1, StringComparer.Ordinal) {{sessionName, sessionID}};
            }

            BasicResponse response = await WebLimitRequest(host,
                    async () => await WebBrowser.UrlPost(host + request, data, referer).ConfigureAwait(false))
                .ConfigureAwait(false);

            if (response == null) return false;

            if (IsSessionExpiredUri(response.FinalUri))
            {
                if (await RefreshSession().ConfigureAwait(false))
                    return await UrlPostWithSession(host, request, data, referer, session, checkSessionPreemptively,
                        --maxTries).ConfigureAwait(false);

                Bot.Logger.LogGenericWarning(Strings.WarningFailed);
                Bot.Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

                return false;
            }

            // Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
            if (!await IsProfileUri(response.FinalUri).ConfigureAwait(false)) return true;
            Bot.Logger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

            return await UrlPostWithSession(host, request, data, referer, session, checkSessionPreemptively, --maxTries)
                .ConfigureAwait(false);
        }

        private async Task<(bool Success, string Result)> ResolveApiKey()
        {
            if (Bot.IsAccountLimited) // API key is permanently unavailable for limited accounts
                return (true, null);

            (ESteamApiKeyState State, string Key) result = await GetApiKeyState().ConfigureAwait(false);

            switch (result.State)
            {
                case ESteamApiKeyState.AccessDenied:
                    // We succeeded in fetching API key, but it resulted in access denied
                    // Return empty result, API key is unavailable permanently
                    return (true, "");
                case ESteamApiKeyState.NotRegisteredYet:
                    // We succeeded in fetching API key, and it resulted in no key registered yet
                    // Let's try to register a new key
                    if (!await RegisterApiKey().ConfigureAwait(false)
                    ) // Request timed out, bad luck, we'll try again later
                        goto case ESteamApiKeyState.Timeout;

                    // We should have the key ready, so let's fetch it again
                    result = await GetApiKeyState().ConfigureAwait(false);

                    if (result.State == ESteamApiKeyState.Timeout) // Request timed out, bad luck, we'll try again later
                        goto case ESteamApiKeyState.Timeout;

                    if (result.State != ESteamApiKeyState.Registered) // Something went wrong, report error
                        goto default;

                    goto case ESteamApiKeyState.Registered;
                case ESteamApiKeyState.Registered:
                    // We succeeded in fetching API key, and it resulted in registered key
                    // Cache the result, this is the API key we want
                    return (true, result.Key);
                case ESteamApiKeyState.Timeout:
                    // Request timed out, bad luck, we'll try again later
                    return (false, null);
                default:
                    // We got an unhandled error, this should never happen
                    Bot.Logger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport,
                        nameof(result.State), result.State));

                    return (false, null);
            }
        }

        public async Task<HtmlDocument> UrlGetToHtmlDocumentWithSession(string host, string request,
            bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries)
        {
            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request))
            {
                Bot.Logger.LogNullError(nameof(host) + " || " + nameof(request));

                return null;
            }

            if (maxTries == 0)
            {
                Bot.Logger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes,
                    WebBrowser.MaxTries));
                Bot.Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

                return null;
            }

            if (checkSessionPreemptively)
            {
                // Check session preemptively as this request might not get redirected to expiration
                var sessionExpired = await IsSessionExpired().ConfigureAwait(false);

                if (sessionExpired.GetValueOrDefault(true))
                {
                    if (await RefreshSession().ConfigureAwait(false))
                        return await UrlGetToHtmlDocumentWithSession(host, request, true, --maxTries)
                            .ConfigureAwait(false);

                    Bot.Logger.LogGenericWarning(Strings.WarningFailed);
                    Bot.Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

                    return null;
                }
            }
            else
            {
                // If session refresh is already in progress, just wait for it
                await SessionSemaphore.WaitAsync().ConfigureAwait(false);
                SessionSemaphore.Release();
            }

            if (!Initialized)
            {
                for (byte i = 0;
                    i < sc.GlobalConfig.ConnectionTimeout && !Initialized && Bot.IsConnectedAndLoggedOn;
                    i++) await Task.Delay(1000).ConfigureAwait(false);

                if (!Initialized)
                {
                    Bot.Logger.LogGenericWarning(Strings.WarningFailed);
                    Bot.Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

                    return null;
                }
            }

            HtmlDocumentResponse response = await WebLimitRequest(host,
                    async () => await WebBrowser.UrlGetToHtmlDocument(host + request).ConfigureAwait(false))
                .ConfigureAwait(false);

            if (response == null) return null;

            if (IsSessionExpiredUri(response.FinalUri))
            {
                if (await RefreshSession().ConfigureAwait(false))
                    return await UrlGetToHtmlDocumentWithSession(host, request, checkSessionPreemptively, --maxTries)
                        .ConfigureAwait(false);

                Bot.Logger.LogGenericWarning(Strings.WarningFailed);
                Bot.Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

                return null;
            }

            // Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
            if (!await IsProfileUri(response.FinalUri).ConfigureAwait(false)) return response.Content;
            Bot.Logger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

            return await UrlGetToHtmlDocumentWithSession(host, request, checkSessionPreemptively, --maxTries)
                .ConfigureAwait(false);
        }

        private async Task<bool> RefreshSession()
        {
            if (!Bot.IsConnectedAndLoggedOn) return false;

            DateTime triggeredAt = DateTime.UtcNow;

            if (triggeredAt < LastSessionRefresh.AddSeconds(MinSessionValidityInSeconds)) return true;

            await SessionSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                if (triggeredAt < LastSessionRefresh.AddSeconds(MinSessionValidityInSeconds)) return true;

                if (!Bot.IsConnectedAndLoggedOn) return false;

                Bot.Logger.LogGenericInfo(Strings.RefreshingOurSession);
                var result = await Bot.RefreshSession().ConfigureAwait(false);

                if (result) LastSessionCheck = LastSessionRefresh = DateTime.UtcNow;

                return result;
            }
            finally
            {
                SessionSemaphore.Release();
            }
        }

        private async Task<bool?> IsSessionExpired()
        {
            if (DateTime.UtcNow < LastSessionCheck.AddSeconds(MinSessionValidityInSeconds))
                return LastSessionCheck != LastSessionRefresh;

            await SessionSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                if (DateTime.UtcNow < LastSessionCheck.AddSeconds(MinSessionValidityInSeconds))
                    return LastSessionCheck != LastSessionRefresh;

                // Choosing proper URL to check against is actually much harder than it initially looks like, we must abide by several rules to make this function as lightweight and reliable as possible
                // We should prefer to use Steam store, as the community is much more unstable and broken, plus majority of our requests get there anyway, so load-balancing with store makes much more sense. It also has a higher priority than the community, so all eventual issues should be fixed there first
                // The URL must be fast enough to render, as this function will be called reasonably often, and every extra delay adds up. We're already making our best effort by using HEAD request, but the URL itself plays a very important role as well
                // The page should have as little internal dependencies as possible, since every extra chunk increases likelihood of broken functionality. We can only make a guess here based on the amount of content that the page returns to us
                // It should also be URL with fairly fixed address that isn't going to disappear anytime soon, preferably something staple that is a dependency of other requests, so it's very unlikely to change in a way that would add overhead in the future
                // Lastly, it should be a request that is preferably generic enough as a routine check, not something specialized and targetted, to make it very clear that we're just checking if session is up, and to further aid internal dependencies specified above by rendering as general Steam info as possible

                const string host = SteamStoreURL;
                const string request = "/account";

               BasicResponse response =
                    await WebLimitRequest(host,
                            async () => await WebBrowser.UrlHead(host + request).ConfigureAwait(false))
                        .ConfigureAwait(false);

                if (response?.FinalUri == null) return null;

                var result = IsSessionExpiredUri(response.FinalUri);

                DateTime now = DateTime.UtcNow;

                if (!result) LastSessionRefresh = now;

                LastSessionCheck = now;

                return result;
            }
            finally
            {
                SessionSemaphore.Release();
            }
        }


        private async Task<(ESteamApiKeyState State, string Key)> GetApiKeyState()
        {
            const string request = "/dev/apikey?l=english";
            HtmlDocument htmlDocument =
                await UrlGetToHtmlDocumentWithSession(SteamCommunityURL, request).ConfigureAwait(false);

            HtmlNode titleNode = htmlDocument?.DocumentNode.SelectSingleNode("//div[@id='mainContents']/h2");

            if (titleNode == null) return (ESteamApiKeyState.Timeout, null);

            var title = titleNode.InnerText;

            if (string.IsNullOrEmpty(title))
            {
                Bot.Logger.LogNullError(nameof(title));

                return (ESteamApiKeyState.Error, null);
            }

            if (title.Contains("Access Denied") || title.Contains("Validated email address required"))
                return (ESteamApiKeyState.AccessDenied, null);

            HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@id='bodyContents_ex']/p");

            if (htmlNode == null)
            {
                Bot.Logger.LogNullError(nameof(htmlNode));

                return (ESteamApiKeyState.Error, null);
            }

            var text = htmlNode.InnerText;

            if (string.IsNullOrEmpty(text))
            {
                Bot.Logger.LogNullError(nameof(text));

                return (ESteamApiKeyState.Error, null);
            }

            if (text.Contains("Registering for a Steam Web API Key")) return (ESteamApiKeyState.NotRegisteredYet, null);

            var keyIndex = text.IndexOf("Key: ", StringComparison.Ordinal);

            if (keyIndex < 0)
            {
                Bot.Logger.LogNullError(nameof(keyIndex));

                return (ESteamApiKeyState.Error, null);
            }

            keyIndex += 5;

            if (text.Length <= keyIndex)
            {
                Bot.Logger.LogNullError(nameof(text));

                return (ESteamApiKeyState.Error, null);
            }

            text = text.Substring(keyIndex);

            if (text.Length == 32 && Utilities.IsValidHexadecimalText(text))
                return (ESteamApiKeyState.Registered, text);
            Bot.Logger.LogNullError(nameof(text));

            return (ESteamApiKeyState.Error, null);
        }

        private enum ESteamApiKeyState : byte
        {
            Error,
            Timeout,
            Registered,
            NotRegisteredYet,
            AccessDenied
        }
        internal async Task<string?> UploadAvatar(string imagePath, SteamID steamID) {
            if (!File.Exists(imagePath)) {
                Bot.Logger.LogNullError(nameof(imagePath));
                return null;
            }
            
            const string setAvatarRequest = "/actions/FileUploader";

            // Extra entry for sessionID
            Dictionary<string, string> data = new Dictionary<string, string>(6,StringComparer.Ordinal) {
                {"MAX_FILE_SIZE", "1048576"},
                {"type", "player_avatar_image"},
                {"sId", steamID.ConvertToUInt64().ToString()},
                {"doSub", "1"},
                {"json","1"},
                
            };
            
            var httpClient = new HttpClient();
            var content = new MultipartFormDataContent();
            var setAvatarResponse = await UrlPostToMultipartFormJsonObjectWithSession<BooleanResponse>(
                SteamCommunityURL , setAvatarRequest, new Dictionary<(string Name, string FileName), byte[]>(1)
                {
                    {("avatar","file.jpg"),File.ReadAllBytes(imagePath)}
                },data
            );
            
            return null;
        }
        	public async Task<T> UrlPostToMultipartFormJsonObjectWithSession<T>(string host, string request,[CanBeNull] IReadOnlyDictionary<(string Name, string FileName), byte[]> multipartFormData, Dictionary<string, string> data = null, string referer = null, ESession session = ESession.Lowercase, bool checkSessionPreemptively = true, byte maxTries = WebBrowser.MaxTries) where T : class {
			if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(request) || !Enum.IsDefined(typeof(ESession), session)) {
				Bot.Logger.LogNullError(nameof(host) + " || " + nameof(request) + " || " + nameof(session));

				return null;
			}

			if (maxTries == 0) {
				Bot.Logger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries));
				Bot.Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			if (checkSessionPreemptively) {
				// Check session preemptively as this request might not get redirected to expiration
				bool? sessionExpired = await IsSessionExpired().ConfigureAwait(false);

				if (sessionExpired.GetValueOrDefault(true)) {
					if (await RefreshSession().ConfigureAwait(false)) {
						return await UrlPostToMultipartFormJsonObjectWithSession<T>(host, request,multipartFormData, data, referer, session, true, --maxTries).ConfigureAwait(false);
					}

					Bot.Logger.LogGenericWarning(Strings.WarningFailed);
					Bot.Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return null;
				}
			} else {
				// If session refresh is already in progress, just wait for it
				await SessionSemaphore.WaitAsync().ConfigureAwait(false);
				SessionSemaphore.Release();
			}

			if (!Initialized) {
				for (byte i = 0; (i < sc.GlobalConfig.ConnectionTimeout) && !Initialized && Bot.IsConnectedAndLoggedOn; i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (!Initialized) {
					Bot.Logger.LogGenericWarning(Strings.WarningFailed);
					Bot.Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

					return null;
				}
			}

			if (session != ESession.None) {
				string sessionID = WebBrowser.CookieContainer.GetCookieValue(host, "sessionid");

				if (string.IsNullOrEmpty(sessionID)) {
					Bot.Logger.LogNullError(nameof(sessionID));

					return null;
				}

				string sessionName;

				switch (session) {
					case ESession.CamelCase:
						sessionName = "sessionID";

						break;
					case ESession.Lowercase:
						sessionName = "sessionid";

						break;
					case ESession.PascalCase:
						sessionName = "SessionID";

						break;
					default:
						Bot.Logger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(session), session));

						return null;
				}

				if (data != null) {
					data[sessionName] = sessionID;
				} else {
					data = new Dictionary<string, string>(1, StringComparer.Ordinal) { { sessionName, sessionID } };
				}
			}

			ObjectResponse<T> response = await WebLimitRequest(host, async () => await WebBrowser.UrlPostToMultipartFormJsonObject<T>(host + request,multipartFormData, data, referer).ConfigureAwait(false)).ConfigureAwait(false);

			if (response == null) {
				return null;
			}

			if (IsSessionExpiredUri(response.FinalUri)) {
				if (await RefreshSession().ConfigureAwait(false)) {
					return await UrlPostToMultipartFormJsonObjectWithSession<T>(host, request,multipartFormData ,data, referer, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
				}

				Bot.Logger.LogGenericWarning(Strings.WarningFailed);
				Bot.Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, host + request));

				return null;
			}

			// Under special brain-damaged circumstances, Steam might just return our own profile as a response to the request, for absolutely no reason whatsoever - just try again in this case
			if (await IsProfileUri(response.FinalUri).ConfigureAwait(false)) {
				Bot.Logger.LogGenericDebug(string.Format(Strings.WarningWorkaroundTriggered, nameof(IsProfileUri)));

				return await UrlPostToMultipartFormJsonObjectWithSession<T>(host, request,multipartFormData, data, referer, session, checkSessionPreemptively, --maxTries).ConfigureAwait(false);
			}

			return response.Content;
		}

    }
    public class BooleanResponse {
        [JsonProperty(PropertyName = "success", Required = Required.Always)]
        public readonly bool Success;

        [JsonConstructor]
        protected BooleanResponse() { }
    }   
    public class BasicResponse
    {
        internal readonly Uri FinalUri;

        [PublicAPI] public readonly HttpStatusCode StatusCode;

        internal BasicResponse([NotNull] HttpResponseMessage httpResponseMessage)
        {
            if (httpResponseMessage == null) throw new ArgumentNullException(nameof(httpResponseMessage));

            FinalUri = httpResponseMessage.Headers.Location ?? httpResponseMessage.RequestMessage.RequestUri;
            StatusCode = httpResponseMessage.StatusCode;
        }

        internal BasicResponse([NotNull] BasicResponse basicResponse)
        {
            if (basicResponse == null) throw new ArgumentNullException(nameof(basicResponse));

            FinalUri = basicResponse.FinalUri;
            StatusCode = basicResponse.StatusCode;
        }
    }

    public sealed class ObjectResponse<T> : BasicResponse {
        [PublicAPI]
        public readonly T Content;

        internal ObjectResponse([NotNull] StringResponse stringResponse, T content) : base(stringResponse) {
            if (stringResponse == null) {
                throw new ArgumentNullException(nameof(stringResponse));
            }

            Content = content;
        }

        internal ObjectResponse([NotNull] BasicResponse basicResponse) : base(basicResponse) { }
    }

    internal sealed class StringResponse : BasicResponse {
        internal readonly string Content;

        internal StringResponse([NotNull] HttpResponseMessage httpResponseMessage, [NotNull] string content) : base(httpResponseMessage) {
            if ((httpResponseMessage == null) || (content == null)) {
                throw new ArgumentNullException(nameof(httpResponseMessage) + " || " + nameof(content));
            }

            Content = content;
        }

        internal StringResponse([NotNull] HttpResponseMessage httpResponseMessage) : base(httpResponseMessage) { }
    }

    public sealed class HtmlDocumentResponse : BasicResponse
    {
        internal static HtmlDocument StringToHtmlDocument(string html)
        {
            if (html == null)
            {
                sc.Logger.LogNullError(nameof(html));

                return null;
            }

            var htmlDocument = new HtmlDocument();
            htmlDocument.LoadHtml(html);

            return htmlDocument;
        }

        [PublicAPI] public readonly HtmlDocument Content;

        internal HtmlDocumentResponse([NotNull] StringResponse stringResponse) : base(stringResponse)
        {
            if (stringResponse == null) throw new ArgumentNullException(nameof(stringResponse));

            if (!string.IsNullOrEmpty(stringResponse.Content))
                Content = StringToHtmlDocument(stringResponse.Content);
        }
    }
    
    public enum ERequestOptions : byte
    {
        None = 0,
        ReturnClientErrors = 1
    }
}