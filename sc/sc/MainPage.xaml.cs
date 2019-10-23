using System;
using System.IO;
using System.Threading;
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

		public static readonly string CacheDir = FileSystem.CacheDirectory;
		public static readonly string MainDir = FileSystem.AppDataDirectory;

		private readonly BotConfig BotConfig = new BotConfig();
		private readonly BotDatabase BotDatabase = new BotDatabase(Path.Combine(MainDir, "bot"));

		public MainPage() {
			InitializeComponent();
		}

		public string Login => LoginField.Text;
		public string Password => PasswordField.Text;
		public string TwoFactorCode => TwoFactorCodeField.Text;

		private void Button_OnClicked(object sender, EventArgs e) {
			ThreadPool.QueueUserWorkItem(o => {
				sc.InitializeGlobalConfigAndDatabase();
				sc.Init().ConfigureAwait(false);
				Bot bot = new Bot("bot", BotConfig, BotDatabase);
				bot.InitModules().ConfigureAwait(false);
				bot.InitStart();
			});
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
	}
}
