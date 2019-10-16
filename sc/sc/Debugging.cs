using SteamKit2;

namespace sc
{
    internal  class Debugging {
#if DEBUG
         internal static bool IsDebugBuild => true;
#else
		 internal static bool IsDebugBuild => false;
#endif

        private static  Logger _logger;
        internal static bool IsDebugConfigured => sc.GlobalConfig.Debug;

        internal static bool IsUserDebugging => IsDebugBuild || IsDebugConfigured;

        internal sealed class DebugListener : IDebugListener {
            public void WriteLine(string category, string msg) {
                if (string.IsNullOrEmpty(category) && string.IsNullOrEmpty(msg)) {
                    _logger.LogNullError(nameof(category) + " && " + nameof(msg));

                    return;
                }

                _logger.LogGenericDebug(category + " | " + msg);
            }
        }
    }
}