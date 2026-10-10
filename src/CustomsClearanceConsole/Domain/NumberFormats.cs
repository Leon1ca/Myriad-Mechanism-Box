using System.Globalization;
using System.Text.Json.Serialization;

namespace CustomsClearanceConsole;

/// <summary>
/// Lossless decimal text with thousands separators, used by exports so a value is
/// never rounded to a display-oriented number of decimal places.
/// </summary>
internal static class NumberFormats
{
    public static string Exact(decimal value)
    {
        var text = value.ToString("0.############################", CultureInfo.InvariantCulture);
        var separator = text.IndexOf('.');
        var integerPart = separator < 0 ? text : text[..separator];
        var fraction = separator < 0 ? "" : text[(separator + 1)..];
        if (decimal.TryParse(integerPart, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
            integerPart = integer.ToString("#,0", CultureInfo.InvariantCulture);
        return fraction.Length == 0 ? integerPart : integerPart + "." + fraction;
    }
}
