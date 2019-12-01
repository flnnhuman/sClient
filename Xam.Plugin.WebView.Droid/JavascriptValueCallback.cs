using System;
using Android.Webkit;
using Object = Java.Lang.Object;

namespace Xam.Plugin.WebView.Droid
{
    public class JavascriptValueCallback : Object, IValueCallback
    {
        private readonly WeakReference<FormsWebViewRenderer> Reference;

        public JavascriptValueCallback(FormsWebViewRenderer renderer)
        {
            Reference = new WeakReference<FormsWebViewRenderer>(renderer);
        }

        public Object Value { get; private set; }

        public void OnReceiveValue(Object value)
        {
            if (Reference == null || !Reference.TryGetTarget(out FormsWebViewRenderer renderer)) return;
            Value = value;
        }

        public void Reset()
        {
            Value = null;
        }
    }
}