/*
 * AuthController.cs
 * -----------------------------------------------------------------------------
 * File        : AuthController.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Exposes Prosumer registration, login and the current-session
 *               check as REST endpoints. Stays thin: each action only receives
 *               the request, calls AuthService, and returns the result. No
 *               business logic here.
 * Depends     : AuthService, injected through the constructor.
 * Errors      : No try/catch. Exceptions thrown by AuthService (409, 401, 403)
 *               and by [RequireActiveAccount] are turned into responses by
 *               ExceptionMiddleware.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolarMicrogrid.API.Filters;
using SolarMicrogrid.API.Models.DTOs;
using SolarMicrogrid.API.Services;

namespace SolarMicrogrid.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;

        // Receives AuthService through dependency injection.
        public AuthController(AuthService authService)
        {
            // Execute AuthController with validated inputs and the authoritative application state.
            _authService = authService;
        }

        // POST api/auth/register
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<ActionResult<UserResponseDto>> Register([FromBody] RegisterRequestDto request)
        {
            // Create a new Prosumer in PendingActivation; Backoffice must activate it before login.
            UserResponseDto createdUser = await _authService.RegisterAsync(request);


            return CreatedAtAction(nameof(Register), new { nic = createdUser.Nic }, createdUser);
        }

        // POST api/auth/login
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
        {
            // Verify credentials and account status, then return a signed JWT.
            LoginResponseDto response = await _authService.LoginAsync(request);
            return Ok(response);
        }

        // GET api/auth/me
        // Used by the web and Android clients to validate a stored session on start-up.
        // [RequireActiveAccount] makes a pending/deactivated account fail here too.
        [HttpGet("me")]
        [Authorize]
        [RequireActiveAccount]
        public ActionResult<object> Me()
        {
            // Echo the identity from the validated token (response shape unchanged for clients).
            string? nic = User.FindFirstValue(ClaimTypes.NameIdentifier);
            string? email = User.FindFirstValue(ClaimTypes.Email);
            string? role = User.FindFirstValue(ClaimTypes.Role);

            return Ok(new { nic, email, role });
        }
    }
}
