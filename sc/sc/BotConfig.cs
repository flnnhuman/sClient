using System;
using Newtonsoft.Json;
using SteamKit2;

namespace sc
{
    public class BotConfig
    {
        private const EPersonaState DefaultOnlineStatus = EPersonaState.Online;
        private const bool DefaultUseLoginKeys = true;
        public readonly bool UseLoginKeys = DefaultUseLoginKeys;
        private const string DefaultSteamLogin = "randomflbnacr";
        private const string DefaultSteamPassword = "Fdk&!!k184";

        public string SteamLogin = DefaultSteamLogin;
        public string SteamPassword = DefaultSteamPassword;

        public string DecryptedSteamPassword = "";

        [JsonProperty(Required = Required.DisallowNull)]
        public readonly EPersonaState OnlineStatus = DefaultOnlineStatus;

        private const string DefaultSteamParentalCode = null;
        internal const byte SteamParentalCodeLength = 4;
        internal bool IsSteamParentalCodeSet { get; private set; }

        internal string SteamParentalCode
        {
            get => BackingSteamParentalCode;

            set
            {
                IsSteamParentalCodeSet = true;
                BackingSteamParentalCode = value;
            }
        }

        private string BackingSteamParentalCode = DefaultSteamParentalCode;

        [JsonProperty(Required = Required.DisallowNull)]
        public readonly bool Enabled = DefaultEnabled;

        private const bool DefaultEnabled = true;
    }
}