using System.Globalization;
using System.Text.Json.Serialization;

namespace CustomsClearanceConsole;

internal static class CurrencyNames
{
    public static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["美元"] = "USD", ["USD"] = "USD", ["US$"] = "USD",
        ["人民币"] = "CNY", ["CNY"] = "CNY", ["RMB"] = "CNY",
        ["欧元"] = "EUR", ["EUR"] = "EUR",
        ["英镑"] = "GBP", ["GBP"] = "GBP",
        ["日元"] = "JPY", ["JPY"] = "JPY",
        ["港币"] = "HKD", ["HKD"] = "HKD",
        ["加拿大元"] = "CAD", ["加元"] = "CAD", ["CAD"] = "CAD",
        ["澳大利亚元"] = "AUD", ["澳元"] = "AUD", ["AUD"] = "AUD",
        ["新加坡元"] = "SGD", ["SGD"] = "SGD"
    };

    public static string? Normalize(string input)
    {
        var compact = input.Replace(" ", "").Trim('(', ')', '（', '）', ':', '：');
        if (Map.TryGetValue(compact, out var code)) return code;
        return Map.FirstOrDefault(x => compact.Contains(x.Key, StringComparison.OrdinalIgnoreCase)).Value;
    }
}
