/*
 * ProsumersController.cs
 * -----------------------------------------------------------------------------
 * File        : ProsumersController.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Self-service REST endpoints for a signed-in Prosumer: view
 *               profile, update allowed profile fields, and request account
 *               deactivation. The NIC is always read from the caller's JWT and
 *               never from the route or body, so a Prosumer can only reach
 *               their own account. Stays thin: no business logic here.
 * Routes      : GET  api/prosumers/me
 *               PUT  api/prosumers/me
 *               POST api/prosumers/me/deactivation-request
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolarMicrogrid.API.Exceptions;
using SolarMicrogrid.API.Models.DTOs;
using SolarMicrogrid.API.Models.DTOs.Prosumers;
using SolarMicrogrid.API.Services;

namespace SolarMicrogrid.API.Controllers
{
    [ApiController]
    [Route("api/prosumers")]
    [Authorize(Roles = "Prosumer")]
    public class ProsumersController : ControllerBase
    {
        private readonly ProsumerService _prosumerService;

        // Receives ProsumerService through dependency injection.
        public ProsumersController(ProsumerService prosumerService)
        {
            _prosumerService = prosumerService;
        }

        // GET api/prosumers/me
        [HttpGet("me")]
        public async Task<ActionResult<ProsumerProfileResponseDto>> GetMyProfile(CancellationToken cancellationToken)
        {
            // Return the signed-in Prosumer's own profile.
            ProsumerProfileResponseDto result =
                await _prosumerService.GetProfileAsync(GetCallerNic(), cancellationToken);
            return Ok(result);
        }

        // PUT api/prosumers/me
        [HttpPut("me")]
        public async Task<ActionResult<ProsumerProfileResponseDto>> UpdateMyProfile(
            [FromBody] UpdateProfileDto request,
            CancellationToken cancellationToken)
        {
            // Update name, email, phone and address for the signed-in Prosumer only.
            ProsumerProfileResponseDto result =
                await _prosumerService.UpdateProfileAsync(GetCallerNic(), request, cancellationToken);
            return Ok(result);
        }

        // POST api/prosumers/me/deactivation-request
        [HttpPost("me/deactivation-request")]
        public async Task<ActionResult<ProsumerProfileResponseDto>> RequestDeactivation(
            CancellationToken cancellationToken)
        {
            // Record a deactivation request for Backoffice review; the account stays active until then.
            ProsumerProfileResponseDto result =
                await _prosumerService.RequestDeactivationAsync(GetCallerNic(), cancellationToken);
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
