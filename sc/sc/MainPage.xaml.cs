using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Plugin.Toast;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace sc {
	public partial class MainPage : ContentPage {
		public enum EToastType {
			Message,
			Warning,
			Error,
			Success
		}

		private static Bot bot;
		
		public static readonly string CacheDir = FileSystem.CacheDirectory;
		public static readonly string MainDir = FileSystem.AppDataDirectory;
		
		public MainPage() {
			InitializeComponent();
		}

		public string Login => LoginField.Text;
		public string Password => PasswordField.Text;
		public string TwoFactorCode => TwoFactorCodeField.Text;

		private async void Button_OnClicked(object sender, EventArgs e) {
			ThreadPool.QueueUserWorkItem(o => {
				sc.InitializeGlobalConfigAndDatabase();
				sc.Init().ConfigureAwait(false);
				//bot = new Bot("bot", BotConfig, BotDatabase);
				RegisterBot(Login);
				bot.InitModules().ConfigureAwait(false);
				bot.InitStart();
			});
		}
		private void Button2_OnClicked(object sender, EventArgs e) {
			Toast(bot.SteamID.ToString(),EToastType.Message);
		}

		public static void Toast(string msg, EToastType type) {
			Device.BeginInvokeOnMainThread(() => {
				switch (type) {
					case EToastType.Message:
						CrossToastPopUp.Current.ShowToastMessage(msg);
						break;
					case EToastType.Error:
						CrossToastPopUp.Current.ShowToastError(msg);
						break;
					case EToastType.Warning:
						CrossToastPopUp.Current.ShowToastWarning(msg);
						break;
					case EToastType.Success:
						CrossToastPopUp.Current.ShowToastSuccess(msg);
						break;
				}
			});
		}
		internal static async Task RegisterBot(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				sc.Logger.LogNullError(nameof(botName));

				return;
			}

			//if (Bots.ContainsKey(botName)) {
			//	return;
			//}

			string configFilePath = Bot.GetFilePath(botName, Bot.EFileType.Config);

			if (string.IsNullOrEmpty(configFilePath)) {
				sc.Logger.LogNullError(nameof(configFilePath));

				return;
			}

			//BotConfig botConfig = await BotConfig.Load(configFilePath).ConfigureAwait(false);
			BotConfig botConfig = BotConfig.CreateOrLoad(configFilePath);
			if (botConfig == null) {
				sc.Logger.LogGenericError(string.Format(Strings.ErrorBotConfigInvalid, configFilePath));

				return;
			}

			if (Debugging.IsDebugConfigured) {
				sc.Logger.LogGenericDebug(configFilePath + ": " + JsonConvert.SerializeObject(botConfig, Formatting.Indented));
			}

			string databaseFilePath = Bot.GetFilePath(botName, Bot.EFileType.Database);

			if (string.IsNullOrEmpty(databaseFilePath)) {
				sc.Logger.LogNullError(nameof(databaseFilePath));

				return;
			}

			BotDatabase botDatabase = await BotDatabase.CreateOrLoad(databaseFilePath).ConfigureAwait(false);

			
			if (botDatabase == null) {
				sc.Logger.LogGenericError(string.Format(Strings.ErrorDatabaseInvalid, databaseFilePath));

				return;
			}

			if (Debugging.IsDebugConfigured) {
				sc.Logger.LogGenericDebug(databaseFilePath + ": " + JsonConvert.SerializeObject(botDatabase, Formatting.Indented));
			}

			//Bot bot;
			

			await Bot.BotsSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				//if (Bot.Bots.ContainsKey(botName)) {
				//	return;
				//}

				bot = new Bot(botName, botConfig, botDatabase);

				if (!Bot.Bots.TryAdd(botName, bot)) {
					sc.Logger.LogNullError(nameof(bot));
					bot.Dispose();

					return;
				}
			} finally {
				Bot.BotsSemaphore.Release();
			}
			
			await bot.InitModules().ConfigureAwait(false);
			bot.InitStart();
		}

	}
}
