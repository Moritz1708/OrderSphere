using Microsoft.Extensions.Compliance.Classification;

namespace OrderSphere.BuildingBlocks.Compliance;

/// <summary>
/// Data classifications for log parameters, mirroring the sensitivity tiers defined in
/// docs/data-classification.md. Applying one of the matching attributes to a
/// <c>[LoggerMessage]</c> parameter makes the redaction pipeline (wired in ServiceDefaults)
/// replace the value before it reaches any sink.
/// <para>
/// Redaction only applies to source-generated log methods whose parameters carry one of these
/// attributes. A plain <c>logger.LogInformation("... {Email}", email)</c> call is NOT redacted —
/// that is why PII must be logged through <c>[LoggerMessage]</c>. See docs/logging.md.
/// </para>
/// <para>
/// Tier T3 (financial) has deliberately no classification here. Payment amounts and PSP
/// references are not personal data, are needed verbatim for reconciliation, and would be
/// destroyed by the erasing fallback redactor if they were classified.
/// </para>
/// </summary>
public static class OrderSphereDataClassifications
{
    public const string TaxonomyName = "OrderSphere";

    /// <summary>T1 — directly identifies a natural person: email, name, postal address.</summary>
    public static DataClassification DirectPii => new(TaxonomyName, nameof(DirectPii));

    /// <summary>T2 — identifies a person only via a join: client IP, session id, Auth0 sub.</summary>
    public static DataClassification PseudonymousId => new(TaxonomyName, nameof(PseudonymousId));

    /// <summary>T4 — human-authored free text that may incidentally contain PII.</summary>
    public static DataClassification FreeText => new(TaxonomyName, nameof(FreeText));
}

/// <summary>Marks a log parameter as T1 direct PII. Redacted with a keyed hash.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class DirectPiiAttribute()
    : DataClassificationAttribute(OrderSphereDataClassifications.DirectPii);

/// <summary>Marks a log parameter as T2 pseudonymous identifier. Redacted with a keyed hash.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PseudonymousIdAttribute()
    : DataClassificationAttribute(OrderSphereDataClassifications.PseudonymousId);

/// <summary>Marks a log parameter as T4 free text with PII risk. Erased entirely.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field)]
public sealed class FreeTextAttribute()
    : DataClassificationAttribute(OrderSphereDataClassifications.FreeText);
