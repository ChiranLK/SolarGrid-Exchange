/*
 * EligibleProsumerListQueryDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Bounds staff-only Prosumer lookup so reservation creation searches
 *           the user collection on the server instead of downloading all users.
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Users;

public sealed class EligibleProsumerListQueryDto
{
    [Required]
    [MinLength(2)]
    [MaxLength(100)]
    public string Search { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 20)]
    public int PageSize { get; set; } = 10;
}
