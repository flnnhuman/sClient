using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace sc
{
    public sealed class Logger
    {
        public Logger([NotNull] string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
        }

        public void LogGenericError(string message, [CallerMemberName] string previousMethodName = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                LogNullError(nameof(message));

                return;
            }

            Debug.Fail($"{previousMethodName}() {message}" + "\r\n");
        }

        public void LogNullError(string nullObjectName, [CallerMemberName] string previousMethodName = null)
        {
            if (string.IsNullOrEmpty(nullObjectName)) return;

            LogGenericError(string.Format(Strings.ErrorObjectIsNull, nullObjectName), previousMethodName);
        }

        public void LogGenericInfo(string message, [CallerMemberName] string previousMethodName = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                LogNullError(nameof(message));

                return;
            }

            Debug.Write($"{previousMethodName}() {message}" + "\r\n");
        }

        public void LogGenericException(Exception exception, [CallerMemberName] string previousMethodName = null)
        {
            if (exception == null)
            {
                LogNullError(nameof(exception));

                return;
            }

            Debug.Fail(exception.Message, $"{previousMethodName}()" + "\r\n");
        }

        public void LogGenericWarning(string message, [CallerMemberName] string previousMethodName = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                LogNullError(nameof(message));

                return;
            }

            Debug.WriteLine($"{previousMethodName}() {message}" + "\r\n");
        }

        public void LogGenericDebuggingException(Exception exception,
            [CallerMemberName] string previousMethodName = null)
        {
            if (exception == null)
            {
                LogNullError(nameof(exception));

                return;
            }

            if (!Debugging.IsUserDebugging) return;

            Debug.Print(exception.Message, $"{previousMethodName}()" + "\r\n");
        }

        public void LogGenericDebug(string message, [CallerMemberName] string previousMethodName = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                LogNullError(nameof(message));

                return;
            }

            Debug.Print($"{previousMethodName}() {message}" + "\r\n");
        }

        public void LogGenericWarningException(Exception exception, [CallerMemberName] string previousMethodName = null)
        {
            if (exception == null)
            {
                LogNullError(nameof(exception));

                return;
            }

            Debug.Print(exception.Message, $"{previousMethodName}()" + "\r\n");
        }
    }
}