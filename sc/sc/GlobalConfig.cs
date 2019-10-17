using Newtonsoft.Json;

namespace sc
{
    public class GlobalConfig
    {
        private const bool DefaultDebug = false;
        public static readonly bool Debug = DefaultDebug;

        internal const byte DefaultConnectionTimeout = 90;
        internal const byte DefaultLoginLimiterDelay = 10;
        private const ushort DefaultWebLimiterDelay = 300;

        [JsonProperty(Required = Required.DisallowNull)]
        public static readonly byte ConnectionTimeout = DefaultConnectionTimeout;

        [JsonProperty(Required = Required.DisallowNull)]
        public static readonly byte LoginLimiterDelay = DefaultLoginLimiterDelay;

        public readonly ushort WebLimiterDelay = DefaultWebLimiterDelay;
    }
}