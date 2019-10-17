using Newtonsoft.Json;

namespace sc
{
    public sealed class GlobalDatabase : SerializableFile
    {
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
    }
}