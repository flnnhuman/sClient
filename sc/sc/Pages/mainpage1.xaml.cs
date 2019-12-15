using System;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace sc
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class mainpage1 : MasterDetailPage
    {
        public second_page SecondPage;
        public Friends Friends;
        
        public mainpage1()
        {
            InitializeComponent();
            SecondPage = new second_page();
            Friends=new Friends();
            Detail = new NavigationPage(SecondPage);
        }

        private void CommunityPage(object sender, EventArgs e)
        {
            var Source = WebHandler.SteamCommunityURL;
            if (SecondPage?.Source == Source)
            {
                IsPresented = false;
                return;
            }

            if (SecondPage == null) SecondPage = new second_page {Source = Source};
            SecondPage.Source = Source;
            Detail = new NavigationPage(SecondPage);
            IsPresented = false;
        }

        private void StorePage(object sender, EventArgs e)
        {
            var Source = WebHandler.SteamStoreURL;
            if (SecondPage?.Source == Source)
            {
                IsPresented = false;
                return;
            }

            if (SecondPage == null) SecondPage = new second_page {Source = Source};
            else SecondPage.Source = Source;

            Detail = new NavigationPage(SecondPage);
            IsPresented = false;
        }

        private void FriendsPage(object sender, EventArgs e)
        {
            Detail = new NavigationPage(Friends);
            IsPresented = false;
        }

        private void ProfilePage(object sender, EventArgs e)
        {
            var Source = WebHandler.SteamCommunityURL + "/my";
            if (SecondPage?.Source == Source)
            {
                IsPresented = false;
                return;
            }

            if (SecondPage == null) SecondPage = new second_page {Source = Source};
            SecondPage.Source = Source;
            Detail = new NavigationPage(SecondPage);
            IsPresented = false;
        }

        public void OpenWebPage(string uri)
        {
            if (SecondPage?.Source == uri)
            {
                IsPresented = false;
                SecondPage.Refresh();
                return;
            }

            if (SecondPage == null) SecondPage = new second_page {Source = uri};
            else
                Device.BeginInvokeOnMainThread(()=>SecondPage.Source = uri);

            Detail = new NavigationPage(SecondPage);
            IsPresented = false;
        }
        private void CameraPage(object sender, EventArgs e)
        {
            Detail = new NavigationPage(new CameraPage());
            IsPresented = false;
        }
        
        private void LogOut(object sender, EventArgs e)
        {
            sc.bot.Stop();
        }
    }
}