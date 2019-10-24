using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Newtonsoft.Json;
using SteamKit2.Discovery;


namespace sc {
	public sealed class GlobalDatabase : SerializableFile {
		[JsonProperty(PropertyName = "_CellID", Required = Required.DisallowNull)]
		private uint BackingCellID;

		//internal readonly InMemoryServerListProvider ServerListProvider = new InMemoryServerListProvider();

		[JsonConstructor]
		private GlobalDatabase() {
		}

		private GlobalDatabase(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			FilePath = filePath;
		}

		internal uint CellID {
			get => BackingCellID;

			set {
				if (BackingCellID == value) {
					return;
				}

				BackingCellID = value;
				Utilities.InBackground(Save);
			}
		}

		internal static GlobalDatabase CreateOrLoad(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				sc.Logger.LogNullError(nameof(filePath));
				return null;
			}

			string filePathLog = "db.json";
			if (!File.Exists(filePath)) {
				sc.Logger.LogGenericInfo(string.Format( Strings.FileCreated,filePathLog));
				return new GlobalDatabase(filePath);
			}

			GlobalDatabase globalDatabase;

			try {
				string json = File.ReadAllText(filePath);
				if (string.IsNullOrEmpty(json)) {
					sc.Logger.LogGenericError(string.Format(Strings.ErrorObjectIsNull, nameof(json)));
					return null;
				}

				globalDatabase = JsonConvert.DeserializeObject<GlobalDatabase>(json);
			} catch (Exception e) {
				sc.Logger.LogGenericWarningException(e);
				return null;
			}

			if (globalDatabase == null) {
				sc.Logger.LogNullError(nameof(globalDatabase));
				return null;
			}

			globalDatabase.FilePath = filePath;
			sc.Logger.LogGenericInfo(string.Format( Strings.FileLoaded,filePathLog));
			return globalDatabase;
		}
	
	}
}
