namespace CustomsClearanceConsole;

/// <summary>Normalizes optional values from earlier JSON without discarding valid records.</summary>
internal static class StateMigration
{
    public static AppState Normalize(AppState state)
    {
        state.LastFolder ??= "";
        state.ScreenshotFolder ??= "";
        if (state.PageSize is not (50 or 100 or 200)) state.PageSize = 50;
        state.Records = (state.Records ?? []).Where(record => record is not null).ToList();
        foreach (var record in state.Records)
        {
            record.SourcePath ??= "";
            record.DeclarationNo ??= "";
            record.Consignee ??= "";
            record.ContractNo ??= "";
            record.ExitCustoms ??= "";
            record.DestinationCountry ??= "";
            record.Warning ??= "";
            record.DuplicateWarning ??= "";
            record.ScreenshotPath ??= "";
            if (string.IsNullOrWhiteSpace(record.Status)) record.Status = "需关注";
            var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in record.Totals ?? [])
            {
                var currency = pair.Key.Trim().ToUpperInvariant();
                if (currency.Length == 0 || conflicts.Contains(currency)) continue;
                if (totals.TryGetValue(currency, out var amount) && amount != pair.Value)
                {
                    conflicts.Add(currency);
                    totals.Remove(currency);
                }
                else totals[currency] = pair.Value;
            }
            record.Totals = totals;
            if (conflicts.Count > 0)
            {
                record.Status = "需关注";
                record.Warning = string.Join("；", new[] { record.Warning,
                    "历史币种金额冲突，已排除冲突合计，请重新识别：" + string.Join("、", conflicts) }
                    .Where(value => value.Length > 0));
            }
            record.LineTotals = (record.LineTotals ?? []).Where(line => line is not null).ToList();
            foreach (var line in record.LineTotals)
            {
                line.ItemNo ??= "";
                line.ProductName ??= "";
                line.Unit ??= "";
                line.Currency ??= "";
                line.Note ??= "";
                line.VerificationProductName ??= "";
                line.VerificationUnit ??= "";
                line.BackfilledFields ??= [];
            }
        }
        return state;
    }
}
