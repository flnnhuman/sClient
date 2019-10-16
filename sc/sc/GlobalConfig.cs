using Newtonsoft.Json;

namespace sc {
	public class GlobalConfig {
		private const bool DefaultDebug = false;

		internal const byte DefaultConnectionTimeout = 90;
		internal const byte DefaultLoginLimiterDelay = 10;
		private const ushort DefaultWebLimiterDelay = 300;
		public static readonly bool Debug = DefaultDebug;

		[JsonProperty(Required = Required.DisallowNull)]
		public static readonly byte ConnectionTimeout = DefaultConnectionTimeout;

		[JsonProperty(Required = Required.DisallowNull)]
		public static readonly byte LoginLimiterDelay = DefaultLoginLimiterDelay;

		public readonly ushort WebLimiterDelay = DefaultWebLimiterDelay;
	}
}
