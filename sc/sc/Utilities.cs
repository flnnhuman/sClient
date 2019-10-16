using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Humanizer.Localisation;

namespace sc
{
    public static class Utilities
    {
        private static Logger Logger;
        public static void InBackground<T>(Func<T> function, bool longRunning = false) {
            if (function == null) {
                Logger.LogNullError(nameof(function));

                return;
            }

            TaskCreationOptions options = TaskCreationOptions.DenyChildAttach;

            if (longRunning) {
                options |= TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness;
            }

            Task.Factory.StartNew(function, CancellationToken.None, options, TaskScheduler.Default);
        } 
        public static string ToHumanReadable(this TimeSpan timeSpan) => timeSpan.Humanize(3, maxUnit: TimeUnit.Year, minUnit: TimeUnit.Second);

        internal static string GetCookieValue(this CookieContainer cookieContainer, string url, string name) {
            if ((cookieContainer == null) || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(name)) {
                Logger.LogNullError(nameof(cookieContainer) + " || " + nameof(url) + " || " + nameof(name));

                return null;
            }

            Uri uri;

            try {
                uri = new Uri(url);
            } catch (UriFormatException e) {
                Logger.LogGenericException(e);

                return null;
            }

            CookieCollection cookies = cookieContainer.GetCookies(uri);

            return cookies.Count > 0 ? (from Cookie cookie in cookies where cookie.Name.Equals(name) select cookie.Value).FirstOrDefault() : null;
        }
        public static bool IsClientErrorCode(this HttpStatusCode statusCode) => (statusCode >= HttpStatusCode.BadRequest) && (statusCode < HttpStatusCode.InternalServerError);

    }
}