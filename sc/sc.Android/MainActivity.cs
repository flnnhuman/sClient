using System;
using System.Net;
using Acr.UserDialogs;
using Acr.UserDialogs.Infrastructure;
using Android.App;
using Android.Content.PM;
using Android.OS;
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
			LoadApplication(new App());
			
		}

	}
}