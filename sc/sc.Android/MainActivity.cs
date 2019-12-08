using Acr.UserDialogs;
using Android.App;
using Android.Content.PM;
using Android.OS;
using FFImageLoading.Forms.Platform;
using Xam.Plugin.WebView.Droid;
using Xamarin.Forms;
using Xamarin.Forms.Platform.Android;
using Plugin.Media;
using Plugin.Permissions;
using Plugin.CurrentActivity;
using Permission = Android.Content.PM.Permission;

namespace sc.Android
{
    [Activity(Label = "sc", Theme = "@style/MainTheme", MainLauncher = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
    public class MainActivity : FormsAppCompatActivity
    {
        protected override async void OnCreate(Bundle savedInstanceState)
        {
            TabLayoutResource = Resource.Layout.Tabbar;
            ToolbarResource = Resource.Layout.Toolbar;
            UserDialogs.Init(this);
            base.OnCreate(savedInstanceState);
            FormsWebViewRenderer.Initialize();
            CrossCurrentActivity.Current.Init(this, savedInstanceState);
            await CrossMedia.Current.Initialize();
            Forms.Init(this, savedInstanceState);
            CachedImageRenderer.Init(true);
            LoadApplication(new App());
        }
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            PermissionsImplementation.Current.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }
}