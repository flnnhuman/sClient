using System.IO;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

[assembly: XamlCompilation(XamlCompilationOptions.Compile)]

namespace sc {
	public partial class App : Application {
		public App() {
			InitializeComponent();
			MainPage = new MainPage();
			MainPage = new NavigationPage(new second_page());
			if (!Directory.Exists(Path.Combine(Bot.MainDir,"config")))
			{
				Directory.CreateDirectory(Path.Combine(Bot.MainDir, "config"));
			}
		}

		protected override void OnResume() {
			// Handle when your app resumes
		}

		protected override void OnSleep() {
			// Handle when your app sleeps
		}

		protected override void OnStart() {
			// Handle when your app starts
		}
	}
}
