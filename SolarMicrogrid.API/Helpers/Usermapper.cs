/*
 * Usermapper.cs
 * -----------------------------------------------------------------------------
 * File        : Usermapper.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Converts User documents into the safe UserResponseDto used by
 *               the Backoffice user-management endpoints.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Models.DTOs;

namespace SolarMicrogrid.API.Helpers
{

    public static class UserMapper
    {
        public static UserResponseDto ToUserResponse(User user)
        {
            // Copy only public account fields; the password hash is deliberately left out.
            return new UserResponseDto
            {
                Nic = user.Nic,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                Address = user.Address,
                Role = user.Role.ToString(),
                Status = user.Status.ToString(),
                AssignedStationId = user.AssignedStationId,
                DeactivationRequested = user.DeactivationRequested,
                CreatedAtUtc = user.CreatedAtUtc
            };
        }
    }
}
