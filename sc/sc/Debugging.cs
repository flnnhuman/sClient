using SteamKit2;

namespace sc
{
    internal static class Debugging
    {
#if DEBUG
        private static bool IsDebugBuild => true;
#else
		 internal static bool IsDebugBuild => false;
#endif


        internal static bool IsDebugConfigured => sc.GlobalConfig.Debug;

        internal static bool IsUserDebugging => IsDebugBuild || IsDebugConfigured;

        internal sealed class DebugListener : IDebugListener
        {
            public void WriteLine(string category, string msg)
            {
                if (string.IsNullOrEmpty(category) && string.IsNullOrEmpty(msg))
                {
                    sc.Logger.LogNullError(nameof(category) + " && " + nameof(msg));

                    return;
                }

                sc.Logger.LogGenericDebug(category + " | " + msg);
            }
        }
    }
}