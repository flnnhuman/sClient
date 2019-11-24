using System;
using System.Net;
using System.Threading.Tasks;
using Acr.UserDialogs;
using Acr.UserDialogs.Infrastructure;
using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using FFImageLoading.Forms.Platform;
using Plugin.CurrentActivity;
using sc.Android;
using Xam.Plugin.WebView.Droid;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Platform.Android;



namespace sc.Android
{
	[Activity(Label = "sc", Theme = "@style/MainTheme", MainLauncher = true,
		ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
	public class MainActivity : FormsAppCompatActivity
	{
		protected override void OnCreate(Bundle savedInstanceState)
		{
			TabLayoutResource = Resource.Layout.Tabbar;
			ToolbarResource = Resource.Layout.Toolbar;
			UserDialogs.Init(this);
			base.OnCreate(savedInstanceState);
			FormsWebViewRenderer.Initialize();
			Forms.Init(this, savedInstanceState);
			CachedImageRenderer.Init(true);
			LoadApplication(new App());
			
		}

	}
	
}