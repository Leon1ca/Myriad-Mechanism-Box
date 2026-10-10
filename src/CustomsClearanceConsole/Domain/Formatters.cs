using System.Globalization;
using System.Text.Json.Serialization;

namespace CustomsClearanceConsole;

internal static class Formatters
{
    public static string MoneyTotalsLines(Dictionary<string, decimal> values) => values.Count == 0
        ? "—"
        : string.Join(Environment.NewLine, values.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value.ToString("N2", CultureInfo.CurrentCulture)}"));

}
