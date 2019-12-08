using System;
using System.Diagnostics;
using System.Net;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace sc
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class second_page : ContentPage
    {
        public second_page()
        {
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

        private async void SetCookieClicked(object sender, EventArgs e)
        {
            if (sc.bot == null) sc.Logger.LogNullError(nameof(sc.bot.WebHandler.WebBrowser.Cookies));
            else
                foreach (MyCookie myCookie in sc.bot.WebHandler.WebBrowser.Cookies)
                {
                    DateTime expiresDate = DateTime.Now;
                    expiresDate = expiresDate.AddDays(30);

                    var cookie = new Cookie
                    {
                        Name = myCookie.Name,
                        Value = myCookie.Value,
                        Domain = new Uri(localContent.Source).Host,
                        Expired = false,
                        Expires = expiresDate,
                        Path = "/"
                    };
                    await localContent.SetCookieAsync(cookie);
                }
        }

        private async void GetCookieClicked(object sender, EventArgs e)
        {
            Debug.WriteLine(await localContent.GetCookieAsync("sessionid"));
            Debug.WriteLine(await localContent.GetCookieAsync("steamLoginSecure"));
        }

        private async void GetAllCookiesClicked(object sender, EventArgs e)
        {
            Debug.WriteLine(await localContent.GetAllCookiesAsync());
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