using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using JetBrains.Annotations;
using NLog;

namespace sc {
	public sealed class Logger {
		public Logger([NotNull] string name) {
			if (string.IsNullOrEmpty(name)) {
				throw new ArgumentNullException(nameof(name));
			}
			Debug.WriteLine(LogManager.GetLogger(name));
		}

		public void LogGenericDebug(string message, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));

				return;
			}

			Debug.Print($"{previousMethodName}() {message}" + "\r\n");
		}

		public void LogGenericDebuggingException(Exception exception, [CallerMemberName] string previousMethodName = null) {
			if (exception == null) {
				LogNullError(nameof(exception));

				return;
			}

			if (!Debugging.IsUserDebugging) {
				return;
			}

			Debug.Print(exception.Message, $"{previousMethodName}()" + "\r\n");
		}

		public void LogGenericError(string message, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));

				return;
			}

			Debug.Fail($"{previousMethodName}() {message}" + "\r\n");
		}

		public void LogGenericException(Exception exception, [CallerMemberName] string previousMethodName = null) {
			if (exception == null) {
				LogNullError(nameof(exception));

				return;
			}

			Debug.Fail(exception.Message, $"{previousMethodName}()" + "\r\n");
		}

		public void LogGenericInfo(string message, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));

				return;
			}

			Debug.Write($"{previousMethodName}() {message}" + "\r\n");
		}

		public void LogGenericWarning(string message, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message)) {
				LogNullError(nameof(message));

				return;
			}

			Debug.WriteLine($"{previousMethodName}() {message}" + "\r\n");
		}
		internal void LogChatMessage(bool echo, string message, ulong chatGroupID = 0, ulong chatID = 0, ulong steamID = 0, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(message) || (((chatGroupID == 0) || (chatID == 0)) && (steamID == 0))) {
				LogNullError(nameof(message) + " || " + "((" + nameof(chatGroupID) + " || " + nameof(chatID) + ") && " + nameof(steamID) + ")");

				return;
			}

			StringBuilder loggedMessage = new StringBuilder(previousMethodName + "() " + message + " " + (echo ? "->" : "<-") + " ");

			if ((chatGroupID != 0) && (chatID != 0)) {
				loggedMessage.Append(chatGroupID + "-" + chatID);

				if (steamID != 0) {
					loggedMessage.Append("/" + steamID);
				}
			} else if (steamID != 0) {
				loggedMessage.Append(steamID);
			}

			LogEventInfo logEventInfo = new LogEventInfo(LogLevel.Trace, nameof(Debug), loggedMessage.ToString());
			logEventInfo.Properties["Echo"] = echo;
			logEventInfo.Properties["Message"] = message;
			logEventInfo.Properties["ChatGroupID"] = chatGroupID;
			logEventInfo.Properties["ChatID"] = chatID;
			logEventInfo.Properties["SteamID"] = steamID;

			Debug.WriteLine(logEventInfo);
		}


		public void LogGenericWarningException(Exception exception, [CallerMemberName] string previousMethodName = null) {
			if (exception == null) {
				LogNullError(nameof(exception));

				return;
			}

			Debug.Print(exception.Message, $"{previousMethodName}()" + "\r\n");
		}

		public void LogNullError(string nullObjectName, [CallerMemberName] string previousMethodName = null) {
			if (string.IsNullOrEmpty(nullObjectName)) {
				return;
			}

			LogGenericError(string.Format(Strings.ErrorObjectIsNull, nullObjectName), previousMethodName);
		}
	}
}
