using System;
using System.IO;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace sc {
	public class BotDatabase : SerializableFile {
		[JsonProperty(PropertyName = "_LoginKey")]
		private string BackingLoginKey;

		//  [JsonProperty(PropertyName = "_MobileAuthenticator")]
		// private MobileAuthenticator BackingMobileAuthenticator;
		// 
		// internal MobileAuthenticator MobileAuthenticator {
		//     get => BackingMobileAuthenticator;

		//     set {
		//         if (BackingMobileAuthenticator == value) {
		//             return;
		//         }

		//         BackingMobileAuthenticator = value;
		//         Utilities.InBackground(Save);
		//     }
		// }


		public BotDatabase([NotNull] string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			FilePath = filePath;
		}
		[JsonConstructor]
		private BotDatabase() { }
		
		internal string LoginKey {
			get => BackingLoginKey;

			set {
				if (BackingLoginKey == value) {
					return;
				}

				BackingLoginKey = value;
				Utilities.InBackground(Save);
			}
		}
		internal static async Task<BotDatabase> CreateOrLoad(string filePath)
		{
			string filePathLog=filePath;
			filePath = Path.Combine(Bot.MainDir, filePath);
			if (string.IsNullOrEmpty(filePath)) {
				sc.Logger.LogNullError(nameof(filePath));

				return null;
			}

			if (!File.Exists(filePath)) {
				sc.Logger.LogGenericInfo(string.Format( Strings.FileCreated,filePathLog));
				return new BotDatabase(filePath);
			}
			
			BotDatabase botDatabase;

			try {
				string json = File.ReadAllText(filePath);

				if (string.IsNullOrEmpty(json)) {
					sc.Logger.LogGenericError(string.Format(Strings.ErrorIsEmpty, nameof(json)));

					return null;
				}

				botDatabase = JsonConvert.DeserializeObject<BotDatabase>(json);
			} catch (Exception e) {
				sc.Logger.LogGenericException(e);

				return null;
			}

			if (botDatabase == null) {
				sc.Logger.LogNullError(nameof(botDatabase));

				return null;
			}

			botDatabase.FilePath = filePath;

			sc.Logger.LogGenericInfo(string.Format( Strings.FileLoaded,filePathLog));
			return botDatabase;
		}

	}
}
