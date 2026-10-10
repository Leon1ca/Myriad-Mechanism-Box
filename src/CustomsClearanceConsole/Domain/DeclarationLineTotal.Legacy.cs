using System.Text.Json.Serialization;

namespace CustomsClearanceConsole;

// Read-only compatibility semantics for previously saved dual-engine results.
public sealed partial class DeclarationLineTotal
{
    public decimal? VerificationAmount { get; set; }
    public string VerificationProductName { get; set; } = "";
    public decimal? VerificationQuantity { get; set; }
    public string VerificationUnit { get; set; } = "";
    public decimal? VerificationUnitPrice { get; set; }

    /// <summary>
    /// Descriptor fields that were copied from the verification engine because the primary
    /// engine left them empty. Such a field was never independently read by two engines and
    /// therefore must not count as fully verified. Serialized so provenance survives reload;
    /// absent in old JSON, where it defaults to empty.
    /// </summary>
    public HashSet<string> BackfilledFields { get; set; } = new();

    /// <summary>True when the row amount itself came only from the verification engine.</summary>
    public bool AmountOnlyFromSecondary { get; set; }

    [JsonIgnore]
    public bool HasSecondaryDifference =>
        !string.IsNullOrWhiteSpace(VerificationProductName) && !string.Equals(VerificationProductName, ProductName, StringComparison.Ordinal) ||
        VerificationQuantity is not null && VerificationQuantity != Quantity ||
        !string.IsNullOrWhiteSpace(VerificationUnit) && !string.Equals(VerificationUnit, Unit, StringComparison.Ordinal) ||
        VerificationUnitPrice is not null && VerificationUnitPrice != UnitPrice;

    /// <summary>The verification engine produced an amount and it differs from the primary one.</summary>
    [JsonIgnore]
    public bool HasAmountDifference => VerificationAmount is not null && VerificationAmount.Value != Amount;

    /// <summary>Any primary/secondary disagreement, amount included. Feeds the overall verdict.</summary>
    [JsonIgnore]
    public bool HasValueDifference => HasAmountDifference || HasSecondaryDifference;

    /// <summary>
    /// A line is only "fully verified" when the second engine independently supplied every
    /// comparable field. A field that the primary engine only received by backfilling from
    /// the secondary engine is not independent evidence, and a single-engine legacy row is
    /// never reported as consistent.
    /// </summary>
    [JsonIgnore]
    public bool IsFullyVerified =>
        VerificationAmount is not null &&
        VerificationQuantity is not null &&
        VerificationUnitPrice is not null &&
        !string.IsNullOrWhiteSpace(VerificationProductName) &&
        !string.IsNullOrWhiteSpace(VerificationUnit) &&
        !BackfilledFields.Contains("ProductName") &&
        !BackfilledFields.Contains("UnitPrice") &&
        !BackfilledFields.Contains("Quantity") &&
        !BackfilledFields.Contains("Unit");
}
