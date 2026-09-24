/*
 * EligibleProsumerResponseDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Returns only the identity fields staff need to select an eligible
 *           active Prosumer for reservation creation.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Models.DTOs.Users;

public sealed class EligibleProsumerResponseDto
{
    public string Nic { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
