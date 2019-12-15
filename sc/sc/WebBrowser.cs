using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace sc
{
    public sealed class WebBrowser : IDisposable
    {
        public enum ERequestOptions : byte
        {
            None = 0,
            ReturnClientErrors = 1
        }

        private const byte
            ExtendedTimeoutMultiplier =
                10; // Defines multiplier of timeout for WebBrowsers dealing with huge data (ASF update)

        public const byte MaxTries = 5;
        internal const byte MaxConnections = 5;

        private const byte
            MaxIdleTime =
                15; // Defines in seconds, how long socket is allowed to stay in CLOSE_WAIT state after there are no connections to it


        public readonly CookieContainer CookieContainer = new CookieContainer();
        private readonly HttpClient HttpClient;
        private readonly HttpClientHandler HttpClientHandler;
        private readonly Logger Logger;
        public List<MyCookie> Cookies = new List<MyCookie>();

        internal WebBrowser([NotNull] Logger logger, bool extendedTimeout = false)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));

            HttpClientHandler = new HttpClientHandler
            {
                AllowAutoRedirect =
                    false, // This must be false if we want to handle custom redirection schemes such as "steammobile://"
                AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
                CookieContainer = CookieContainer,
                MaxConnectionsPerServer = MaxConnections
            };


            //if (!RuntimeCompatibility.IsRunningOnMono) {
            //}

            HttpClient = GenerateDisposableHttpClient(extendedTimeout);
        }

        public TimeSpan Timeout => HttpClient.Timeout;

        public void Dispose()
        {
            HttpClient.Dispose();
            HttpClientHandler.Dispose();
        }

        internal static void Init()
        {
            // Set max connection limit from default of 2 to desired value
            ServicePointManager.DefaultConnectionLimit = MaxConnections;

            // Set max idle time from default of 100 seconds (100 * 1000) to desired value
            ServicePointManager.MaxServicePointIdleTime = MaxIdleTime * 1000;

            // Don't use Expect100Continue, we're sure about our POSTs, save some TCP packets
            ServicePointManager.Expect100Continue = false;
        }

        public HttpClient GenerateDisposableHttpClient(bool extendedTimeout = false)
        {
            var result = new HttpClient(HttpClientHandler, false)
            {
                Timeout = TimeSpan.FromSeconds(extendedTimeout
                    ? ExtendedTimeoutMultiplier * sc.GlobalConfig.ConnectionTimeout
                    : sc.GlobalConfig.ConnectionTimeout)
            };

            // Most web services expect that UserAgent is set, so we declare it globally
            // If you by any chance came here with a very "clever" idea of hiding your ass by changing default ASF user-agent then here is a very good advice from me: don't, for your own safety - you've been warned
            result.DefaultRequestHeaders.UserAgent.ParseAdd(SharedInfo.PublicIdentifier + "/" + SharedInfo.Version +
                                                            " (+" + SharedInfo.ProjectURL + ")");

            return result;
        }

        [ItemCanBeNull]
        [PublicAPI]
        public async Task<HtmlDocumentResponse> UrlGetToHtmlDocument(string request, string referer = null,
            ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries)
        {
            if (string.IsNullOrEmpty(request) || maxTries == 0)
            {
                Logger.LogNullError(nameof(request) + " || " + nameof(maxTries));

                return null;
            }

            StringResponse response =
                await UrlGetToString(request, referer, requestOptions, maxTries).ConfigureAwait(false);

            return response != null ? new HtmlDocumentResponse(response) : null;
        }

        internal async Task<StringResponse> UrlGetToString(string request, string referer = null,
            ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries)
        {
            if (string.IsNullOrEmpty(request) || maxTries == 0)
            {
                Logger.LogNullError(nameof(request) + " || " + nameof(maxTries));

                return null;
            }

            StringResponse result = null;

            for (byte i = 0; i < maxTries; i++)
            {
                using HttpResponseMessage response = await InternalGet(request, referer).ConfigureAwait(false);

                if (response == null) continue;

                if (response.StatusCode.IsClientErrorCode())
                {
                    if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors))
                        result = new StringResponse(response);

                    break;
                }

                return new StringResponse(response, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }

            if (maxTries > 1)
            {
                Logger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
                Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
            }

            return result;
        }

        private async Task<HttpResponseMessage> InternalGet(string request, string referer = null,
            HttpCompletionOption httpCompletionOptions = HttpCompletionOption.ResponseContentRead)
        {
            if (string.IsNullOrEmpty(request))
            {
                Logger.LogNullError(nameof(request));

                return null;
            }

            return await InternalRequest(new Uri(request), HttpMethod.Get, null, referer, httpCompletionOptions)
                .ConfigureAwait(false);
        }
        private async Task<StringResponse> UrlPostToString(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
        			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
        				Logger.LogNullError(nameof(request) + " || " + nameof(maxTries));
        
        				return null;
        			}
        
        			StringResponse result = null;
        
        			for (byte i = 0; i < maxTries; i++) {
        				using HttpResponseMessage response = await InternalPost(request, data, referer).ConfigureAwait(false);
        
        				if (response == null) {
        					continue;
        				}
        
        				if (response.StatusCode.IsClientErrorCode()) {
        					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
        						result = new StringResponse(response);
        					}
        
        					break;
        				}
        
        				return new StringResponse(response, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        			}
        
        			if (maxTries > 1) {
        				Logger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
        				Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
        			}
        
        			return result;
        		}

        public async Task<ObjectResponse<T>> UrlPostToJsonObject<T>(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) where T : class {
            if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
                Logger.LogNullError(nameof(request) + " || " + nameof(maxTries));

                return null;
            }

            ObjectResponse<T> result = null;

            for (byte i = 0; i < maxTries; i++) {
                StringResponse response = await UrlPostToString(request, data, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

                if (response == null) {
                    return null;
                }

                if (response.StatusCode.IsClientErrorCode()) {
                    if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
                        result = new ObjectResponse<T>(response);
                    }

                    break;
                }

                if (string.IsNullOrEmpty(response.Content)) {
                    continue;
                }

                T obj;

                try {
                    obj = JsonConvert.DeserializeObject<T>(response.Content);
                } catch (JsonException e) {
                    Logger.LogGenericWarningException(e);

                    if (Debugging.IsUserDebugging) {
                        Logger.LogGenericDebug(string.Format(Strings.Content, response.Content));
                    }

                    continue;
                }

                return new ObjectResponse<T>(response, obj);
            }

            if (maxTries > 1) {
                Logger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
                Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
            }

            return result;
        }

        private async Task<HttpResponseMessage> InternalPost(string request,
            IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null)
        {
            if (string.IsNullOrEmpty(request))
            {
                Logger.LogNullError(nameof(request));

                return null;
            }

            return await InternalRequest(new Uri(request), HttpMethod.Post, data, referer).ConfigureAwait(false);
        }
        private async Task<HttpResponseMessage> InternalRequest(Uri requestUri, HttpMethod httpMethod, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead, byte maxRedirections = MaxTries) {
			if ((requestUri == null) || (httpMethod == null)) {
				Logger.LogNullError(nameof(requestUri) + " || " + nameof(httpMethod));

				return null;
			}

			HttpResponseMessage response;

			using (HttpRequestMessage request = new HttpRequestMessage(httpMethod, requestUri)) {
				if (data != null) {
					try {
						request.Content = new FormUrlEncodedContent(data);
					} catch (UriFormatException e) {
						Logger.LogGenericException(e);

						return null;
					}
				}

				if (!string.IsNullOrEmpty(referer)) {
					request.Headers.Referrer = new Uri(referer);
				}

				if (Debugging.IsUserDebugging) {
					Logger.LogGenericDebug(httpMethod + " " + requestUri);
				}

				try {
					response = await HttpClient.SendAsync(request, httpCompletionOption).ConfigureAwait(false);
				} catch (Exception e) {
					Logger.LogGenericDebuggingException(e);

					return null;
				}
			}

			if (response == null) {
				if (Debugging.IsUserDebugging) {
					Logger.LogGenericDebug("null <- " + httpMethod + " " + requestUri);
				}

				return null;
			}

			if (Debugging.IsUserDebugging) {
				Logger.LogGenericDebug(response.StatusCode + " <- " + httpMethod + " " + requestUri);
			}

			if (response.IsSuccessStatusCode) {
				return response;
			}

			// WARNING: We still have not disposed response by now, make sure to dispose it ASAP if we're not returning it!
			if ((response.StatusCode >= HttpStatusCode.Ambiguous) && (response.StatusCode < HttpStatusCode.BadRequest) && (maxRedirections > 0)) {
				Uri redirectUri = response.Headers.Location;

				if (redirectUri.IsAbsoluteUri) {
					switch (redirectUri.Scheme) {
						case "http":
						case "https":
							break;
						case "steammobile":
							// Those redirections are invalid, but we're aware of that and we have extra logic for them
							return response;
						default:
							// We have no clue about those, but maybe HttpClient can handle them for us
							sc.Logger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(redirectUri.Scheme), redirectUri.Scheme));

							break;
					}
				} else {
					redirectUri = new Uri(requestUri, redirectUri);
				}

				response.Dispose();

				// Per https://tools.ietf.org/html/rfc7231#section-7.1.2, a redirect location without a fragment should inherit the fragment from the original URI
				if (!string.IsNullOrEmpty(requestUri.Fragment) && string.IsNullOrEmpty(redirectUri.Fragment)) {
					redirectUri = new UriBuilder(redirectUri) { Fragment = requestUri.Fragment }.Uri;
				}

				return await InternalRequest(redirectUri, httpMethod, data, referer, httpCompletionOption, --maxRedirections).ConfigureAwait(false);
			}

			if (response.StatusCode.IsClientErrorCode()) {
				if (Debugging.IsUserDebugging) {
					Logger.LogGenericDebug(string.Format(Strings.Content, await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
				}

				// Do not retry on client errors
				return response;
			}

			using (response) {
				if (Debugging.IsUserDebugging) {
					Logger.LogGenericDebug(string.Format(Strings.Content, await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
				}

				return null;
			}
		}

        private async Task<HttpResponseMessage> InternalMultipartFormPost(string request,[CanBeNull] IReadOnlyDictionary<(string Name, string FileName), byte[]> multipartFormData, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null) {
            if (string.IsNullOrEmpty(request)) {
                Logger.LogNullError(nameof(request));

                return null;
            }

            return await InternalMultipartFormRequest(new Uri(request), HttpMethod.Post,multipartFormData, data, referer).ConfigureAwait(false);
        }
        
        private async Task<StringResponse> UrlPostMultipartFormToString(string request,[CanBeNull] IReadOnlyDictionary<(string Name, string FileName), byte[]> multipartFormData, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
            if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
                Logger.LogNullError(nameof(request) + " || " + nameof(maxTries));

                return null;
            }

            StringResponse result = null;

            for (byte i = 0; i < maxTries; i++) {
                using HttpResponseMessage response = await InternalMultipartFormPost(request,multipartFormData, data, referer).ConfigureAwait(false);

                if (response == null) {
                    continue;
                }

                if (response.StatusCode.IsClientErrorCode()) {
                    if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
                        result = new StringResponse(response);
                    }

                    break;
                }

                return new StringResponse(response, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            }

            if (maxTries > 1) {
                Logger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
                Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
            }

            return result;
        }

        
        public async Task<ObjectResponse<T>> UrlPostToMultipartFormJsonObject<T>(string request,[CanBeNull] IReadOnlyDictionary<(string Name, string FileName), byte[]> multipartFormData, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) where T : class {
            if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
                Logger.LogNullError(nameof(request) + " || " + nameof(maxTries));

                return null;
            }

            ObjectResponse<T> result = null;

            for (byte i = 0; i < maxTries; i++) {
                StringResponse response = await UrlPostMultipartFormToString(request,multipartFormData, data, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

                if (response == null) {
                    return null;
                }

                if (response.StatusCode.IsClientErrorCode()) {
                    if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
                        result = new ObjectResponse<T>(response);
                    }

                    break;
                }

                if (string.IsNullOrEmpty(response.Content)) {
                    continue;
                }

                T obj;

                try {
                    obj = JsonConvert.DeserializeObject<T>(response.Content);
                } catch (JsonException e) {
                    Logger.LogGenericWarningException(e);

                    if (Debugging.IsUserDebugging) {
                        Logger.LogGenericDebug(string.Format(Strings.Content, response.Content));
                    }

                    continue;
                }

                return new ObjectResponse<T>(response, obj);
            }

            if (maxTries > 1) {
                Logger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
                Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
            }

            return result;
        }

        
        private async Task<HttpResponseMessage> InternalMultipartFormRequest(Uri requestUri, HttpMethod httpMethod,
            [CanBeNull] IReadOnlyDictionary<(string Name, string FileName), byte[]> multipartFormData,
            IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null,
            HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead,
            byte maxRedirections = MaxTries)
        {
            if (requestUri == null || httpMethod == null)
            {
                Logger.LogNullError(nameof(requestUri) + " || " + nameof(httpMethod));

                return null;
            }

            HttpResponseMessage response;

            using (var request = new HttpRequestMessage(httpMethod, requestUri))
            {
                if (data != null)
                    try
                    {
                        MultipartFormDataContent content = new MultipartFormDataContent();
                        foreach ((string name,string textValue) in data)
                        {
                            content.Add(new StringContent(textValue),name);
                        }

                        foreach (((string name,string fileName),byte[] binartyValue) in multipartFormData)
                        {
                            content.Add(new ByteArrayContent(binartyValue),name,fileName);
                        }
                        request.Content = content;
                        
                    }
                    catch (UriFormatException e)
                    {
                        Logger.LogGenericException(e);

                        return null;
                    }

                if (!string.IsNullOrEmpty(referer)) request.Headers.Referrer = new Uri(referer);

                if (Debugging.IsUserDebugging) Logger.LogGenericDebug(httpMethod + " " + requestUri);

                try
                {
                    response = await HttpClient.SendAsync(request, httpCompletionOption).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.LogGenericDebuggingException(e);

                    return null;
                }
            }

            if (response == null)
            {
                if (Debugging.IsUserDebugging) Logger.LogGenericDebug("null <- " + httpMethod + " " + requestUri);

                return null;
            }

            if (Debugging.IsUserDebugging)
                Logger.LogGenericDebug(response.StatusCode + " <- " + httpMethod + " " + requestUri);

            if (response.IsSuccessStatusCode) return response;

            // WARNING: We still have not disposed response by now, make sure to dispose it ASAP if we're not returning it!
            if (response.StatusCode >= HttpStatusCode.Ambiguous && response.StatusCode < HttpStatusCode.BadRequest &&
                maxRedirections > 0)
            {
                Uri redirectUri = response.Headers.Location;

                if (redirectUri.IsAbsoluteUri)
                    switch (redirectUri.Scheme)
                    {
                        case "http":
                        case "https":
                            break;
                        case "steammobile":
                            // Those redirections are invalid, but we're aware of that and we have extra logic for them
                            return response;
                        default:
                            // We have no clue about those, but maybe HttpClient can handle them for us
                            Logger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport,
                                nameof(redirectUri.Scheme), redirectUri.Scheme));

                            break;
                    }
                else
                    redirectUri = new Uri(requestUri, redirectUri);

                response.Dispose();

                // Per https://tools.ietf.org/html/rfc7231#section-7.1.2, a redirect location without a fragment should inherit the fragment from the original URI
                if (!string.IsNullOrEmpty(requestUri.Fragment) && string.IsNullOrEmpty(redirectUri.Fragment))
                    redirectUri = new UriBuilder(redirectUri)
                    {
                        Fragment = requestUri.Fragment
                    }.Uri;

                return await InternalRequest(redirectUri, httpMethod, data, referer, httpCompletionOption,
                    --maxRedirections).ConfigureAwait(false);
            }

            if (response.StatusCode.IsClientErrorCode())
            {
                if (Debugging.IsUserDebugging)
                    Logger.LogGenericDebug(string.Format(Strings.Content,
                        await response.Content.ReadAsStringAsync().ConfigureAwait(false)));

                // Do not retry on client errors
                return response;
            }

            using (response)
            {
                if (Debugging.IsUserDebugging)
                    Logger.LogGenericDebug(string.Format(Strings.Content,
                        await response.Content.ReadAsStringAsync().ConfigureAwait(false)));

                return null;
            }
        }

        public async Task<BasicResponse> UrlPost(string request,
            IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null,
            ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries)
        {
            if (string.IsNullOrEmpty(request) || maxTries == 0)
            {
                Logger.LogNullError(nameof(request) + " || " + nameof(maxTries));

                return null;
            }

            BasicResponse result = null;

            for (byte i = 0; i < maxTries; i++)
            {
                HttpResponseMessage response = await InternalPost(request, data, referer).ConfigureAwait(false);

                if (response == null) continue;

                if (response.StatusCode.IsClientErrorCode())
                {
                    if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors))
                        result = new BasicResponse(response);

                    break;
                }

                return new BasicResponse(response);
            }

            if (maxTries > 1)
            {
                Logger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
                Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
            }

            return result;
        }

        public async Task<BasicResponse> UrlHead(string request, string referer = null,
            ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries)
        {
            if (string.IsNullOrEmpty(request) || maxTries == 0)
            {
                Logger.LogNullError(nameof(request) + " || " + nameof(maxTries));

                return null;
            }

            BasicResponse result = null;

            for (byte i = 0; i < maxTries; i++)
            {
                using HttpResponseMessage response = await InternalHead(request, referer).ConfigureAwait(false);

                if (response == null) continue;

                if (response.StatusCode.IsClientErrorCode())
                {
                    if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors))
                        result = new BasicResponse(response);

                    break;
                }

                return new BasicResponse(response);
            }

            if (maxTries > 1)
            {
                Logger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
                Logger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
            }

            return result;
        }

        private async Task<HttpResponseMessage> InternalHead(string request, string referer = null)
        {
            if (string.IsNullOrEmpty(request))
            {
                Logger.LogNullError(nameof(request));

                return null;
            }

            return await InternalRequest(new Uri(request), HttpMethod.Head, null as IReadOnlyCollection<KeyValuePair<string, string>>, referer).ConfigureAwait(false);
        }

    }

    public class MyCookie
    {
        public string Name;
        public string Value;

        public MyCookie(string value, string name)
        {
            Value = value;
            Name = name;
        }
    }

}