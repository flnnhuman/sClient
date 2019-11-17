using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace sc
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class mainpage1 : MasterDetailPage
    {
        private second_page SecondPage;
        public mainpage1()
        {
            InitializeComponent();
            Detail = new NavigationPage(new MainPage());
            
        }

        void CommunityPage(object sender, EventArgs e)
        {
            if (SecondPage?.Source==WebHandler.SteamCommunityURL){IsPresented = false; return;}
            if (SecondPage == null) SecondPage=new second_page(){Source =WebHandler.SteamCommunityURL};
            SecondPage.Source = WebHandler.SteamCommunityURL;
            Detail = new NavigationPage(SecondPage);
            IsPresented = false;
           
           
        }
        void StorePage(object sender, EventArgs e)
        {
            if (SecondPage?.Source==WebHandler.SteamStoreURL){IsPresented = false; return;}
            if (SecondPage == null) SecondPage=new second_page(){Source =WebHandler.SteamStoreURL};
                else SecondPage.Source = WebHandler.SteamStoreURL;
            
            Detail = new NavigationPage(SecondPage);
            IsPresented = false;
        }
    }
}