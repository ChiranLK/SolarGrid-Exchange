using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs.Slots;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;

namespace SolarMicrogrid.API.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class SlotsController : ControllerBase
{
    private readonly SlotService _slotService;
    private readonly StationAccessService _stationAccessService;

    public SlotsController(
        SlotService slotService,
        StationAccessService stationAccessService)
    {
        _slotService = slotService;
        _stationAccessService = stationAccessService;
    }

    [HttpGet("stations/{stationId}/slots")]
    public async Task<ActionResult<PagedSlotResponseDto>> GetSlotsByStation(
        string stationId,
        [FromQuery] SlotListQueryDto query,
        CancellationToken cancellationToken)
    {
        PagedSlotResponseDto result = await _slotService.GetSlotsByStationAsync(
            stationId,
            query,
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("stations/{stationId}/slots/available")]
    public async Task<ActionResult<PagedSlotResponseDto>> GetAvailableSlotsByStation(
        string stationId,
        [FromQuery] SlotListQueryDto query,
        CancellationToken cancellationToken)
    {
        PagedSlotResponseDto result = await _slotService.GetAvailableSlotsByStationAsync(
            stationId,
            query,
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("slots/{slotId}")]
    public async Task<ActionResult<SlotResponseDto>> GetSlotById(
        string slotId,
        CancellationToken cancellationToken)
    {
        SlotResponseDto? slot = await _slotService.GetSlotByIdAsync(slotId, cancellationToken);
        return slot is null ? NotFound() : Ok(slot);
    }

    [HttpPost("stations/{stationId}/slots")]
    [Authorize(Roles = nameof(UserRole.Backoffice))]
    public async Task<ActionResult<SlotResponseDto>> CreateSlot(
        string stationId,
        [FromBody] CreateSlotRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.StationId) &&
            !string.Equals(request.StationId.Trim(), stationId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                nameof(request.StationId),
                "StationId in the request body must match the route StationId.");
            return ValidationProblem(ModelState);
        }

        request.StationId = stationId;
        SlotResponseDto slot = await _slotService.CreateSlotAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetSlotById), new { slotId = slot.Id }, slot);
    }

    [HttpPut("slots/{slotId}")]
    [Authorize(Roles = nameof(UserRole.Backoffice))]
    public async Task<ActionResult<SlotResponseDto>> UpdateSlot(
        string slotId,
        [FromBody] UpdateSlotRequestDto request,
        CancellationToken cancellationToken)
    {
        SlotResponseDto? slot = await _slotService.UpdateSlotAsync(slotId, request, cancellationToken);
        return slot is null ? NotFound() : Ok(slot);
    }

    [HttpPatch("slots/{slotId}/availability")]
    [Authorize(Roles = $"{nameof(UserRole.Backoffice)},{nameof(UserRole.GridOperator)}")]
    public async Task<ActionResult<SlotResponseDto>> ChangeSlotAvailability(
        string slotId,
        [FromBody] ChangeSlotAvailabilityRequestDto request,
        CancellationToken cancellationToken)
    {
        string? actorNic = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorNic))
        {
            throw new UnauthorizedException(
                "The access token is missing the required user identifier claim.");
        }

        SlotResponseDto slot = await _stationAccessService.ChangeSlotAvailabilityAsync(
            actorNic,
            slotId,
            request,
            cancellationToken);
        return Ok(slot);
    }
}
