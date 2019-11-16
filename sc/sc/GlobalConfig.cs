using System;
using System.IO;
using Newtonsoft.Json;
using SteamKit2;

namespace sc {
	public class GlobalConfig {
		private const bool DefaultDebug = false;

		internal const byte DefaultConnectionTimeout = 90;
		internal const byte DefaultLoginLimiterDelay = 10;
		private const ushort DefaultWebLimiterDelay = 300;
		public ProtocolTypes SteamProtocols { get; } = DefaultSteamProtocols;
		//private const ProtocolTypes DefaultSteamProtocols = ProtocolTypes.All;
		private const ProtocolTypes DefaultSteamProtocols = ProtocolTypes.WebSocket;
		[JsonProperty(Required = Required.DisallowNull)]
		public readonly byte ConnectionTimeout = DefaultConnectionTimeout;

		public readonly bool Debug = DefaultDebug;

		[JsonProperty(Required = Required.DisallowNull)]
		public readonly byte LoginLimiterDelay = DefaultLoginLimiterDelay;

		public readonly ushort WebLimiterDelay = DefaultWebLimiterDelay;

		internal static GlobalConfig CreateOrLoad(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				sc.Logger.LogNullError(nameof(filePath));
				return null;
			}
			string filePathLog="config.json";

			GlobalConfig globalConfig;
			if (!File.Exists(filePath)) {
				globalConfig = new GlobalConfig();
				globalConfig.Write(filePath);
				sc.Logger.LogGenericInfo(string.Format( Strings.FileCreated,filePathLog));
				return globalConfig;
			}

			try {
				string json = File.ReadAllText(filePath);
				if (string.IsNullOrEmpty(json)) {
					sc.Logger.LogGenericError(string.Format(Strings.ErrorObjectIsNull, nameof(json)));
					return null;
				}

				globalConfig = JsonConvert.DeserializeObject<GlobalConfig>(json);
			} catch (Exception e) {
				sc.Logger.LogGenericWarningException(e);
				return null;
			}
			sc.Logger.LogGenericInfo(string.Format( Strings.FileLoaded,filePathLog));
			return globalConfig;
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
	}
}
