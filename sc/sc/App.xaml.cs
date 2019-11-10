using System.IO;
using System.Threading.Tasks;
using Plugin.Toast;
using Xamarin.Forms;
using Xamarin.Forms.Themes;
using Xamarin.Forms.Xaml;

[assembly: XamlCompilation(XamlCompilationOptions.Compile)]

namespace sc {
	public partial class App : Application {
		public App() {
			InitializeComponent();
			MainPage = new MainPage();
			//MainPage = new NavigationPage(new second_page());
			if (!Directory.Exists(Path.Combine(Bot.MainDir,"config")))
			{
				Directory.CreateDirectory(Path.Combine(Bot.MainDir, "config"));
			}
		}

		protected override async void OnResume()
		{
			base.OnResume();
			MyTheme theme = await DependencyService.Get<IEnvironment>().GetOperatingSystemTheme();
			SetTheme(theme);
		}

		protected override void OnSleep() {
			// Handle when your app sleeps
		}

		protected override async void OnStart()
		{
			base.OnStart();
			MyTheme theme = await DependencyService.Get<IEnvironment>().GetOperatingSystemTheme();
			SetTheme(theme);
		}
		void SetTheme(MyTheme theme)
		{ 
			CrossToastPopUp.Current.ShowToastMessage(theme.ToString());
			
		}

		
	}
	
}
