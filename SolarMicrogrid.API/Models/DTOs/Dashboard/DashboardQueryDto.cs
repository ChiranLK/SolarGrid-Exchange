/*
 * DashboardQueryDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Bounds the number of live reservation summaries returned in each
 *           role-specific dashboard section.
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;

namespace SolarMicrogrid.API.Models.DTOs.Dashboard;

public sealed class DashboardQueryDto
{
    [Range(1, 20)]
    public int RecentLimit { get; set; } = 5;
}
