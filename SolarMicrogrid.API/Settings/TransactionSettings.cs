/*
 * TransactionSettings.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines bounded server-side lifetimes for opaque QR transaction
 *           tokens and their one-time verification receipts.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Settings;

public sealed class TransactionSettings
{
    public const string SectionName = "TransactionSettings";

    public int QrTokenLifetimeMinutes { get; set; } = 5;

    public int VerificationLifetimeMinutes { get; set; } = 5;
}
