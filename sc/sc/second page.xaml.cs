using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static sc.Steam;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using static sc.MainPage;
namespace sc
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class second_page : ContentPage
    {
        public second_page()
        {
            InitializeComponent();
        }
        private void Button2_OnClicked(object sender, EventArgs e)
        {
            if (SteamUser != null)
            {
                //Toast(SteamClient.SteamID.ToString(),Steam.EToastType.Message);
                SteamUser.LogOff();
               
            }
            else{Toast("login into your account first",Steam.EToastType.Warning);}

        }
    }
}