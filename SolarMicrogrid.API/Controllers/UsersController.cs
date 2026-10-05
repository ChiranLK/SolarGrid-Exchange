/*
 * UsersController.cs
 * -----------------------------------------------------------------------------
 * File        : UsersController.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Backoffice-facing REST endpoints for managing users: creating
 *               staff accounts (Backoffice / Grid Operator), listing users,
 *               listing pending activations and deactivation requests,
 *               activating/reactivating and deactivating accounts, and
 *               assigning a Grid Operator to a station. Stays thin -- each
 *               action only receives the request, calls UserService, and
 *               returns the result. No business logic here.
 * Security    : Member 1 actions carry [RequireActiveAccount], so a caller
 *               whose account is no longer Active is rejected even with an
 *               unexpired token. The eligible-prosumers search (Member 3) now uses
 *               the same check so a deactivated staff user cannot list Prosumers.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Filters;
using SolarMicrogrid.API.Services;
using SolarMicrogrid.API.Models.DTOs;
using SolarMicrogrid.API.Models.DTOs.Users;

namespace SolarMicrogrid.API.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly UserService _userService;

        // Receives UserService through dependency injection.
        public UsersController(UserService userService)
        {
            // Execute UsersController with validated inputs and the authoritative application state.
            _userService = userService;
        }

        // POST api/users
        // Creates a Backoffice or Grid Operator account. Only Backoffice may call this;
        // Prosumer accounts are created through registration instead (AuthController).
        [HttpPost]
        [Authorize(Roles = "Backoffice")]
        [RequireActiveAccount]
        public async Task<ActionResult<UserResponseDto>> CreateStaffUser([FromBody] CreateStaffUserDto dto)
        {
            // Delegate validation, duplicate checks and optional station assignment to UserService.
            UserResponseDto result = await _userService.CreateStaffUserAsync(dto);
            return StatusCode(201, result);
        }

        // GET api/users?role=&status=
        // Lists users, optionally filtered by role and/or status. Open to both
        // Backoffice and Grid Operator, since Grid Operators need to see the
        // Prosumers tied to their station.
        [HttpGet]
        [Authorize(Roles = "Backoffice,GridOperator")]
        [RequireActiveAccount]
        public async Task<ActionResult<List<UserResponseDto>>> GetAll([FromQuery] string? role, [FromQuery] string? status)
        {
            // Return safe user DTOs matching the optional role/status filters.
            List<UserResponseDto> result = await _userService.GetAllAsync(role, status);
            return Ok(result);
        }

        // GET api/users/{nic}
        // Loads one account for the Backoffice edit screen without exposing its password hash.
        [HttpGet("{nic}")]
        [Authorize(Roles = "Backoffice")]
        [RequireActiveAccount]
        public async Task<ActionResult<UserResponseDto>> GetByNic(
            string nic,
            CancellationToken cancellationToken)
        {
            // Resolve the canonical NIC and return the safe account projection.
            UserResponseDto result = await _userService.GetByNicAsync(nic, cancellationToken);
            return Ok(result);
        }

        // PUT api/users/{nic}
        // Updates contact/profile fields while keeping NIC, role, status and password immutable.
        [HttpPut("{nic}")]
        [Authorize(Roles = "Backoffice")]
        [RequireActiveAccount]
        public async Task<ActionResult<UserResponseDto>> UpdateDetails(
            string nic,
            [FromBody] UpdateProfileDto request,
            CancellationToken cancellationToken)
        {
            // Apply the validated Backoffice edit through the service-layer uniqueness checks.
            UserResponseDto result = await _userService.UpdateDetailsAsync(nic, request, cancellationToken);
            return Ok(result);
        }

        [HttpGet("eligible-prosumers")]
        [Authorize(Roles = "Backoffice,GridOperator")]
        [RequireActiveAccount]
        public async Task<ActionResult<PagedEligibleProsumerResponseDto>> SearchEligibleProsumers(
            [FromQuery] EligibleProsumerListQueryDto query,
            CancellationToken cancellationToken)
        {
            // Search only active Prosumer identities through a bounded server-side query.
            PagedEligibleProsumerResponseDto result =
                await _userService.SearchEligibleProsumersAsync(query, cancellationToken);
            return Ok(result);
        }

        // GET api/users/pending
        // Shortcut for the Backoffice "pending activations" view: users with
        // Status = PendingActivation. Backoffice only.
        [HttpGet("pending")]
        [Authorize(Roles = "Backoffice")]
        [RequireActiveAccount]
        public async Task<ActionResult<List<UserResponseDto>>> GetPending()
        {
            // Return every account still awaiting Backoffice approval.
            List<UserResponseDto> result = await _userService.GetPendingActivationsAsync();
            return Ok(result);
        }

        // GET api/users/deactivation-requests
        // Prosumer deactivation requests awaiting review, oldest first. Backoffice only.
        [HttpGet("deactivation-requests")]
        [Authorize(Roles = "Backoffice")]
        [RequireActiveAccount]
        public async Task<ActionResult<List<DeactivationRequestResponseDto>>> GetDeactivationRequests(
            CancellationToken cancellationToken)
        {
            // Return pending deactivation requests as safe DTOs (empty list when none).
            List<DeactivationRequestResponseDto> result =
                await _userService.GetDeactivationRequestsAsync(cancellationToken);
            return Ok(result);
        }

        // PATCH api/users/{nic}/activate
        // Activates a user by NIC: approves a pending prosumer, or reactivates a
        // deactivated account. Backoffice only -- this attribute is what enforces
        // that rule; UserService.ActivateAsync itself does not check the caller.
        [HttpPatch("{nic}/activate")]
        [Authorize(Roles = "Backoffice")]
        [RequireActiveAccount]
        public async Task<ActionResult<UserResponseDto>> Activate(string nic, CancellationToken cancellationToken)
        {
            // Apply PendingActivation/Deactivated -> Active; any other state returns 409.
            UserResponseDto result = await _userService.ActivateAsync(nic, cancellationToken);
            return Ok(result);
        }

        // PATCH api/users/{nic}/deactivate
        // Deactivates an Active account and finalises any pending deactivation request.
        // Backoffice only; the caller's own NIC is passed so self-deactivation is refused.
        [HttpPatch("{nic}/deactivate")]
        [Authorize(Roles = "Backoffice")]
        [RequireActiveAccount]
        public async Task<ActionResult<UserResponseDto>> Deactivate(string nic, CancellationToken cancellationToken)
        {
            // Apply Active -> Deactivated using the signed-in Backoffice user's NIC from the JWT.
            UserResponseDto result = await _userService.DeactivateAsync(nic, GetCallerNic(), cancellationToken);
            return Ok(result);
        }

        // PATCH api/users/{nic}/station
        // Assigns a Grid Operator to an existing active station. Backoffice only.
        [HttpPatch("{nic}/station")]
        [Authorize(Roles = "Backoffice")]
        [RequireActiveAccount]
        public async Task<ActionResult<UserResponseDto>> AssignStation(
            string nic,
            [FromBody] AssignStationRequestDto request,
            CancellationToken cancellationToken)
        {
            // Validate the station and store it on the Grid Operator account.
            UserResponseDto result = await _userService.AssignStationAsync(nic, request.StationId, cancellationToken);
            return Ok(result);
        }

        private string GetCallerNic()
        {
            // The NIC claim is written by JwtHelper at login; without it the token is unusable.
            string? nic = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(nic))
            {
                throw new UnauthorizedException("Your session is invalid. Please sign in again.");
            }

            return nic;
        }
    }
}
