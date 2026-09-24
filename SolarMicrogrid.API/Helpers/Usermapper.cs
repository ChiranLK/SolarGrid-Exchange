using SolarMicrogrid.API.Models.Entities;
using SolarMicrogrid.API.Models.DTOs;

namespace SolarMicrogrid.API.Helpers
{
    
    public static class UserMapper
    {
        public static UserResponseDto ToUserResponse(User user)
        {
            return new UserResponseDto
            {
                Nic = user.Nic,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                Address = user.Address,
                Role = user.Role.ToString(),
                Status = user.Status.ToString(),
                CreatedAtUtc = user.CreatedAtUtc
            };
        }
    }
}