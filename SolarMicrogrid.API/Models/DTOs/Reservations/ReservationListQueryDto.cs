/*
 * ReservationListQueryDto.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines role-scoped reservation filters and the repository's standard
 *           paged query inputs; services enforce which filters each role may use.
 * -----------------------------------------------------------------------------
 */

using System.ComponentModel.DataAnnotations;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.API.Models.DTOs.Reservations;

public enum ReservationListView
{
    All,
    Pending,
    Current,
    ApprovedFuture,
    History
}

public sealed class ReservationListQueryDto
{
    [EnumDataType(typeof(ReservationListView))]
    public ReservationListView View { get; set; } = ReservationListView.All;

    [EnumDataType(typeof(ReservationStatus))]
    public ReservationStatus? Status { get; set; }

    [RegularExpression("^[a-fA-F0-9]{24}$", ErrorMessage = "StationId must be a valid MongoDB ObjectId.")]
    public string? StationId { get; set; }

    [RegularExpression(
        "^([0-9]{9}[VvXx]|[0-9]{12})$",
        ErrorMessage = "ProsumerNic must be 9 digits followed by V or X, or 12 digits.")]
    public string? ProsumerNic { get; set; }

    public DateTime? FromUtc { get; set; }

    public DateTime? ToUtc { get; set; }

    [MaxLength(100)]
    public string? Search { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}
