using System.Text.Json.Serialization;

namespace CustomsClearanceConsole;

public sealed partial class DeclarationLineTotal
{
    public int Sequence { get; set; }
    public int PageNumber { get; set; }
    public string ItemNo { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal? Quantity { get; set; }
    public string Unit { get; set; } = "";
    public decimal? UnitPrice { get; set; }
    public string Currency { get; set; } = "";
    public decimal Amount { get; set; }
    public bool IsReliable { get; set; } = true;
    public string Note { get; set; } = "";

    /// <summary>
    /// Every number in the row's quantity column (declared, legal and second unit quantities),
    /// used only by the quantity × unit price rule while parsing.
    /// </summary>
    [JsonIgnore]
    public List<decimal> QuantityCandidates { get; } = [];

    /// <summary>
    /// Result of the OCR quantity × unit price = total rule: true when it holds, false when it
    /// does not (the line is then excluded from the total), null when it could not be checked
    /// (no unit price / quantity read).
    /// </summary>
    public bool? RuleCheckPassed { get; set; }

    [JsonIgnore]
    public string DisplayAmount => $"{Currency} {Amount:N2}";

    [JsonIgnore]
    public string DisplayProduct => string.IsNullOrWhiteSpace(ProductName) ? "未识别" : ProductName;

    // Display and tooltip text must never round a source value: a non-zero quantity
    // such as 0.0004 has to stay readable, never collapse to a bare "0".
    [JsonIgnore]
    public string DisplayQuantity => Quantity is null ? "—" : NumberFormats.Exact(Quantity.Value);

    [JsonIgnore]
    public string ExactQuantity => Quantity is null ? "—" : NumberFormats.Exact(Quantity.Value);

    [JsonIgnore]
    public string DisplayUnit => string.IsNullOrWhiteSpace(Unit) ? "—" : Unit;

    [JsonIgnore]
    public string DisplayQuantityUnit => Quantity is null
        ? "—"
        : string.IsNullOrWhiteSpace(Unit) ? DisplayQuantity : $"{DisplayQuantity} {Unit}";

    [JsonIgnore]
    public string ExactQuantityUnit => Quantity is null
        ? "—"
        : string.IsNullOrWhiteSpace(Unit) ? ExactQuantity : $"{ExactQuantity} {Unit}";

    // No rounding here either: 0.1234567 must not display as 0.123457.
    [JsonIgnore]
    public string DisplayUnitPrice => UnitPrice is null ? "—" : NumberFormats.Exact(UnitPrice.Value);

    [JsonIgnore]
    public string ExactUnitPrice => UnitPrice is null ? "—" : NumberFormats.Exact(UnitPrice.Value);

    /// <summary>
    /// Three-state verdict shared by the detail dialog, Excel and Markdown so an amount
    /// conflict can never be exported as "一致" and an unverified row is never "一致".
    /// </summary>
    [JsonIgnore]
    public string ItemConsistency => HasValueDifference ? "存在差异" : IsFullyVerified ? "一致"
        : RuleCheckPassed == true ? "规则校验通过" : RuleCheckPassed == false ? "数量×单价不符" : "未完整复核";

    /// <summary>Per-amount verdict; distinguishes a real amount conflict from "not verified".</summary>
    [JsonIgnore]
    public string AmountVerification => HasAmountDifference
        ? "金额不一致"
        : VerificationAmount is not null && !AmountOnlyFromSecondary ? "金额一致"
        : RuleCheckPassed == true ? "金额规则校验通过" : RuleCheckPassed == false ? "数量×单价不符" : "金额未复核";
}
