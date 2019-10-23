using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Discovery;

namespace sc {
	public class sc {
		public static readonly Logger Logger = new Logger(nameof(sc));

		public static GlobalConfig GlobalConfig { get; private set; }
		public static GlobalDatabase GlobalDatabase { get; private set; }
		public static WebBrowser WebBrowser { get; internal set; }

		internal static async Task Init() {
			WebBrowser = new WebBrowser(Logger,  true);
			//await RegisterBots().ConfigureAwait(false);

		}

		/*internal async void RegisterServers()
		{
			IEnumerable<ServerRecord> servers =
				await GlobalDatabase.ServerListProvider.FetchServerListAsync().ConfigureAwait(false);

			if (servers?.Any() != true)
			{
				Logger.LogGenericInfo(string.Format(Strings.Initializing, nameof(SteamDirectory)));

				SteamConfiguration steamConfiguration = SteamConfiguration.Create(builder =>
					builder.WithProtocolTypes(GlobalConfig.SteamProtocols).WithCellID(GlobalDatabase.CellID)
						.WithServerListProvider(GlobalDatabase.ServerListProvider)
						.WithHttpClientFactory(() => WebBrowser.GenerateDisposableHttpClient()));

				try
				{
					await SteamDirectory.LoadAsync(steamConfiguration).ConfigureAwait(false);
					Logger.LogGenericInfo(Strings.Success);
				}
				catch
				{
					Logger.LogGenericWarning(Strings.BotSteamDirectoryInitializationFailed);
					await Task.Delay(5000).ConfigureAwait(false);
				}

			}
		}
		 */
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
