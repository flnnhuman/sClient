using System;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace sc
{
    public class sc
    {
        [PublicAPI] public static readonly Logger Logger = new Logger(nameof(sc));

        [PublicAPI] public static GlobalConfig GlobalConfig { get; private set; }
        [PublicAPI] public static GlobalDatabase GlobalDatabase { get; private set; }
        [PublicAPI] public static WebBrowser WebBrowser { get; internal set; }


        internal static void InitGlobalConfig(GlobalConfig globalConfig)
        {
            if (globalConfig == null)
            {
                Logger.LogNullError(nameof(globalConfig));

                return;
            }

            if (GlobalConfig != null) return;

            GlobalConfig = globalConfig;
        }

        internal static void InitGlobalDatabase(GlobalDatabase globalDatabase)
        {
            if (globalDatabase == null)
            {
                Logger.LogNullError(nameof(globalDatabase));

                return;
            }

            if (GlobalDatabase != null) return;

            GlobalDatabase = globalDatabase;
        }

        internal enum EUserInputType : byte
        {
            Unknown,
            DeviceID,
            Login,
            Password,
            SteamGuard,
            SteamParentalCode,
            TwoFactorAuthentication
        }
    }
}