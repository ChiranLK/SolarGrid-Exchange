namespace SolarMicrogrid.API.Models.DTOs.Stations;

public sealed class NearbyStationResponseDto
{
    public StationResponseDto Station { get; set; } = new();

    public double DistanceKm { get; set; }
}
