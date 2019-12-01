using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SteamKit2;

namespace sc
{
    public class BotConfig
    {
        public enum EBotBehaviour : byte
        {
            None = 0,
            RejectInvalidFriendInvites = 1,
            RejectInvalidTrades = 2,
            RejectInvalidGroupInvites = 4,
            DismissInventoryNotifications = 8,
            MarkReceivedMessagesAsRead = 16,

            All = RejectInvalidFriendInvites | RejectInvalidTrades | RejectInvalidGroupInvites |
                  DismissInventoryNotifications | MarkReceivedMessagesAsRead
        }

        private const EPersonaState DefaultOnlineStatus = EPersonaState.Online;
        private const bool DefaultUseLoginKeys = true;
        private const string DefaultSteamLogin = "";
        private const string DefaultSteamPassword = "";
        private const string DefaultSteamParentalCode = null;
        private const EBotBehaviour DefaultBotBehaviour = EBotBehaviour.None;

        internal const byte SteamParentalCodeLength = 4;
        private const bool DefaultEnabled = true;
        private static readonly SemaphoreSlim WriteSemaphore = new SemaphoreSlim(1, 1);

        [JsonProperty(Required = Required.DisallowNull)]
        public readonly EBotBehaviour BotBehaviour = DefaultBotBehaviour;

        [JsonProperty(Required = Required.DisallowNull)]
        public readonly bool Enabled = DefaultEnabled;

        [JsonProperty(Required = Required.DisallowNull)]
        public readonly EPersonaState OnlineStatus = DefaultOnlineStatus;

        public readonly bool UseLoginKeys = DefaultUseLoginKeys;
        private string BackingSteamParentalCode = DefaultSteamParentalCode;
        private bool ShouldSerializeSensitiveDetails = true;
        public string SteamLogin = DefaultSteamLogin;
        public string SteamPassword = DefaultSteamPassword;
        internal bool ShouldSerializeEverything { private get; set; } = true;
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

        internal (bool Valid, string ErrorMessage) CheckValidation()
        {
            if (BotBehaviour > EBotBehaviour.All)
                return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(BotBehaviour), BotBehaviour));

            //	if (GamesPlayedWhileIdle.Count > ArchiHandler.MaxGamesPlayedConcurrently) {
            //		return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(GamesPlayedWhileIdle), GamesPlayedWhileIdle.Count + " > " + ArchiHandler.MaxGamesPlayedConcurrently));
            //	}

            if (OnlineStatus < EPersonaState.Offline || OnlineStatus >= EPersonaState.Max)
                return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(OnlineStatus), OnlineStatus));

            //	if (RedeemingPreferences > ERedeemingPreferences.All) {
            //		return (false, string.Format(Strings.ErrorConfigPropertyInvalid, nameof(RedeemingPreferences), RedeemingPreferences));
            //	}


            if (!string.IsNullOrEmpty(SteamParentalCode) && SteamParentalCode != "0" &&
                SteamParentalCode.Length != SteamParentalCodeLength)
                return (false,
                    string.Format(Strings.ErrorConfigPropertyInvalid, nameof(SteamParentalCode), SteamParentalCode));


            return (true, null);
        }

        internal static BotConfig CreateOrLoad(string filePath)
        {
            var filePathLog = filePath;
            filePath = Path.Combine(Bot.MainDir, filePath);
            if (string.IsNullOrEmpty(filePath))
            {
                sc.Logger.LogNullError(nameof(filePath));
                return null;
            }

            BotConfig botConfig;
            if (!File.Exists(filePath))
            {
                botConfig = new BotConfig();
                botConfig.Write(filePath);
                sc.Logger.LogGenericInfo(string.Format(Strings.FileCreated, filePathLog));
                return botConfig;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                if (string.IsNullOrEmpty(json))
                {
                    sc.Logger.LogGenericError(string.Format(Strings.ErrorObjectIsNull, nameof(json)));
                    return null;
                }

                botConfig = JsonConvert.DeserializeObject<BotConfig>(json);
            }
            catch (Exception e)
            {
                sc.Logger.LogGenericWarningException(e);
                return null;
            }

            var (valid, errorMessage) = botConfig.CheckValidation();

            if (!valid)
            {
                sc.Logger.LogGenericError(errorMessage);

                return null;
            }


            sc.Logger.LogGenericInfo(string.Format(Strings.FileLoaded, filePathLog));
            botConfig.ShouldSerializeEverything = false;
            botConfig.ShouldSerializeSensitiveDetails = false;
            return botConfig;
        }

        internal static async Task<bool> Write(string filePath, BotConfig botConfig)
        {
            if (string.IsNullOrEmpty(filePath) || botConfig == null)
            {
                sc.Logger.LogNullError(nameof(filePath) + " || " + nameof(botConfig));

                return false;
            }

            var json = JsonConvert.SerializeObject(botConfig, Formatting.Indented);
            var newFilePath = filePath + ".new";

            await WriteSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                File.WriteAllText(newFilePath, json);

                if (File.Exists(filePath))
                    File.Replace(newFilePath, filePath, null);
                else
                    File.Move(newFilePath, filePath);
            }
            catch (Exception e)
            {
                sc.Logger.LogGenericException(e);

                return false;
            }
            finally
            {
                WriteSemaphore.Release();
            }

            return true;
        }

        internal void Write(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                sc.Logger.LogNullError(nameof(filePath));
                return;
            }

            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            var newFilePath = filePath + ".new";

            try
            {
                File.WriteAllText(newFilePath, json);

                if (File.Exists(filePath))
                    File.Replace(newFilePath, filePath, null);
                else
                    File.Move(newFilePath, filePath);
            }
            catch (Exception e)
            {
                sc.Logger.LogGenericWarningException(e);
            }
        }
    }
}