using System.Globalization;
using System.IO;
using System.Threading;
using Plugin.Toast;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

[assembly: XamlCompilation(XamlCompilationOptions.Compile)]

namespace sc
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            sc.InitializeGlobalConfigAndDatabase();
            MainPage = new SplashScreen();
            //MainPage = new MainPage();
            //MainPage = new NavigationPage(new second_page());
            if (!Directory.Exists(Path.Combine(Bot.MainDir, "config")))
                Directory.CreateDirectory(Path.Combine(Bot.MainDir, "config"));
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(sc.GlobalConfig.Language);
        }

        protected override async void OnResume()
        {
            base.OnResume();
            Theme theme = await DependencyService.Get<IEnvironment>().GetOperatingSystemTheme();
            SetTheme(theme);
        }

        protected override void OnSleep()
        {
            // Handle when your app sleeps
        }

        protected override async void OnStart()
        {
            base.OnStart();
            Theme theme = await DependencyService.Get<IEnvironment>().GetOperatingSystemTheme();
            SetTheme(theme);
        }

        private void SetTheme(Theme theme)
        {
            CrossToastPopUp.Current.ShowToastMessage(theme.ToString());
        }
    }
}