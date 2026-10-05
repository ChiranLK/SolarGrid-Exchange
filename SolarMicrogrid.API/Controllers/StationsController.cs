/*
 * StationsController.cs
 * -----------------------------------------------------------------------------
 * Purpose : Exposes authenticated station, schedule, activation and nearby-search endpoints.
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolarMicrogrid.API.Models.DTOs.Stations;
using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Filters;
using SolarMicrogrid.API.Services;

namespace SolarMicrogrid.API.Controllers;

[ApiController]
[Authorize]
[RequireActiveAccount]
[Route("api/stations")]
public sealed class StationsController : ControllerBase
{
    private readonly StationService _stationService;

    public StationsController(StationService stationService)
    {
        // Execute StationsController with validated inputs and the authoritative application state.
        _stationService = stationService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedStationResponseDto>> GetStations(
        [FromQuery] StationListQueryDto query,
        CancellationToken cancellationToken)
    {
        // Execute GetStations with validated inputs and the authoritative application state.
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
        // Execute Range with validated inputs and the authoritative application state.
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
        // Execute GetStationById with validated inputs and the authoritative application state.
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
        // Execute CreateStation with validated inputs and the authoritative application state.
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
        // Execute UpdateStation with validated inputs and the authoritative application state.
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
        // Execute ActivateStation with validated inputs and the authoritative application state.
        StationResponseDto? station = await _stationService.ActivateStationAsync(stationId, cancellationToken);
        return station is null ? NotFound() : Ok(station);
    }

    [HttpPatch("{stationId}/deactivate")]
    [Authorize(Roles = nameof(UserRole.Backoffice))]
    public async Task<ActionResult<StationResponseDto>> DeactivateStation(
        string stationId,
        CancellationToken cancellationToken)
    {
        // Execute DeactivateStation with validated inputs and the authoritative application state.
        StationResponseDto? station = await _stationService.DeactivateStationAsync(stationId, cancellationToken);
        return station is null ? NotFound() : Ok(station);
    }

    [HttpDelete("{stationId}")]
    [Authorize(Roles = nameof(UserRole.Backoffice))]
    public async Task<IActionResult> DeleteStation(
        string stationId,
        CancellationToken cancellationToken)
    {
        // Execute DeleteStation with validated inputs and the authoritative application state.
        StationResponseDto? station = await _stationService.DeactivateStationAsync(
            stationId,
            cancellationToken);
        return station is null ? NotFound() : NoContent();
    }
}
