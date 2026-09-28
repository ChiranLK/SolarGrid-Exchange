/*
 * IDashboardService.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines the Member 4 server-side dashboard and booking-history read
 *           contract used by the API controller and test infrastructure.
 * -----------------------------------------------------------------------------
 */

using SolarMicrogrid.API.Models.DTOs.Dashboard;

namespace SolarMicrogrid.API.Services;

public interface IDashboardService
{
    /// <summary>Builds one live role-scoped dashboard from authoritative MongoDB records.</summary>
    Task<DashboardResponseDto> GetDashboardAsync(
        string actorNic,
        string actorRoleClaim,
        DashboardQueryDto query,
        CancellationToken cancellationToken);

    /// <summary>Returns filtered, deterministically sorted history within the actor's data scope.</summary>
    Task<PagedBookingHistoryResponseDto> GetBookingHistoryAsync(
        string actorNic,
        string actorRoleClaim,
        BookingHistoryQueryDto query,
        CancellationToken cancellationToken);
}
