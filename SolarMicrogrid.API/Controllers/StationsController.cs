using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolarMicrogrid.API.Models.DTOs.Stations;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Services;

namespace SolarMicrogrid.API.Controllers;

[ApiController]
[Authorize]
[Route("api/stations")]
public sealed class StationsController : ControllerBase
{
    private readonly StationService _stationService;

    public StationsController(StationService stationService)
    {
        _stationService = stationService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedStationResponseDto>> GetStations(
        [FromQuery] StationListQueryDto query,
        CancellationToken cancellationToken)
    {
        PagedStationResponseDto result = await _stationService.GetStationsAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("nearby")]
    public async Task<ActionResult<IReadOnlyList<NearbyStationResponseDto>>> GetNearbyStations(
        [FromQuery, Range(-90d, 90d)] double latitude,
        [FromQuery, Range(-180d, 180d)] double longitude,
        [FromQuery, Range(0.01d, 1000d)] double radiusKm = 25,
        [FromQuery, Range(1, 100)] int maximumResults = 20,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<NearbyStationResponseDto> result =
            await _stationService.GetNearbyStationsAsync(
                latitude,
                longitude,
                radiusKm,
                maximumResults,
                cancellationToken);

        return Ok(result);
    }

    [HttpGet("{stationId}")]
    public async Task<ActionResult<StationResponseDto>> GetStationById(
        string stationId,
        CancellationToken cancellationToken)
    {
        StationResponseDto? station = await _stationService.GetStationByIdAsync(
            stationId,
            cancellationToken);

        return station is null ? NotFound() : Ok(station);
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Backoffice))]
    public async Task<ActionResult<StationResponseDto>> CreateStation(
        [FromBody] CreateStationRequestDto request,
        CancellationToken cancellationToken)
    {
        StationResponseDto station = await _stationService.CreateStationAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetStationById), new { stationId = station.Id }, station);
    }

    [HttpPut("{stationId}")]
    [Authorize(Roles = nameof(UserRole.Backoffice))]
    public async Task<ActionResult<StationResponseDto>> UpdateStation(
        string stationId,
        [FromBody] UpdateStationRequestDto request,
        CancellationToken cancellationToken)
    {
        StationResponseDto? station = await _stationService.UpdateStationAsync(
            stationId,
            request,
            cancellationToken);

        return station is null ? NotFound() : Ok(station);
    }

    [HttpPatch("{stationId}/activate")]
    [Authorize(Roles = nameof(UserRole.Backoffice))]
    public async Task<ActionResult<StationResponseDto>> ActivateStation(
        string stationId,
        CancellationToken cancellationToken)
    {
        StationResponseDto? station = await _stationService.ActivateStationAsync(stationId, cancellationToken);
        return station is null ? NotFound() : Ok(station);
    }

    [HttpPatch("{stationId}/deactivate")]
    [Authorize(Roles = nameof(UserRole.Backoffice))]
    public async Task<ActionResult<StationResponseDto>> DeactivateStation(
        string stationId,
        CancellationToken cancellationToken)
    {
        StationResponseDto? station = await _stationService.DeactivateStationAsync(stationId, cancellationToken);
        return station is null ? NotFound() : Ok(station);
    }

    [HttpDelete("{stationId}")]
    [Authorize(Roles = nameof(UserRole.Backoffice))]
    public async Task<IActionResult> DeleteStation(
        string stationId,
        CancellationToken cancellationToken)
    {
        StationResponseDto? station = await _stationService.DeactivateStationAsync(
            stationId,
            cancellationToken);
        return station is null ? NotFound() : NoContent();
    }
}
