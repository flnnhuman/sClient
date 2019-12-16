using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Threading;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace sc
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class second_page : ContentPage
    {
        public second_page()
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(sc.GlobalConfig.Language);
            InitializeComponent();
        }

        public string Source
        {
            get => localContent.Source;
            set => localContent.Source = value;
        }

        public void Refresh()
        {
            localContent.Refresh();
        }

        public async void SetCookieClicked(object sender, EventArgs e)
        {
            if (sc.bot == null) sc.Logger.LogNullError(nameof(sc.bot.WebHandler.WebBrowser.Cookies));
            else
                foreach (MyCookie myCookie in sc.bot.WebHandler.WebBrowser.Cookies)
                {
                    DateTime expiresDate = DateTime.Now;
                    expiresDate = expiresDate.AddDays(30);

                    var communityCookie = new Cookie
                    {
                        Name = myCookie.Name,
                        Value = myCookie.Value,
                        Domain = WebHandler.SteamCommunityURL,
                        Expired = false,
                        Expires = expiresDate,
                        Path = "/"
                    };
                    var marketCookie = new Cookie
                    {
                        Name = myCookie.Name,
                        Value = myCookie.Value,
                        Domain = WebHandler.SteamStoreURL,
                        Expired = false,
                        Expires = expiresDate,
                        Path = "/"
                    };
                    var helpCookie = new Cookie
                    {
                        Name = myCookie.Name,
                        Value = myCookie.Value,
                        Domain = WebHandler.SteamHelpURL,
                        Expired = false,
                        Expires = expiresDate,
                        Path = "/"
                    };
                    string str1 =await localContent.SetCookieAsync(communityCookie).ConfigureAwait(false);
                    string str2 =await localContent.SetCookieAsync(marketCookie).ConfigureAwait(true);
                    string str3 =await localContent.SetCookieAsync(helpCookie);
                }
        }

        private void OnRefreshPageClicked(object sender, EventArgs e)
        {
            localContent.Refresh();
        }

        private async void ClearAllCookiesClicked(object sender, EventArgs e)
        {
            await localContent.ClearCookiesAsync();
        }

        private async void JsClicked(object sender, EventArgs e)
        {
            await localContent.InjectJavascriptAsync(
                "document.getElementById(\"responsive_page_menu\").style.display=\"none\";" +
                "document.getElementById(\"responsive_menu_logo\").style.display=\"none\";" +
                "document.getElementsByClassName(\"responsive_header\")[0].style.display = \"none\";" +
                "document.getElementById(\"ModalContentContainer\").style.marginTop = \"-50px\";"); //todo динамический отступ
        }
    }
}