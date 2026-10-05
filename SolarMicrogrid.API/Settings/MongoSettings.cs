/*
 * MongoSettings.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines configured MongoDB database and collection names used by the
 *           central API, including reservation guards and QR transactions.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Settings
{
    public class MongoSettings
    {
        public const string SectionName = "MongoSettings";

        public string ConnectionString { get; set; } = string.Empty;

        public string DatabaseName { get; set; } = "SolarGridExchangeDb";

        // Physical, backward-compatible name for the assignment's logical UserDetail collection.
        public string UsersCollectionName { get; set; } = "Users";

        public string StationsCollectionName { get; set; } = "SolarStationInfo";

        public string SlotsCollectionName { get; set; } = "EnergyBookingSlots";

        // Physical, backward-compatible name for the assignment's logical EnergyReservation collection.
        public string ReservationsCollectionName { get; set; } = "EnergyReservations";

        // Supplemental consistency metadata; not a fifth domain/entity collection.
        public string ReservationSchedulingGuardsCollectionName { get; set; } =
            "ReservationSchedulingGuards";

        // Supplemental security/audit metadata for the required QR completion workflow.
        public string QrTransactionsCollectionName { get; set; } = "QrTransactions";
    }
}
