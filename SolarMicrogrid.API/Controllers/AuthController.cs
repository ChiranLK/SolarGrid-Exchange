/*
 * AuthController.cs
 * -----------------------------------------------------------------------------
 * Purpose : Exposes registration and login as REST endpoints. Stays thin: each
 *           action only receives the request, calls AuthService, and returns
 *           the result. No business logic here.
 * Depends : AuthService, injected through the constructor.
 * Errors  : No try/catch. Exceptions thrown by AuthService (409, 401, 403) are
 *           turned into responses by ExceptionMiddleware.
 * -----------------------------------------------------------------------------
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SolarMicrogrid.API.Models.DTOs;
using SolarMicrogrid.API.Services;

namespace SolarMicrogrid.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }

        
        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<ActionResult<UserResponseDto>> Register([FromBody] RegisterRequestDto request)
        {
            UserResponseDto createdUser = await _authService.RegisterAsync(request);

            
            return CreatedAtAction(nameof(Register), new { nic = createdUser.Nic }, createdUser);
        }

        
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
        {
            LoginResponseDto response = await _authService.LoginAsync(request);
            return Ok(response);
        }

       
        [HttpGet("me")]
        [Authorize]
        public ActionResult<object> Me()
        {
            string? nic = User.FindFirstValue(ClaimTypes.NameIdentifier);
            string? email = User.FindFirstValue(ClaimTypes.Email);
            string? role = User.FindFirstValue(ClaimTypes.Role);

            return Ok(new { nic, email, role });
        }
    }
}