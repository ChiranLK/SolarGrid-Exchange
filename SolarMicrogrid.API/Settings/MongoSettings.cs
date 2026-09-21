namespace SolarMicrogrid.API.Settings
{
    public class MongoSettings
    {
        public const string SectionName = "MongoSettings";

        public string ConnectionString { get; set; } = string.Empty;

        public string DatabaseName { get; set; } = "SolarGridExchangeDb";

        public string UsersCollectionName { get; set; } = "Users";

        public string StationsCollectionName { get; set; } = "SolarStationInfo";

        public string SlotsCollectionName { get; set; } = "EnergyBookingSlots";

        public string ReservationsCollectionName { get; set; } = "EnergyReservations";
    }
}
