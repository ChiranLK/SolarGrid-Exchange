namespace SolarMicrogrid.API.Settings
{
    public class MongoSettings
    {
        public const string SectionName = "MongoDbSettings";

        public string ConnectionString { get; set; } = string.Empty;

        public string DatabaseName { get; set; } = string.Empty;

        public string UsersCollectionName { get; set; } = "Users";

        public string SolarStationInfoCollectionName { get; set; } = "SolarStationInfo";

    }
}