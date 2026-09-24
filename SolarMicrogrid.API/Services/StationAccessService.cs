using MongoDB.Driver;
using SolarMicrogrid.API.Data;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Slots;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Services;

public sealed class StationAccessService
{
    private readonly MongoDbContext _context;
    private readonly SlotService _slotService;

    public StationAccessService(MongoDbContext context, SlotService slotService)
    {
        _context = context;
        _slotService = slotService;
    }

    public async Task<SlotResponseDto> ChangeSlotAvailabilityAsync(
        string actorNic,
        string slotId,
        ChangeSlotAvailabilityRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string normalizedNic = actorNic?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalizedNic.Length == 0)
        {
            throw new UnauthorizedException(
                "The access token is missing the required user identifier claim.");
        }

        User? actor = await _context.Users
            .Find(user => user.Nic == normalizedNic)
            .FirstOrDefaultAsync(cancellationToken);

        if (actor is null)
        {
            throw new UnauthorizedException("The authenticated user no longer exists.");
        }

        if (actor.Status != UserStatus.Active)
        {
            throw new ForbiddenException(
                "Only active users may manage battery-slot availability.");
        }

        if (actor.Role is not UserRole.Backoffice and not UserRole.GridOperator)
        {
            throw new ForbiddenException(
                "This user role cannot manage battery-slot availability.");
        }

        SlotResponseDto? slot = await _slotService.GetSlotByIdAsync(slotId, cancellationToken);
        if (slot is null)
        {
            throw new NotFoundException("The specified slot does not exist.");
        }

        if (actor.Role == UserRole.GridOperator &&
            (string.IsNullOrWhiteSpace(actor.AssignedStationId) ||
             !string.Equals(actor.AssignedStationId, slot.StationId, StringComparison.Ordinal)))
        {
            throw new ForbiddenException(
                "Grid Operators may manage slot availability only for their assigned station.");
        }

        SlotResponseDto? updatedSlot = await _slotService.ChangeSlotAvailabilityAsync(
            slotId,
            request,
            cancellationToken);

        return updatedSlot ?? throw new NotFoundException("The specified slot does not exist.");
    }
}
