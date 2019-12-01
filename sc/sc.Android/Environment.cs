using System;
using System.Threading.Tasks;
using Android.Content.Res;
using Android.OS;
using Plugin.CurrentActivity;
using sc.Android;
using Xamarin.Forms;

[assembly: Dependency(typeof(Environment_Android))]

namespace sc.Android
{
    public class Environment_Android : IEnvironment
    {
        public Task<Theme> GetOperatingSystemTheme()
        {
            //Ensure the device is running Android Froyo or higher because UIMode was added in Android Froyo, API 8.0
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Froyo)
            {
                UiMode uiModeFlags = CrossCurrentActivity.Current.AppContext.Resources.Configuration.UiMode &
                                     UiMode.NightMask;

                switch (uiModeFlags)
                {
                    case UiMode.NightYes:
                        return Task.FromResult(Theme.Dark);

                    case UiMode.NightNo:
                        return Task.FromResult(Theme.Light);

                    default:
                        throw new NotSupportedException($"UiMode {uiModeFlags} not supported");
                }
            }

            return Task.FromResult(Theme.Light);
        }
    }
}