using System.IO;

namespace sc {
	public class sc {
		public static readonly Logger Logger = new Logger(nameof(sc));

		public static GlobalConfig GlobalConfig { get; private set; }
		public static GlobalDatabase GlobalDatabase { get; private set; }
		public static WebBrowser WebBrowser { get; internal set; }

		internal static void InitGlobalConfig(GlobalConfig globalConfig) {
			if (globalConfig == null) {
				Logger.LogNullError(nameof(globalConfig));

				return;
			}

			if (GlobalConfig != null) {
				return;
			}

			GlobalConfig = globalConfig;
		}

		internal static void InitGlobalDatabase(GlobalDatabase globalDatabase) {
			if (globalDatabase == null) {
				Logger.LogNullError(nameof(globalDatabase));

				return;
			}

			if (GlobalDatabase != null) {
				return;
			}

			GlobalDatabase = globalDatabase;
		}

		internal static void InitializeGlobalConfigAndDatabase() {
			InitGlobalConfig(GlobalConfig.CreateOrLoad(Path.Combine(MainPage.MainDir, "config.json")));
			InitGlobalDatabase(GlobalDatabase.CreateOrLoad(Path.Combine(MainPage.MainDir, "db.json")));
		}

		internal enum EUserInputType : byte {
			Unknown,
			DeviceID,
			Login,
			Password,
			SteamGuard,
			SteamParentalCode,
			TwoFactorAuthentication
		}
	}
}
