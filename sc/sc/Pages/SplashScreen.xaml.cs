using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace sc
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class SplashScreen : ContentPage
    {
        private Image SplashImage;
        public SplashScreen()
        {
            //InitializeComponent();
            NavigationPage.SetHasNavigationBar(this,false);
            var sub = new AbsoluteLayout();
            SplashImage = new Image
            {
                Source = "steam_icon.png",
                WidthRequest = 100,
                HeightRequest = 100
            };
            AbsoluteLayout.SetLayoutFlags(SplashImage,AbsoluteLayoutFlags.PositionProportional);
            AbsoluteLayout.SetLayoutBounds(SplashImage,new Rectangle(0.5, 0.5,AbsoluteLayout.AutoSize,AbsoluteLayout.AutoSize));
            sub.Children.Add(SplashImage);
            this.BackgroundColor= Color.Azure;
            this.Content = sub;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await SplashImage.ScaleTo(1, 2000);
            await SplashImage.ScaleTo(0.9, 1800,Easing.Linear);
            await SplashImage.ScaleTo(150, 1200,Easing.Linear);
            Application.Current.MainPage = new MainPage();
        }
    }
}