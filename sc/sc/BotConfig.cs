using Newtonsoft.Json;
using SteamKit2;

namespace sc {
	public class BotConfig {
		private const EPersonaState DefaultOnlineStatus = EPersonaState.Online;
		private const bool DefaultUseLoginKeys = true;
		private const string DefaultSteamLogin = "";
		private const string DefaultSteamPassword = "";

		private const string DefaultSteamParentalCode = null;
		internal const byte SteamParentalCodeLength = 4;

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
	}
}
