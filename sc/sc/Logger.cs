using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace sc
{
    public sealed class Logger
    {
        
        public void LogGenericError(string message, [CallerMemberName] string previousMethodName = null) {
            if (string.IsNullOrEmpty(message)) {
                LogNullError(nameof(message));

                return;
            }
            
            Debug.Fail($"{previousMethodName}() {message}");
        }
        public void LogNullError(string nullObjectName, [CallerMemberName] string previousMethodName = null) {
            if (string.IsNullOrEmpty(nullObjectName)) {
                return;
            }

            LogGenericError(string.Format(Strings.ErrorObjectIsNull, nullObjectName), previousMethodName);
        }
        
        public void LogGenericInfo(string message, [CallerMemberName] string previousMethodName = null) {
            if (string.IsNullOrEmpty(message)) {
                LogNullError(nameof(message));

                return;
            }

            Debug.Write($"{previousMethodName}() {message}");
        } 
        
        public void LogGenericException(Exception exception, [CallerMemberName] string previousMethodName = null) {
            if (exception == null) {
                LogNullError(nameof(exception));

                return;
            }

            Debug.Fail(exception.Message, $"{previousMethodName}()");
        }
        public void LogGenericWarning(string message, [CallerMemberName] string previousMethodName = null) {
            if (string.IsNullOrEmpty(message)) {
                LogNullError(nameof(message));

                return;
            }

            Debug.WriteLine($"{previousMethodName}() {message}");
        }
        public void LogGenericDebuggingException(Exception exception, [CallerMemberName] string previousMethodName = null) {
            if (exception == null) {
                LogNullError(nameof(exception));

                return;
            }

            if (!Debugging.IsUserDebugging) {
                return;
            }

            Debug.Print(exception.Message, $"{previousMethodName}()");
        }
        public void LogGenericDebug(string message, [CallerMemberName] string previousMethodName = null) {
            if (string.IsNullOrEmpty(message)) {
                LogNullError(nameof(message));

                return;
            }

            Debug.Print($"{previousMethodName}() {message}");
        }
        public void LogGenericWarningException(Exception exception, [CallerMemberName] string previousMethodName = null) {
            if (exception == null) {
                LogNullError(nameof(exception));

                return;
            }

            Debug.Print(exception.Message, $"{previousMethodName}()");
        }
    }
    
}