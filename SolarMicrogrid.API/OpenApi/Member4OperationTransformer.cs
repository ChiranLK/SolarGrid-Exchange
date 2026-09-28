/*
 * Member4OperationTransformer.cs
 * -----------------------------------------------------------------------------
 * Purpose : Documents bearer authorization, exact roles, and safe examples for
 *           all Member 4 dashboard and QR transaction operations.
 * Security: Examples are explicit placeholders and never resemble a live JWT,
 *           raw QR value, verification receipt, NIC, or infrastructure secret.
 * -----------------------------------------------------------------------------
 */

using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SolarMicrogrid.API.OpenApi;

public sealed class Member4OperationTransformer : IOpenApiOperationTransformer
{
    private static readonly IReadOnlyDictionary<string, OperationDocumentation> Documentation =
        new Dictionary<string, OperationDocumentation>(StringComparer.OrdinalIgnoreCase)
        {
            ["GET api/dashboard"] = new(
                "Read the live role-scoped dashboard",
                "Roles: Prosumer, GridOperator, Backoffice. Counts and items are server-authoritative.",
                null,
                """
                {
                  "serverNowUtc": "2030-01-15T10:00:00Z",
                  "role": "GridOperator",
                  "scope": "AssignedStation",
                  "stationId": "000000000000000000000001",
                  "statusSummary": {
                    "pendingTotal": 0,
                    "approvedTotal": 0,
                    "rejectedTotal": 0,
                    "cancelledTotal": 0,
                    "completedTotal": 0,
                    "currentCount": 0,
                    "pendingCount": 0,
                    "approvedFutureCount": 0,
                    "historyCount": 0
                  },
                  "currentReservations": [],
                  "pendingReservations": [],
                  "recentHistory": [],
                  "recentTransfers": [],
                  "activeTransfers": [],
                  "completedTransfers": []
                }
                """),
            ["GET api/dashboard/history"] = new(
                "Search role-scoped booking history",
                "Roles: Prosumer, GridOperator, Backoffice. Filters are applied after persisted actor scope.",
                null,
                """
                {
                  "serverNowUtc": "2030-01-15T10:00:00Z",
                  "items": [],
                  "totalCount": 0,
                  "page": 1,
                  "pageSize": 20,
                  "totalPages": 0
                }
                """),
            ["POST api/transactions/reservations/{reservationId}/qr"] = new(
                "Issue a short-lived QR transaction token",
                "Role: Prosumer. The returned opaque value is secret and is shown only to the scanner.",
                null,
                """
                {
                  "reservationId": "000000000000000000000001",
                  "reservationVersion": 2,
                  "qrToken": "<redacted-opaque-qr-token>",
                  "issuedAtUtc": "2030-01-15T10:00:00Z",
                  "expiresAtUtc": "2030-01-15T10:05:00Z"
                }
                """),
            ["POST api/transactions/verify"] = new(
                "Verify a scanned QR transaction token",
                "Role: GridOperator. Authorization and assigned station are resolved from persisted identity.",
                """
                { "token": "<redacted-opaque-qr-token>" }
                """,
                """
                {
                  "verificationId": "<redacted-one-time-receipt>",
                  "reservationId": "000000000000000000000001",
                  "reservationReference": "RES-00000001",
                  "prosumerReference": "PRO-00000001",
                  "reservationVersion": 2,
                  "stationId": "000000000000000000000002",
                  "stationName": "Example Solar Hub",
                  "scheduledStartTimeUtc": "2030-01-15T10:00:00Z",
                  "scheduledEndTimeUtc": "2030-01-15T11:00:00Z",
                  "requestedEnergyKwh": 5,
                  "status": "Approved",
                  "verifiedAtUtc": "2030-01-15T10:01:00Z",
                  "expiresAtUtc": "2030-01-15T10:06:00Z"
                }
                """),
            ["POST api/transactions/reservations/{reservationId}/complete"] = new(
                "Complete a verified energy transfer",
                "Role: GridOperator. Requires the one-time receipt and expected reservation version.",
                """
                {
                  "verificationId": "<redacted-one-time-receipt>",
                  "expectedVersion": 2
                }
                """,
                """
                {
                  "reservationId": "000000000000000000000001",
                  "reservationReference": "RES-00000001",
                  "stationId": "000000000000000000000002",
                  "status": "Completed",
                  "version": 3,
                  "completedAtUtc": "2030-01-15T10:02:00Z"
                }
                """)
        };

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        // Attach bearer security to every authorized controller operation in the generated contract.
        IReadOnlyList<AuthorizeAttribute> authorization = context.Description.ActionDescriptor
            .EndpointMetadata
            .OfType<AuthorizeAttribute>()
            .ToArray();
        bool isAnonymous = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAllowAnonymous>()
            .Any();

        if (authorization.Count > 0 && !isAnonymous)
        {
            operation.Security ??= [];
            var bearerReference = new OpenApiSecuritySchemeReference(
                "Bearer",
                context.Document,
                null);
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [bearerReference] = []
            });
        }

        string key = $"{context.Description.HttpMethod} {context.Description.RelativePath}";
        if (!Documentation.TryGetValue(key, out OperationDocumentation? documentation))
        {
            return Task.CompletedTask;
        }

        operation.Summary = documentation.Summary;
        operation.Description = documentation.Description;
        SetJsonExample(operation.RequestBody?.Content, documentation.RequestExample);

        if (operation.Responses is not null &&
            operation.Responses.TryGetValue("200", out IOpenApiResponse? successResponse))
        {
            SetJsonExample(successResponse.Content, documentation.ResponseExample);
        }

        return Task.CompletedTask;
    }

    private static void SetJsonExample(
        IDictionary<string, OpenApiMediaType>? content,
        string? example)
    {
        // Apply only predefined, non-secret JSON to the concrete media type generated by ASP.NET.
        if (example is null || content is null ||
            !content.TryGetValue("application/json", out OpenApiMediaType? mediaType))
        {
            return;
        }

        mediaType.Example = JsonNode.Parse(example);
    }

    private sealed record OperationDocumentation(
        string Summary,
        string Description,
        string? RequestExample,
        string ResponseExample);
}
