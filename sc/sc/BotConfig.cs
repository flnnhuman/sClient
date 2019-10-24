using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SteamKit2;

namespace sc {
	public class BotConfig {
		private const EPersonaState DefaultOnlineStatus = EPersonaState.Online;
		private const bool DefaultUseLoginKeys = true;
		private const string DefaultSteamLogin = "";
		private const string DefaultSteamPassword = "";

		private const EBotBehaviour DefaultBotBehaviour = EBotBehaviour.None;
		[JsonProperty(Required = Required.DisallowNull)]
		public readonly EBotBehaviour BotBehaviour = DefaultBotBehaviour;
		private const string DefaultSteamParentalCode = null;
		internal const byte SteamParentalCodeLength = 4;
		private bool ShouldSerializeSensitiveDetails = true;

		private static readonly SemaphoreSlim WriteSemaphore = new SemaphoreSlim(1, 1);

		internal bool ShouldSerializeEverything { private get; set; } = true;

		private const bool DefaultEnabled = true;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly bool Enabled = DefaultEnabled;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly EPersonaState OnlineStatus = DefaultOnlineStatus;

		public readonly bool UseLoginKeys = DefaultUseLoginKeys;

		private string BackingSteamParentalCode = DefaultSteamParentalCode;

		public string DecryptedSteamPassword = "";

		public string SteamLogin = DefaultSteamLogin;
		public string SteamPassword = DefaultSteamPassword;
		internal bool IsSteamParentalCodeSet { get; private set; }

		internal string SteamParentalCode {
			get => BackingSteamParentalCode;

			set {
				IsSteamParentalCodeSet = true;
				BackingSteamParentalCode = value;
			}
		}
		internal (bool Valid, string ErrorMessage) CheckValidation() {
			if (BotBehaviour > EBotBehaviour.All) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(BotBehaviour), BotBehaviour));
			}
			
		//	if (GamesPlayedWhileIdle.Count > ArchiHandler.MaxGamesPlayedConcurrently) {
		//		return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(GamesPlayedWhileIdle), GamesPlayedWhileIdle.Count + " > " + ArchiHandler.MaxGamesPlayedConcurrently));
		//	}
			
			if ((OnlineStatus < EPersonaState.Offline) || (OnlineStatus >= EPersonaState.Max)) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(OnlineStatus), OnlineStatus));
			}
			
		//	if (RedeemingPreferences > ERedeemingPreferences.All) {
		//		return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(RedeemingPreferences), RedeemingPreferences));
		//	}
		

			if (!string.IsNullOrEmpty(SteamParentalCode) && (SteamParentalCode != "0") && (SteamParentalCode.Length != SteamParentalCodeLength)) {
				return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(SteamParentalCode), SteamParentalCode));
			}


			return (true, null);
		}

		internal static async Task<BotConfig> Load(string filePath) {
			filePath = Path.Combine(Bot.MainDir, filePath);
			if (string.IsNullOrEmpty(filePath)) {
				sc.Logger.LogNullError(nameof(filePath));

				return null;
			}

			if (!File.Exists(filePath)) {
				return null;
			}

			BotConfig botConfig;

			try {
				string json = File.ReadAllText(filePath);

				if (string.IsNullOrEmpty(json)) {
					sc.Logger.LogGenericError(string.Format(Strings.ErrorIsEmpty, nameof(json)));

					return null;
				}

				botConfig = JsonConvert.DeserializeObject<BotConfig>(json);
			} catch (Exception e) {
				sc.Logger.LogGenericException(e);

				return null;
			}

			if (botConfig == null) {
				sc.Logger.LogNullError(nameof(botConfig));

				return null;
			}

			(bool valid, string errorMessage) = botConfig.CheckValidation();

			if (!valid) {
				sc.Logger.LogGenericError(errorMessage);

				return null;
			}

			botConfig.ShouldSerializeEverything = false;
			botConfig.ShouldSerializeSensitiveDetails = false;

			return botConfig;
		}

		internal static BotConfig CreateOrLoad(string filePath)
		{
			string filePathLog=filePath;
			filePath = Path.Combine(Bot.MainDir, filePath);
			if (string.IsNullOrEmpty(filePath)) {
				sc.Logger.LogNullError(nameof(filePath));
				return null;
			}

			BotConfig botConfig;
			if (!File.Exists(filePath)) {
				botConfig = new BotConfig();
				botConfig.Write(filePath);
				sc.Logger.LogGenericInfo(string.Format( Strings.FileCreated,filePathLog));
				return botConfig;
			}

			try {
				string json = File.ReadAllText(filePath);
				if (string.IsNullOrEmpty(json)) {
					sc.Logger.LogGenericError(string.Format(Strings.ErrorObjectIsNull, nameof(json)));
					return null;
				}

				botConfig = JsonConvert.DeserializeObject<BotConfig>(json);
			} catch (Exception e) {
				sc.Logger.LogGenericWarningException(e);
				return null;
			}

			(bool valid, string errorMessage) = botConfig.CheckValidation();

			if (!valid) {
				sc.Logger.LogGenericError(errorMessage);

				return null;
			}
			
			
			sc.Logger.LogGenericInfo(string.Format( Strings.FileLoaded,filePathLog));
			botConfig.ShouldSerializeEverything = false;
			botConfig.ShouldSerializeSensitiveDetails = false;
			return botConfig;
		}

		internal static async Task<bool> Write(string filePath, BotConfig botConfig) {
			if (string.IsNullOrEmpty(filePath) || (botConfig == null)) {
				sc.Logger.LogNullError(nameof(filePath) + " || " + nameof(botConfig));

				return false;
			}

			string json = JsonConvert.SerializeObject(botConfig, Formatting.Indented);
			string newFilePath = filePath + ".new";

			await WriteSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				File.WriteAllText(newFilePath, json);

				if (File.Exists(filePath)) {
					File.Replace(newFilePath, filePath, null);
				} else {
					File.Move(newFilePath, filePath);
				}
			} catch (Exception e) {
				sc.Logger.LogGenericException(e);

				return false;
			} finally {
				WriteSemaphore.Release();
			}

			return true;
		}

		internal void Write(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				sc.Logger.LogNullError(nameof(filePath));
				return;
			}

			string json = JsonConvert.SerializeObject(this, Formatting.Indented);
			string newFilePath = filePath + ".new";

			try {
				File.WriteAllText(newFilePath, json);

				if (File.Exists(filePath)) {
					File.Replace(newFilePath, filePath, null);
				} else {
					File.Move(newFilePath, filePath);
				}
			} catch (Exception e) {
				sc.Logger.LogGenericWarningException(e);
			}
		}
		public enum EBotBehaviour : byte {
			None = 0,
			RejectInvalidFriendInvites = 1,
			RejectInvalidTrades = 2,
			RejectInvalidGroupInvites = 4,
			DismissInventoryNotifications = 8,
			MarkReceivedMessagesAsRead = 16,
			All = RejectInvalidFriendInvites | RejectInvalidTrades | RejectInvalidGroupInvites | DismissInventoryNotifications | MarkReceivedMessagesAsRead
		}
	}
}
