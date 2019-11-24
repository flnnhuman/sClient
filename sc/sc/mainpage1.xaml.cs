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
        public second_page SecondPage;
        public mainpage1()
        {
            InitializeComponent();
            SecondPage=new second_page();
            Detail = new NavigationPage(SecondPage);
            
        }

        void CommunityPage(object sender, EventArgs e)
        {
            string Source = WebHandler.SteamCommunityURL;
            if (SecondPage?.Source==Source){IsPresented = false; return;}
            if (SecondPage == null) SecondPage=new second_page(){Source =Source};
            SecondPage.Source = Source;
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