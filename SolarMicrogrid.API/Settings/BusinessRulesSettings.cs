namespace SolarMicrogrid.API.Settings
{
    
public class BusinessRules
    {
        public const string SectionName = "BusinessRules";

        public int MaxBookingDaysAhead {get; set;} = 7;

        public int MinChangeNoticeHours {get; set;} = 12;
    }
}