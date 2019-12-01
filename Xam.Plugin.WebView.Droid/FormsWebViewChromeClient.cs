using System;
using Android.Webkit;

namespace Xam.Plugin.WebView.Droid
{
    public class FormsWebViewChromeClient : WebChromeClient
    {
        private readonly WeakReference<FormsWebViewRenderer> Reference;

        public FormsWebViewChromeClient(FormsWebViewRenderer renderer)
        {
            Reference = new WeakReference<FormsWebViewRenderer>(renderer);
        }
    }
}