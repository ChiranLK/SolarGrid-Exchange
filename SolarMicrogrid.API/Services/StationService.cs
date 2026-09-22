using System.Globalization;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GeoJsonObjectModel;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Stations;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Services;

public sealed class StationService
{
    private const int MaximumPageSize = 100;
    private const int MaximumNearbyResults = 100;
    private const double EarthRadiusKm = 6371.0088;

    private readonly MongoDbContext _context;

    public StationService(MongoDbContext context)
    {
        _context = context;
    }

    public async Task<PagedStationResponseDto> GetStationsAsync(
        StationListQueryDto query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var filters = new List<FilterDefinition<SolarStationInfo>>();

        string? search = NormalizeOptional(query.Search);
        if (search is not null)
        {
            var pattern = new BsonRegularExpression(Regex.Escape(search), "i");
            filters.Add(Builders<SolarStationInfo>.Filter.Or(
                Builders<SolarStationInfo>.Filter.Regex(station => station.Name, pattern),
                Builders<SolarStationInfo>.Filter.Regex(station => station.Address, pattern)));
        }

        if (query.IsActive.HasValue)
        {
            filters.Add(Builders<SolarStationInfo>.Filter.Eq(
                station => station.IsActive,
                query.IsActive.Value));
        }

        FilterDefinition<SolarStationInfo> filter = filters.Count == 0
            ? Builders<SolarStationInfo>.Filter.Empty
            : Builders<SolarStationInfo>.Filter.And(filters);

        long totalCount = await _context.Stations.CountDocumentsAsync(
            filter,
            cancellationToken: cancellationToken);

        int skip = checked((page - 1) * pageSize);
        List<SolarStationInfo> stations = await _context.Stations
            .Find(filter)
            .SortBy(station => station.Name)
            .ThenBy(station => station.Id)
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedStationResponseDto
        {
            Items = stations.Select(MapToResponse).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<StationResponseDto?> GetStationByIdAsync(
        string stationId,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(stationId, out _))
        {
            return null;
        }

        SolarStationInfo? station = await _context.Stations
            .Find(item => item.Id == stationId)
            .FirstOrDefaultAsync(cancellationToken);

        return station is null ? null : MapToResponse(station);
    }

    public async Task<IReadOnlyList<NearbyStationResponseDto>> GetNearbyStationsAsync(
        double latitude,
        double longitude,
        double radiusKm,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        ValidateCoordinates(latitude, longitude);

        if (!double.IsFinite(radiusKm) || radiusKm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusKm), "Radius must be greater than zero.");
        }

        int resultLimit = Math.Clamp(maximumResults, 1, MaximumNearbyResults);
        var point = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
            new GeoJson2DGeographicCoordinates(longitude, latitude));

        FilterDefinition<SolarStationInfo> filter = Builders<SolarStationInfo>.Filter.And(
            Builders<SolarStationInfo>.Filter.Eq(station => station.IsActive, true),
            Builders<SolarStationInfo>.Filter.NearSphere(
                station => station.Location,
                point,
                maxDistance: radiusKm * 1000));

        List<SolarStationInfo> stations = await _context.Stations
            .Find(filter)
            .Limit(resultLimit)
            .ToListAsync(cancellationToken);

        return stations
            .Select(station => new NearbyStationResponseDto
            {
                Station = MapToResponse(station),
                DistanceKm = CalculateDistanceKm(
                    latitude,
                    longitude,
                    station.Location.Coordinates.Latitude,
                    station.Location.Coordinates.Longitude)
            })
            .ToList();
    }

    public async Task<StationResponseDto> CreateStationAsync(
        CreateStationRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateCoordinates(request.Latitude, request.Longitude);
        ValidateCapacities(request.EnergyGenerationCapacityKw, request.BatteryStorageCapacityKwh);
        List<StationOperatingSchedule> schedule = ValidateAndMapSchedule(request.OperatingSchedule);
        DateTime now = DateTime.UtcNow;

        var station = new SolarStationInfo
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = NormalizeRequired(request.Name, nameof(request.Name), 120),
            Description = NormalizeOptional(request.Description, 500),
            Address = NormalizeRequired(request.Address, nameof(request.Address), 250),
            Location = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
                new GeoJson2DGeographicCoordinates(request.Longitude, request.Latitude)),
            EnergyGenerationCapacityKw = request.EnergyGenerationCapacityKw,
            BatteryStorageCapacityKwh = request.BatteryStorageCapacityKwh,
            OperatingSchedule = schedule,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _context.Stations.InsertOneAsync(
            station,
            cancellationToken: cancellationToken);

        return MapToResponse(station);
    }

    public async Task<StationResponseDto?> UpdateStationAsync(
        string stationId,
        UpdateStationRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ObjectId.TryParse(stationId, out _))
        {
            return null;
        }

        ValidateCoordinates(request.Latitude, request.Longitude);
        ValidateCapacities(request.EnergyGenerationCapacityKw, request.BatteryStorageCapacityKwh);
        List<StationOperatingSchedule> schedule = ValidateAndMapSchedule(request.OperatingSchedule);

        SolarStationInfo? station = await _context.Stations
            .Find(item => item.Id == stationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (station is null)
        {
            return null;
        }

        station.Name = NormalizeRequired(request.Name, nameof(request.Name), 120);
        station.Description = NormalizeOptional(request.Description, 500);
        station.Address = NormalizeRequired(request.Address, nameof(request.Address), 250);
        station.Location = new GeoJsonPoint<GeoJson2DGeographicCoordinates>(
            new GeoJson2DGeographicCoordinates(request.Longitude, request.Latitude));
        station.EnergyGenerationCapacityKw = request.EnergyGenerationCapacityKw;
        station.BatteryStorageCapacityKwh = request.BatteryStorageCapacityKwh;
        station.OperatingSchedule = schedule;
        station.UpdatedAtUtc = DateTime.UtcNow;

        await _context.Stations.ReplaceOneAsync(
            item => item.Id == stationId,
            station,
            cancellationToken: cancellationToken);

        return MapToResponse(station);
    }

    public async Task<StationResponseDto?> ActivateStationAsync(
        string stationId,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(stationId, out _))
        {
            return null;
        }

        var update = Builders<SolarStationInfo>.Update
            .Set(station => station.IsActive, true)
            .Set(station => station.UpdatedAtUtc, DateTime.UtcNow);

        SolarStationInfo? station = await _context.Stations.FindOneAndUpdateAsync(
            item => item.Id == stationId,
            update,
            new FindOneAndUpdateOptions<SolarStationInfo>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        return station is null ? null : MapToResponse(station);
    }

    public async Task<StationResponseDto?> DeactivateStationAsync(
        string stationId,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(stationId, out _))
        {
            return null;
        }

        SolarStationInfo? station = await _context.Stations
            .Find(item => item.Id == stationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (station is null)
        {
            return null;
        }

        if (!station.IsActive)
        {
            return MapToResponse(station);
        }

        // Integration blocker: Member 3 has not defined the reservation fields or
        // active-reservation statuses needed for a safe query. The station must stay
        // active until that contract exists and the check can positively allow it.
        throw new ConflictException(
            "Station deactivation is unavailable until active energy reservations can be checked safely.");
    }

    private static List<StationOperatingSchedule> ValidateAndMapSchedule(
        IReadOnlyCollection<OperatingScheduleDto>? scheduleItems)
    {
        if (scheduleItems is null || scheduleItems.Count == 0)
        {
            throw new ArgumentException("At least one operating-schedule entry is required.");
        }

        if (scheduleItems.Count > 7)
        {
            throw new ArgumentException("An operating schedule cannot contain more than seven days.");
        }

        var days = new HashSet<DayOfWeek>();
        var schedule = new List<StationOperatingSchedule>(scheduleItems.Count);

        foreach (OperatingScheduleDto item in scheduleItems)
        {
            string dayText = NormalizeRequired(item.DayOfWeek, nameof(item.DayOfWeek), 9);
            if (!Enum.TryParse(dayText, ignoreCase: true, out DayOfWeek day))
            {
                throw new ArgumentException($"'{dayText}' is not a valid day of the week.");
            }

            if (!days.Add(day))
            {
                throw new ArgumentException($"The operating schedule contains duplicate entries for {day}.");
            }

            if (!item.IsOpen)
            {
                schedule.Add(new StationOperatingSchedule
                {
                    DayOfWeek = day,
                    IsOpen = false
                });
                continue;
            }

            if (!TryParseTime(item.OpeningTime, out TimeOnly openingTime) ||
                !TryParseTime(item.ClosingTime, out TimeOnly closingTime))
            {
                throw new ArgumentException(
                    $"Open schedule entries for {day} require opening and closing times in HH:mm format.");
            }

            if (closingTime <= openingTime)
            {
                throw new ArgumentException($"Closing time must be after opening time for {day}.");
            }

            schedule.Add(new StationOperatingSchedule
            {
                DayOfWeek = day,
                IsOpen = true,
                OpeningTime = openingTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                ClosingTime = closingTime.ToString("HH:mm", CultureInfo.InvariantCulture)
            });
        }

        return schedule.OrderBy(item => item.DayOfWeek).ToList();
    }

    private static bool TryParseTime(string? value, out TimeOnly result)
    {
        return TimeOnly.TryParseExact(
            value?.Trim(),
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }

    private static void ValidateCoordinates(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
        }

        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");
        }
    }

    private static void ValidateCapacities(decimal generationCapacity, decimal batteryCapacity)
    {
        if (generationCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generationCapacity),
                "Energy-generation capacity must be greater than zero.");
        }

        if (batteryCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batteryCapacity),
                "Battery-storage capacity cannot be negative.");
        }
    }

    private static string NormalizeRequired(string? value, string parameterName, int maximumLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength = int.MaxValue)
    {
        string? normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    private static StationResponseDto MapToResponse(SolarStationInfo station)
    {
        return new StationResponseDto
        {
            Id = station.Id,
            Name = station.Name,
            Description = station.Description,
            Address = station.Address,
            Latitude = station.Location.Coordinates.Latitude,
            Longitude = station.Location.Coordinates.Longitude,
            EnergyGenerationCapacityKw = station.EnergyGenerationCapacityKw,
            BatteryStorageCapacityKwh = station.BatteryStorageCapacityKwh,
            OperatingSchedule = station.OperatingSchedule
                .OrderBy(item => item.DayOfWeek)
                .Select(item => new OperatingScheduleDto
                {
                    DayOfWeek = item.DayOfWeek.ToString(),
                    IsOpen = item.IsOpen,
                    OpeningTime = item.OpeningTime,
                    ClosingTime = item.ClosingTime
                })
                .ToList(),
            IsActive = station.IsActive,
            CreatedAtUtc = station.CreatedAtUtc,
            UpdatedAtUtc = station.UpdatedAtUtc
        };
    }

    private static double CalculateDistanceKm(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        double latitudeDelta = DegreesToRadians(latitude2 - latitude1);
        double longitudeDelta = DegreesToRadians(longitude2 - longitude1);
        double firstLatitude = DegreesToRadians(latitude1);
        double secondLatitude = DegreesToRadians(latitude2);

        double haversine = Math.Pow(Math.Sin(latitudeDelta / 2), 2) +
                           Math.Cos(firstLatitude) * Math.Cos(secondLatitude) *
                           Math.Pow(Math.Sin(longitudeDelta / 2), 2);

        return EarthRadiusKm * 2 * Math.Asin(Math.Min(1, Math.Sqrt(haversine)));
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }
}
