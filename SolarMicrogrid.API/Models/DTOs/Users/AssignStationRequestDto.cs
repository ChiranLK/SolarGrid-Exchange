/*
 * AssignStationRequestDto.cs
 * -----------------------------------------------------------------------------
 * File        : AssignStationRequestDto.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Request body for assigning a Grid Operator to an existing
 *               Member 2 station. The ID is validated against the stations
 *               collection by UserService.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Users
{
    public class AssignStationRequestDto
    {
        [Required(ErrorMessage = "Station ID is required.")]
        [MaxLength(24, ErrorMessage = "Station ID cannot be longer than 24 characters.")]
        public string StationId { get; set; } = string.Empty;
    }
}
