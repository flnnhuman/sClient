using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Humanizer.Localisation;

namespace sc
{
    public static class Utilities
    {
        internal static string GetCookieValue(this CookieContainer cookieContainer, string url, string name)
        {
            if (cookieContainer == null || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(name))
            {
                sc.Logger.LogNullError(nameof(cookieContainer) + " || " + nameof(url) + " || " + nameof(name));

                return null;
            }

            Uri uri;

            try
            {
                uri = new Uri(url);
            }
            catch (UriFormatException e)
            {
                sc.Logger.LogGenericException(e);

                return null;
            }

            CookieCollection cookies = cookieContainer.GetCookies(uri);

            return cookies.Count > 0
                ? (from Cookie cookie in cookies where cookie.Name.Equals(name) select cookie.Value).FirstOrDefault()
                : null;
        }

        public static bool IsValidHexadecimalText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                sc.Logger.LogNullError(nameof(text));

                return false;
            }

            return text.Length % 2 == 0 && text.All(Uri.IsHexDigit);
        }

        public static void InBackground(Action action, bool longRunning = false)
        {
            if (action == null)
            {
                sc.Logger.LogNullError(nameof(action));

                return;
            }

            var options = TaskCreationOptions.DenyChildAttach;

            if (longRunning) options |= TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness;

            Task.Factory.StartNew(action, CancellationToken.None, options, TaskScheduler.Default);
        }

        public static void InBackground<T>(Func<T> function, bool longRunning = false)
        {
            if (function == null)
            {
                sc.Logger.LogNullError(nameof(function));

                return;
            }

            var options = TaskCreationOptions.DenyChildAttach;

            if (longRunning) options |= TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness;

            Task.Factory.StartNew(function, CancellationToken.None, options, TaskScheduler.Default);
        }

        public static bool IsClientErrorCode(this HttpStatusCode statusCode)
        {
            return statusCode >= HttpStatusCode.BadRequest && statusCode < HttpStatusCode.InternalServerError;
        }

        public static string ToHumanReadable(this TimeSpan timeSpan)
        {
            return timeSpan.Humanize(3, maxUnit: TimeUnit.Year, minUnit: TimeUnit.Second);
        }
        public static string ByteArrayToHexString(byte[] Bytes)
        {
            var Result = new StringBuilder(Bytes.Length * 2);
            var HexAlphabet = "0123456789ABCDEF";

            foreach (var B in Bytes)
            {
                Result.Append(HexAlphabet[B >> 4]);
                Result.Append(HexAlphabet[B & 0xF]);
            }

            return Result.ToString();
        }
    }
}