using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

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
			SetCookieClicked(null,null);
		}
		
		

		private async void SetCookieClicked(object sender, EventArgs e)
		{
			foreach (MyCookie myCookie in MainPage.bot.WebHandler.WebBrowser.Cookies)
			{
				var expiresDate = DateTime.Now;
				expiresDate = expiresDate.AddDays(30);
				
				Cookie cookie = new Cookie();
				cookie.Name = myCookie.Name;
				cookie.Value = myCookie.Value;
				cookie.Domain = (new Uri(localContent.Source).Host);
				cookie.Expired = false;
				cookie.Expires = expiresDate;
				cookie.Path = "/";	
				var str = await localContent.SetCookieAsync(cookie);
			}
		}

		private async void GetCookieClicked(object sender, EventArgs e)
		{
			Debug.WriteLine(await localContent.GetCookieAsync("sessionid")); 
			Debug.WriteLine(await localContent.GetCookieAsync("steamLoginSecure"));
		}

		private async void GetAllCookiesClicked(object sender, EventArgs e) {
			Debug.WriteLine(await localContent.GetAllCookiesAsync());
		}

		void OnRefreshPageClicked(object sender, EventArgs e)
		{
			localContent.Refresh();
		}

		private async void ClearAllCookiesClicked(object sender, EventArgs e)
		{
			await localContent.ClearCookiesAsync();
		}
		private async void JsClicked(object sender, EventArgs e)
		{
		await localContent.InjectJavascriptAsync("document.getElementById(\"responsive_page_menu\").style.display=\"none\";"+
													 "document.getElementById(\"responsive_menu_logo\").style.display=\"none\";"+
			                                         "document.getElementsByClassName(\"responsive_header\")[0].style.display = \"none\";"+
			                                         "document.getElementById(\"ModalContentContainer\").style.marginTop = \"-50px\";");	//todo динамический отступ
		}
		
	}
}
