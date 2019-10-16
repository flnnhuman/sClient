using Newtonsoft.Json;

namespace sc {
	public sealed class GlobalDatabase : SerializableFile {
		private static SerializableFile _serializableFile;

		[JsonProperty(PropertyName = "_CellID", Required = Required.DisallowNull)]
		private static uint BackingCellID;

		internal static uint CellID {
			get => BackingCellID;

			set {
				if (BackingCellID == value) {
					return;
				}

				BackingCellID = value;
				Utilities.InBackground(_serializableFile.Save);
			}
		}
	}
}
