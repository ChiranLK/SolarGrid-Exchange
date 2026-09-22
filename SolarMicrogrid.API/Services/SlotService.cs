using System.Globalization;
using MongoDB.Bson;
using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Slots;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Services;

public sealed class SlotService
{
    private const int MaximumPageSize = 100;
    private const string OperatingScheduleTimeZoneId = "Asia/Colombo";

    private static readonly TimeZoneInfo OperatingScheduleTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(OperatingScheduleTimeZoneId);

    private readonly MongoDbContext _context;

    public SlotService(MongoDbContext context)
    {
        _context = context;
    }

    public Task<PagedSlotResponseDto> GetSlotsByStationAsync(
        string stationId,
        SlotListQueryDto query,
        CancellationToken cancellationToken)
    {
        return QuerySlotsAsync(
            stationId,
            query,
            forcedStatus: null,
            requireRemainingCapacity: false,
            cancellationToken);
    }

    public Task<PagedSlotResponseDto> GetAvailableSlotsByStationAsync(
        string stationId,
        SlotListQueryDto query,
        CancellationToken cancellationToken)
    {
        return QuerySlotsAsync(
            stationId,
            query,
            SlotAvailabilityStatus.Available,
            requireRemainingCapacity: true,
            cancellationToken);
    }

    public async Task<SlotResponseDto?> GetSlotByIdAsync(
        string slotId,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(slotId, out _))
        {
            return null;
        }

        EnergyBookingSlot? slot = await _context.Slots
            .Find(item => item.Id == slotId)
            .FirstOrDefaultAsync(cancellationToken);

        return slot is null ? null : MapToResponse(slot);
    }

    public async Task<SlotResponseDto> CreateSlotAsync(
        CreateSlotRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string stationId = NormalizeObjectId(request.StationId, nameof(request.StationId));
        SolarStationInfo station = await GetRequiredStationAsync(stationId, cancellationToken);
        if (!station.IsActive)
        {
            throw new ConflictException("Slots cannot be created for an inactive station.");
        }

        (DateTime startUtc, DateTime endUtc) = ValidateFutureTimeRange(
            request.StartTimeUtc,
            request.EndTimeUtc);
        ValidateCapacity(request.TotalCapacityKwh);
        ValidateSlotFitsOperatingSchedule(station, startUtc, endUtc);
        await EnsureNoOverlapAsync(stationId, startUtc, endUtc, excludedSlotId: null, cancellationToken);

        DateTime now = DateTime.UtcNow;
        var slot = new EnergyBookingSlot
        {
            Id = ObjectId.GenerateNewId().ToString(),
            StationId = stationId,
            StartTimeUtc = startUtc,
            EndTimeUtc = endUtc,
            TotalCapacityKwh = request.TotalCapacityKwh,
            AvailableCapacityKwh = request.TotalCapacityKwh,
            AvailabilityStatus = SlotAvailabilityStatus.Available,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        try
        {
            await _context.Slots.InsertOneAsync(slot, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException(
                "An identical slot already exists for this station and time range.");
        }

        return MapToResponse(slot);
    }

    public async Task<SlotResponseDto?> UpdateSlotAsync(
        string slotId,
        UpdateSlotRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ObjectId.TryParse(slotId, out _))
        {
            return null;
        }

        EnergyBookingSlot? slot = await _context.Slots
            .Find(item => item.Id == slotId)
            .FirstOrDefaultAsync(cancellationToken);

        if (slot is null)
        {
            return null;
        }

        DateTime now = DateTime.UtcNow;
        if (slot.StartTimeUtc <= now)
        {
            throw new ConflictException("Past or already-started slots cannot be updated.");
        }

        SolarStationInfo station = await GetRequiredStationAsync(slot.StationId, cancellationToken);
        (DateTime startUtc, DateTime endUtc) = ValidateFutureTimeRange(
            request.StartTimeUtc,
            request.EndTimeUtc,
            now);
        ValidateCapacity(request.TotalCapacityKwh);
        ValidateSlotFitsOperatingSchedule(station, startUtc, endUtc);
        await EnsureNoOverlapAsync(slot.StationId, startUtc, endUtc, slot.Id, cancellationToken);

        decimal reservedCapacity = slot.TotalCapacityKwh - slot.AvailableCapacityKwh;
        if (reservedCapacity < 0)
        {
            throw new InvalidOperationException("The stored slot capacity values are inconsistent.");
        }

        if (request.TotalCapacityKwh < reservedCapacity)
        {
            throw new ConflictException(
                "Total capacity cannot be lower than the capacity already reserved.");
        }

        decimal availableCapacity = request.TotalCapacityKwh - reservedCapacity;
        SlotAvailabilityStatus status = slot.AvailabilityStatus == SlotAvailabilityStatus.Unavailable
            ? SlotAvailabilityStatus.Unavailable
            : availableCapacity == 0
                ? SlotAvailabilityStatus.FullyBooked
                : SlotAvailabilityStatus.Available;

        FilterDefinition<EnergyBookingSlot> updateFilter = Builders<EnergyBookingSlot>.Filter.And(
            Builders<EnergyBookingSlot>.Filter.Eq(item => item.Id, slotId),
            Builders<EnergyBookingSlot>.Filter.Eq(item => item.TotalCapacityKwh, slot.TotalCapacityKwh),
            Builders<EnergyBookingSlot>.Filter.Eq(item => item.AvailableCapacityKwh, slot.AvailableCapacityKwh));

        UpdateDefinition<EnergyBookingSlot> update = Builders<EnergyBookingSlot>.Update
            .Set(item => item.StartTimeUtc, startUtc)
            .Set(item => item.EndTimeUtc, endUtc)
            .Set(item => item.TotalCapacityKwh, request.TotalCapacityKwh)
            .Set(item => item.AvailableCapacityKwh, availableCapacity)
            .Set(item => item.AvailabilityStatus, status)
            .Set(item => item.UpdatedAtUtc, now);

        try
        {
            EnergyBookingSlot? updatedSlot = await _context.Slots.FindOneAndUpdateAsync(
                updateFilter,
                update,
                new FindOneAndUpdateOptions<EnergyBookingSlot>
                {
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken);

            if (updatedSlot is null)
            {
                throw new ConflictException(
                    "The slot capacity changed while it was being updated. Reload the slot and try again.");
            }

            return MapToResponse(updatedSlot);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException(
                "An identical slot already exists for this station and time range.");
        }
    }

    public async Task<SlotResponseDto?> ChangeSlotAvailabilityAsync(
        string slotId,
        ChangeSlotAvailabilityRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ObjectId.TryParse(slotId, out _))
        {
            return null;
        }

        EnergyBookingSlot? slot = await _context.Slots
            .Find(item => item.Id == slotId)
            .FirstOrDefaultAsync(cancellationToken);

        if (slot is null)
        {
            return null;
        }

        string requestedStatus = request.Status.Trim();
        SlotAvailabilityStatus newStatus;

        if (requestedStatus.Equals(nameof(SlotAvailabilityStatus.Unavailable), StringComparison.OrdinalIgnoreCase))
        {
            newStatus = SlotAvailabilityStatus.Unavailable;
        }
        else if (requestedStatus.Equals(nameof(SlotAvailabilityStatus.Available), StringComparison.OrdinalIgnoreCase))
        {
            if (slot.EndTimeUtc <= DateTime.UtcNow)
            {
                throw new ConflictException("Expired slots cannot be made available.");
            }

            newStatus = slot.AvailableCapacityKwh == 0
                ? SlotAvailabilityStatus.FullyBooked
                : SlotAvailabilityStatus.Available;
        }
        else
        {
            throw new ArgumentException(
                "Availability status must be Available or Unavailable.",
                nameof(request));
        }

        FilterDefinition<EnergyBookingSlot> availabilityFilter =
            Builders<EnergyBookingSlot>.Filter.Eq(item => item.Id, slotId);

        if (newStatus == SlotAvailabilityStatus.Available)
        {
            availabilityFilter &= Builders<EnergyBookingSlot>.Filter.Gt(
                item => item.AvailableCapacityKwh,
                0);
            availabilityFilter &= Builders<EnergyBookingSlot>.Filter.Gt(
                item => item.EndTimeUtc,
                DateTime.UtcNow);
        }

        var update = Builders<EnergyBookingSlot>.Update
            .Set(item => item.AvailabilityStatus, newStatus)
            .Set(item => item.UpdatedAtUtc, DateTime.UtcNow);

        EnergyBookingSlot? updatedSlot = await _context.Slots.FindOneAndUpdateAsync(
            availabilityFilter,
            update,
            new FindOneAndUpdateOptions<EnergyBookingSlot>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        if (updatedSlot is null && newStatus == SlotAvailabilityStatus.Available)
        {
            throw new ConflictException(
                "The slot can no longer be enabled because it expired or its capacity changed.");
        }

        return updatedSlot is null ? null : MapToResponse(updatedSlot);
    }

    private async Task<PagedSlotResponseDto> QuerySlotsAsync(
        string stationId,
        SlotListQueryDto query,
        SlotAvailabilityStatus? forcedStatus,
        bool requireRemainingCapacity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string normalizedStationId = NormalizeObjectId(stationId, nameof(stationId));
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var filters = new List<FilterDefinition<EnergyBookingSlot>>
        {
            Builders<EnergyBookingSlot>.Filter.Eq(slot => slot.StationId, normalizedStationId)
        };

        DateTime? fromUtc = NormalizeOptionalUtc(query.FromUtc, nameof(query.FromUtc));
        DateTime? toUtc = NormalizeOptionalUtc(query.ToUtc, nameof(query.ToUtc));
        if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
        {
            throw new ArgumentException("FromUtc cannot be after ToUtc.");
        }

        if (fromUtc.HasValue)
        {
            filters.Add(Builders<EnergyBookingSlot>.Filter.Gte(
                slot => slot.StartTimeUtc,
                fromUtc.Value));
        }

        if (toUtc.HasValue)
        {
            filters.Add(Builders<EnergyBookingSlot>.Filter.Lte(
                slot => slot.StartTimeUtc,
                toUtc.Value));
        }

        SlotAvailabilityStatus? status = forcedStatus ?? query.Status;
        if (status.HasValue)
        {
            filters.Add(Builders<EnergyBookingSlot>.Filter.Eq(
                slot => slot.AvailabilityStatus,
                status.Value));
        }

        if (requireRemainingCapacity)
        {
            filters.Add(Builders<EnergyBookingSlot>.Filter.Gt(
                slot => slot.AvailableCapacityKwh,
                0));
            filters.Add(Builders<EnergyBookingSlot>.Filter.Gt(
                slot => slot.EndTimeUtc,
                DateTime.UtcNow));
        }

        FilterDefinition<EnergyBookingSlot> filter = Builders<EnergyBookingSlot>.Filter.And(filters);
        long totalCount = await _context.Slots.CountDocumentsAsync(
            filter,
            cancellationToken: cancellationToken);

        int skip = checked((page - 1) * pageSize);
        List<EnergyBookingSlot> slots = await _context.Slots
            .Find(filter)
            .SortBy(slot => slot.StartTimeUtc)
            .ThenBy(slot => slot.Id)
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedSlotResponseDto
        {
            Items = slots.Select(MapToResponse).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private async Task<SolarStationInfo> GetRequiredStationAsync(
        string stationId,
        CancellationToken cancellationToken)
    {
        SolarStationInfo? station = await _context.Stations
            .Find(item => item.Id == stationId)
            .FirstOrDefaultAsync(cancellationToken);

        return station ?? throw new ArgumentException("The specified station does not exist.");
    }

    private async Task EnsureNoOverlapAsync(
        string stationId,
        DateTime startUtc,
        DateTime endUtc,
        string? excludedSlotId,
        CancellationToken cancellationToken)
    {
        FilterDefinition<EnergyBookingSlot> filter = Builders<EnergyBookingSlot>.Filter.And(
            Builders<EnergyBookingSlot>.Filter.Eq(slot => slot.StationId, stationId),
            Builders<EnergyBookingSlot>.Filter.Lt(slot => slot.StartTimeUtc, endUtc),
            Builders<EnergyBookingSlot>.Filter.Gt(slot => slot.EndTimeUtc, startUtc));

        if (excludedSlotId is not null)
        {
            filter &= Builders<EnergyBookingSlot>.Filter.Ne(slot => slot.Id, excludedSlotId);
        }

        bool overlaps = await _context.Slots
            .Find(filter)
            .AnyAsync(cancellationToken);

        if (overlaps)
        {
            throw new ConflictException("The slot overlaps another slot for this station.");
        }
    }

    private static (DateTime StartUtc, DateTime EndUtc) ValidateFutureTimeRange(
        DateTime startTime,
        DateTime endTime,
        DateTime? currentUtc = null)
    {
        DateTime startUtc = NormalizeRequiredUtc(startTime, nameof(startTime));
        DateTime endUtc = NormalizeRequiredUtc(endTime, nameof(endTime));

        if (startUtc >= endUtc)
        {
            throw new ArgumentException("StartTimeUtc must be before EndTimeUtc.");
        }

        if (startUtc <= (currentUtc ?? DateTime.UtcNow))
        {
            throw new ArgumentException("New or updated slots must start in the future.");
        }

        return (startUtc, endUtc);
    }

    private static void ValidateSlotFitsOperatingSchedule(
        SolarStationInfo station,
        DateTime startUtc,
        DateTime endUtc)
    {
        DateTime localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, OperatingScheduleTimeZone);
        DateTime localEnd = TimeZoneInfo.ConvertTimeFromUtc(endUtc, OperatingScheduleTimeZone);

        if (localStart.Date != localEnd.Date)
        {
            throw new ArgumentException("A slot must start and end on the same local operating day.");
        }

        StationOperatingSchedule? schedule = station.OperatingSchedule
            .SingleOrDefault(item => item.DayOfWeek == localStart.DayOfWeek);

        if (schedule is null || !schedule.IsOpen)
        {
            throw new ConflictException("The station is closed during the requested slot.");
        }

        if (!TryParseScheduleTime(schedule.OpeningTime, out TimeOnly openingTime) ||
            !TryParseScheduleTime(schedule.ClosingTime, out TimeOnly closingTime))
        {
            throw new InvalidOperationException("The station operating schedule is invalid.");
        }

        TimeOnly slotStart = TimeOnly.FromDateTime(localStart);
        TimeOnly slotEnd = TimeOnly.FromDateTime(localEnd);
        if (slotStart < openingTime || slotEnd > closingTime)
        {
            throw new ConflictException("The slot must fit within the station operating schedule.");
        }
    }

    private static bool TryParseScheduleTime(string? value, out TimeOnly result)
    {
        return TimeOnly.TryParseExact(
            value,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }

    private static void ValidateCapacity(decimal capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Total capacity must be greater than zero.");
        }
    }

    private static string NormalizeObjectId(string? value, string parameterName)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (!ObjectId.TryParse(normalized, out ObjectId objectId))
        {
            throw new ArgumentException("A valid MongoDB ObjectId is required.", parameterName);
        }

        return objectId.ToString();
    }

    private static DateTime NormalizeRequiredUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind == DateTimeKind.Unspecified)
        {
            throw new ArgumentException("A UTC timestamp with an explicit offset is required.", parameterName);
        }

        return value.ToUniversalTime();
    }

    private static DateTime? NormalizeOptionalUtc(DateTime? value, string parameterName)
    {
        return value.HasValue ? NormalizeRequiredUtc(value.Value, parameterName) : null;
    }

    private static SlotResponseDto MapToResponse(EnergyBookingSlot slot)
    {
        return new SlotResponseDto
        {
            Id = slot.Id,
            StationId = slot.StationId,
            StartTimeUtc = slot.StartTimeUtc,
            EndTimeUtc = slot.EndTimeUtc,
            TotalCapacityKwh = slot.TotalCapacityKwh,
            AvailableCapacityKwh = slot.AvailableCapacityKwh,
            AvailabilityStatus = slot.AvailabilityStatus.ToString(),
            CreatedAtUtc = slot.CreatedAtUtc,
            UpdatedAtUtc = slot.UpdatedAtUtc
        };
    }
}
