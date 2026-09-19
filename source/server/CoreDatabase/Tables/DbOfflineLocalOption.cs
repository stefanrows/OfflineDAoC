using DOL.Database.Attributes;

namespace DOL.Database
{
    /// <summary>
    /// Small fork-local key/value store used for installation state that is not
    /// part of the upstream game schema.
    /// </summary>
    [DataTable(TableName = "offline_local_options")]
    public sealed class DbOfflineLocalOption : DataObject
    {
        private string _key = string.Empty;
        private string _value = string.Empty;

        [PrimaryKey]
        public string Key
        {
            get => _key;
            set
            {
                _key = value;
                Dirty = true;
            }
        }

        [DataElement(AllowDbNull = false)]
        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                Dirty = true;
            }
        }
    }
}
