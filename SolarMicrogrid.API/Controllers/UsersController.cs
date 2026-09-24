/*
 * UsersController.cs
 * -----------------------------------------------------------------------------
 * File        : UsersController.cs
 * Author      : [Your Name] - [Your IT Number]
 * Description : Backoffice-facing REST endpoints for managing users: creating
 *               staff accounts (Backoffice / Grid Operator), listing users,
 *               listing pending activations, and activating a user. Stays
 *               thin -- each action only receives the request, calls
 *               UserService, and returns the result. No business logic here.
 * Date        : 2026-09-24
 * -----------------------------------------------------------------------------
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            _userService = userService;
        }

        // POST api/users
        // Creates a Backoffice or Grid Operator account. Only Backoffice may call this;
        // Prosumer accounts are created through registration instead (AuthController).
        [HttpPost]
        [Authorize(Roles = "Backoffice")]
        public async Task<ActionResult<UserResponseDto>> CreateStaffUser([FromBody] CreateStaffUserDto dto)
        {
            UserResponseDto result = await _userService.CreateStaffUserAsync(dto);
            return StatusCode(201, result);
        }

        // GET api/users?role=&status=
        // Lists users, optionally filtered by role and/or status. Open to both
        // Backoffice and Grid Operator, since Grid Operators need to see the
        // Prosumers tied to their station.
        [HttpGet]
        [Authorize(Roles = "Backoffice,GridOperator")]
        public async Task<ActionResult<List<UserResponseDto>>> GetAll([FromQuery] string? role, [FromQuery] string? status)
        {
            List<UserResponseDto> result = await _userService.GetAllAsync(role, status);
            return Ok(result);
        }

        // GET api/users/pending
        // Shortcut for the Backoffice "pending activations" view: users with
        // Status = PendingActivation. Backoffice only.
        [HttpGet("pending")]
        [Authorize(Roles = "Backoffice")]
        public async Task<ActionResult<List<UserResponseDto>>> GetPending()
        {
            List<UserResponseDto> result = await _userService.GetPendingActivationsAsync();
            return Ok(result);
        }

        // PATCH api/users/{nic}/activate
        // Activates a user by NIC: approves a pending prosumer, or reactivates a
        // deactivated account. Backoffice only -- this attribute is what enforces
        // that rule; UserService.ActivateAsync itself does not check the caller.
        [HttpPatch("{nic}/activate")]
        [Authorize(Roles = "Backoffice")]
        public async Task<ActionResult<UserResponseDto>> Activate(string nic)
        {
            UserResponseDto result = await _userService.ActivateAsync(nic);
            return Ok(result);
        }
    }
}