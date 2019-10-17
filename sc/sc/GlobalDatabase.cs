using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace sc
{
    public sealed class GlobalDatabase : SerializableFile
    {
        [JsonConstructor]
        private GlobalDatabase() {}
        private GlobalDatabase(string filePath) {
            if (string.IsNullOrEmpty(filePath)) {
                throw new ArgumentNullException(nameof(filePath));
            }
            FilePath = filePath;
        }
        
        [JsonProperty(PropertyName = "_CellID", Required = Required.DisallowNull)]
        private uint BackingCellID;

        internal uint CellID
        {
            get => BackingCellID;

            set
            {
                if (BackingCellID == value) return;

                BackingCellID = value;
                Utilities.InBackground(Save);
            }
        }
        
        internal static GlobalDatabase CreateOrLoad(string filePath) {
            if (string.IsNullOrEmpty(filePath)) {
                sc.Logger.LogNullError(nameof(filePath));
                return null;
            }

            if (!File.Exists(filePath)) {
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

            return globalDatabase;
        }
    }
}