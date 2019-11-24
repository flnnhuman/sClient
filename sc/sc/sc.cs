using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SteamKit2;
using SteamKit2.Discovery;

namespace sc {
	public class sc {
		public static Bot bot;

	
		public static mainpage1 Mainpage1= new mainpage1();
		
		public static readonly Logger Logger = new Logger(nameof(sc));

		public static GlobalConfig GlobalConfig { get; private set; }
		public static GlobalDatabase GlobalDatabase { get; private set; }
		public static WebBrowser WebBrowser { get; internal set; }
		internal static string GetFilePath(Bot.EFileType fileType) {
			if (!Enum.IsDefined(typeof(Bot.EFileType), fileType)) {
				Logger.LogNullError(nameof(fileType));

				return null;
			}

			switch (fileType) {
				case Bot.EFileType.Config:
					return Path.Combine(MainPage.MainDir,SharedInfo.ConfigDirectory, SharedInfo.GlobalConfigFileName);
				case Bot.EFileType.Database:
					return Path.Combine(MainPage.MainDir,SharedInfo.ConfigDirectory, SharedInfo.GlobalDatabaseFileName);
				default:
					Logger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(fileType), fileType));

					return null;
			}
		}

		internal static async Task Init() {
			WebBrowser = new WebBrowser(Logger,  true);
			//await RegisterBots().ConfigureAwait(false);

			string globalDatabaseFile = Path.Combine(MainPage.MainDir,sc.GetFilePath(Bot.EFileType.Database));

			if (string.IsNullOrEmpty(globalDatabaseFile)) {
				sc.Logger.LogNullError(nameof(globalDatabaseFile));

				return;
			}
			
			GlobalDatabase globalDatabase = GlobalDatabase.CreateOrLoad(globalDatabaseFile);

			if (globalDatabase == null) {
				sc.Logger.LogGenericError(string.Format(Strings.ErrorDatabaseInvalid, globalDatabaseFile));
				await Task.Delay(5 * 1000).ConfigureAwait(false);
				//await Exit(1).ConfigureAwait(false);

				return;
			}

			sc.InitGlobalDatabase(globalDatabase);
			if (Debugging.IsUserDebugging) {
				if (Debugging.IsDebugConfigured) {
					sc.Logger.LogGenericDebug(globalDatabaseFile + ": " + JsonConvert.SerializeObject(sc.GlobalDatabase, Formatting.Indented));
				}

				//todo Logging.EnableTraceLogging();

				DebugLog.AddListener(new Debugging.DebugListener());
				DebugLog.Enabled = true;

				if (Directory.Exists(Path.Combine(MainPage.MainDir,SharedInfo.DebugDirectory))){
					try {
						Directory.Delete(Path.Combine(MainPage.MainDir,SharedInfo.DebugDirectory), true);
						await Task.Delay(1000).ConfigureAwait(false); // Dirty workaround giving Windows some time to sync
					} catch (Exception e) {
						sc.Logger.LogGenericException(e);
					}
				}

				try {
					Directory.CreateDirectory(Path.Combine(MainPage.MainDir,SharedInfo.DebugDirectory));
				} catch (Exception e) {
					sc.Logger.LogGenericException(e);
				}
			}

			WebBrowser.Init();
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
