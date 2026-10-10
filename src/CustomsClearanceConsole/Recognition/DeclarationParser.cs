using System.Globalization;

using System.Text;

using System.Text.RegularExpressions;


namespace CustomsClearanceConsole;

internal sealed partial class DeclarationParser
{
    // The parser and reconciler use ~30 distinct patterns through the static Regex helpers,

    // many inside per-token loops. The framework caches only 15 by default, so patterns were

    // evicted and re-parsed continuously; a larger cache keeps every pattern constructed once.

    static DeclarationParser()

    {

        if (Regex.CacheSize < 64) Regex.CacheSize = 64;

    }



    private static readonly string[] KnownLabels =

    [

        "预录入编号", "海关编号", "境内发货人", "出境关别", "出口日期", "申报日期", "备案号",

        "境外收货人", "运输方式", "运输工具名称及航次号", "提运单号", "生产销售单位", "监管方式",

        "征免性质", "许可证号", "合同协议号", "贸易国", "运抵国", "指运港", "离境口岸", "包装种类",

        "件数", "毛重", "净重", "成交方式", "运费", "保费", "杂费", "随附单证及编号", "标记唛码及备注",

        "项号", "商品编号", "商品名称规格型号", "数量及单位", "单价总价币制", "原产国", "最终目的国",

        "境内货源地", "征免"

    ];



    [GeneratedRegex(@"(?<!\d)\d{18}(?!\d)")]

    private static partial Regex DeclarationRegex();



    [GeneratedRegex(@"^[0-9][0-9,]*(?:\.[0-9]+)?$")]

    private static partial Regex AmountRegex();



    [GeneratedRegex(@"(?<!\d)\(?([0-9]{4})\)?(?!\d)")]

    private static partial Regex CustomsCodeRegex();



    public DeclarationRecord Parse(string path, DocumentText document)

    {

        var record = new DeclarationRecord { SourcePath = path };

        if (document.Pages.Count == 0)

        {

            record.Status = "识别失败";

            record.Warning = "文件中没有可读取的页面";

            return record;

        }



        var first = document.Pages[0];

        var allText = string.Join(" ", first.Tokens.Select(t => t.Text));

        if (FindPriceHeader(first) is null && FindAnchor(first, "海关编号", "境外收货人") is null &&

            (allText.Contains("BILL OF LADING", StringComparison.OrdinalIgnoreCase) ||

             allText.Contains("SAME AS CONSIGNEE", StringComparison.OrdinalIgnoreCase) ||

             allText.Contains("通关无纸化出口放行通知书", StringComparison.Ordinal)))

        {

            record.Status = "需关注";

            record.Warning = "未检测到报关单页面：该文件可能是提单或放行通知书，不计入关单金额";

            return record;

        }

        record.DeclarationNo = FindDeclarationNo(first);

        record.Consignee = ReadLabeledValue(first, "境外收货人");

        record.ContractNo = ReadLabeledValue(first, "合同协议号");

        record.DestinationCountry = ReadLabeledValue(first, "运抵国");



        // Older scans may have damaged labels. Keep the proven normalized regions only as a last fallback.

        if (string.IsNullOrWhiteSpace(record.Consignee))

            record.Consignee = ReadValueImmediatelyAboveLabel(first, "生产销售单位");

        if (string.IsNullOrWhiteSpace(record.Consignee))

            record.Consignee = ReadRegion(first, .025, .32, .215, .255);

        if (string.IsNullOrWhiteSpace(record.ContractNo))

            record.ContractNo = ReadRegion(first, .025, .31, .295, .335);

        if (string.IsNullOrWhiteSpace(record.DestinationCountry))

            record.DestinationCountry = ReadRegion(first, .47, .64, .295, .335);



        record.ExitCustoms = ReadExitCustoms(first, record.DeclarationNo);

        record.LineTotals = ReadLineTotals(document.Pages);

        record.Totals = SumReliableLineTotals(record.LineTotals);



        if (document.UsedOcr)

        {

            record.Consignee = PreferOcrText(record.Consignee, ReadRegion(first, .02, .31, .225, .265), "Amazon.com Services, Inc");

            var contractCandidate = FindContractCandidate(first);

            if (!LooksLikeContract(record.ContractNo) ||

                (record.ContractNo.Contains(' ') && contractCandidate.Length < record.ContractNo.Length))

                record.ContractNo = contractCandidate;

        }



        record.Consignee = CleanValue(record.Consignee, "境外收货人");

        record.Consignee = NormalizeConsigneeIdentifiers(record.Consignee);

        record.ContractNo = NormalizeContractOcr(CleanValue(record.ContractNo, "合同协议号"));

        record.DestinationCountry = ResolveDestinationCountry(first,

            CleanValue(record.DestinationCountry, "运抵国（地区）", "运抵国(地区)", "运抵国"));

        var ruleProblems = ApplyOcrRules(record, document.Pages);

        record.Confidence = CalculateConfidence(record, document.UsedOcr);



        var missing = new List<string>();

        if (record.DeclarationNo.Length != 18) missing.Add("报关单号");

        if (string.IsNullOrWhiteSpace(record.Consignee)) missing.Add("境外收货人");

        if (string.IsNullOrWhiteSpace(record.ContractNo)) missing.Add("合同协议号");

        if (string.IsNullOrWhiteSpace(record.ExitCustoms)) missing.Add("出境关别");

        if (string.IsNullOrWhiteSpace(record.DestinationCountry)) missing.Add("目的国");

        if (record.Totals.Count == 0) missing.Add("关单总货值");



        if (missing.Count == 0 && ruleProblems.Count == 0)

        {

            record.Status = document.UsedOcr ? "OCR 识别完成" : "识别完成";

            record.Warning = document.UsedOcr ? "OCR 识别，已按规则复核（报关单号、数量×单价=总价、国别、关别）" : "";

        }

        else

        {

            record.Status = "需关注";

            record.Warning = JoinWarnings(missing.Count == 0 ? "" : $"未能可靠识别：{string.Join("、", missing)}",

                string.Join("；", ruleProblems));

            if (ruleProblems.Count > 0) record.Confidence = Math.Min(record.Confidence, 80);

        }

        return record;

    }



    private static int CalculateConfidence(DeclarationRecord record, bool usedOcr)

    {

        var score = 0;

        if (record.DeclarationNo.Length == 18) score += 30;

        if (record.Consignee.Length >= 3) score += 10;

        if (record.ContractNo.Length >= 2) score += 10;

        if (record.ExitCustoms.Length >= 2) score += 10;

        if (record.DestinationCountry.Length >= 2) score += 10;

        if (record.Totals.Count > 0 && record.Totals.Values.All(x => x >= 0)) score += 30;

        if (usedOcr) score = Math.Max(0, score - 5);

        return score;

    }


    private static string JoinWarnings(params string[] warnings) =>
        string.Join("；", warnings.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().TrimEnd('；')).Distinct());
}
