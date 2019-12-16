using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using SteamKit2;

namespace sc
{
    public class GlobalConfig
    {
        private const bool DefaultDebug = true;

        internal const byte DefaultConnectionTimeout = 90;
        internal const byte DefaultLoginLimiterDelay = 10;

        private const ushort DefaultWebLimiterDelay = 300;

        private static string DefaultLanguage = CultureInfo.CurrentCulture.Name;

        [JsonProperty(Required = Required.DisallowNull)]
        public string Language { get; set; }  = DefaultLanguage;
        
        //private const ProtocolTypes DefaultSteamProtocols = ProtocolTypes.All;
        private const ProtocolTypes DefaultSteamProtocols = ProtocolTypes.WebSocket;

        [JsonProperty(Required = Required.DisallowNull)]
        public byte ConnectionTimeout { get; set; } = DefaultConnectionTimeout;

        public bool Debug { get; set; } = DefaultDebug;

        [JsonProperty(Required = Required.DisallowNull)]
        public byte LoginLimiterDelay { get; set; } = DefaultLoginLimiterDelay;

        public ushort WebLimiterDelay { get; set; } = DefaultWebLimiterDelay;
        public ProtocolTypes SteamProtocols { get; set; } = DefaultSteamProtocols;

        internal static GlobalConfig CreateOrLoad(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                sc.Logger.LogNullError(nameof(filePath));
                return null;
            }

            var filePathLog = "config.json";

            GlobalConfig globalConfig;
            if (!File.Exists(filePath))
            {
                globalConfig = new GlobalConfig();
                globalConfig.Write(filePath);
                sc.Logger.LogGenericInfo(string.Format(Strings.FileCreated, filePathLog));
                return globalConfig;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                if (string.IsNullOrEmpty(json))
                {
                    sc.Logger.LogGenericError(string.Format(Strings.ErrorObjectIsNull, nameof(json)));
                    return null;
                }

                globalConfig = JsonConvert.DeserializeObject<GlobalConfig>(json);
            }
            catch (Exception e)
            {
                sc.Logger.LogGenericWarningException(e);
                return null;
            }

            sc.Logger.LogGenericInfo(string.Format(Strings.FileLoaded, filePathLog));
            return globalConfig;
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