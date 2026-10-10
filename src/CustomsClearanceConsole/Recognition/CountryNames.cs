namespace CustomsClearanceConsole;

/// <summary>
/// Country / region codes printed on customs declarations (ISO 3166-1 alpha-3, e.g. "运抵国（地区）
/// (DZA)") and their Chinese names. The destination used to be matched against a list of 21
/// countries, so any other country (for example 阿尔及利亚 / DZA) could not be recognised.
/// </summary>
internal static class CountryNames
{
    private const string Table =
        "AFG阿富汗|ALB阿尔巴尼亚|DZA阿尔及利亚|AND安道尔|AGO安哥拉|ATG安提瓜和巴布达|ARG阿根廷|ARM亚美尼亚|ABW阿鲁巴|AUS澳大利亚|" +
        "AUT奥地利|AZE阿塞拜疆|BHS巴哈马|BHR巴林|BGD孟加拉国|BRB巴巴多斯|BLR白俄罗斯|BEL比利时|BLZ伯利兹|BEN贝宁|" +
        "BMU百慕大|BTN不丹|BOL玻利维亚|BIH波斯尼亚和黑塞哥维那|BWA博茨瓦纳|BRA巴西|BRN文莱|BGR保加利亚|BFA布基纳法索|BDI布隆迪|" +
        "CPV佛得角|KHM柬埔寨|CMR喀麦隆|CAN加拿大|CYM开曼群岛|CAF中非|TCD乍得|CHL智利|CHN中国|COL哥伦比亚|" +
        "COM科摩罗|COG刚果（布）|COD刚果（金）|CRI哥斯达黎加|CIV科特迪瓦|HRV克罗地亚|CUB古巴|CUW库拉索|CYP塞浦路斯|CZE捷克|" +
        "DNK丹麦|DJI吉布提|DMA多米尼克|DOM多米尼加|ECU厄瓜多尔|EGY埃及|SLV萨尔瓦多|GNQ赤道几内亚|ERI厄立特里亚|EST爱沙尼亚|" +
        "SWZ斯威士兰|ETH埃塞俄比亚|FJI斐济|FIN芬兰|FRA法国|GAB加蓬|GMB冈比亚|GEO格鲁吉亚|DEU德国|GHA加纳|" +
        "GRC希腊|GRD格林纳达|GTM危地马拉|GIN几内亚|GNB几内亚比绍|GUY圭亚那|HTI海地|HND洪都拉斯|HKG中国香港|HUN匈牙利|" +
        "ISL冰岛|IND印度|IDN印度尼西亚|IRN伊朗|IRQ伊拉克|IRL爱尔兰|ISR以色列|ITA意大利|JAM牙买加|JPN日本|" +
        "JOR约旦|KAZ哈萨克斯坦|KEN肯尼亚|KIR基里巴斯|PRK朝鲜|KOR韩国|KWT科威特|KGZ吉尔吉斯斯坦|LAO老挝|LVA拉脱维亚|" +
        "LBN黎巴嫩|LSO莱索托|LBR利比里亚|LBY利比亚|LIE列支敦士登|LTU立陶宛|LUX卢森堡|MAC中国澳门|MDG马达加斯加|MWI马拉维|" +
        "MYS马来西亚|MDV马尔代夫|MLI马里|MLT马耳他|MHL马绍尔群岛|MRT毛里塔尼亚|MUS毛里求斯|MEX墨西哥|FSM密克罗尼西亚|MDA摩尔多瓦|" +
        "MCO摩纳哥|MNG蒙古|MNE黑山|MAR摩洛哥|MOZ莫桑比克|MMR缅甸|NAM纳米比亚|NRU瑙鲁|NPL尼泊尔|NLD荷兰|" +
        "NZL新西兰|NIC尼加拉瓜|NER尼日尔|NGA尼日利亚|MKD北马其顿|NOR挪威|OMN阿曼|PAK巴基斯坦|PLW帕劳|PSE巴勒斯坦|" +
        "PAN巴拿马|PNG巴布亚新几内亚|PRY巴拉圭|PER秘鲁|PHL菲律宾|POL波兰|PRT葡萄牙|PRI波多黎各|QAT卡塔尔|ROU罗马尼亚|" +
        "RUS俄罗斯|RWA卢旺达|KNA圣基茨和尼维斯|LCA圣卢西亚|VCT圣文森特和格林纳丁斯|WSM萨摩亚|SMR圣马力诺|STP圣多美和普林西比|SAU沙特阿拉伯|SEN塞内加尔|" +
        "SRB塞尔维亚|SYC塞舌尔|SLE塞拉利昂|SGP新加坡|SVK斯洛伐克|SVN斯洛文尼亚|SLB所罗门群岛|SOM索马里|ZAF南非|SSD南苏丹|" +
        "ESP西班牙|LKA斯里兰卡|SDN苏丹|SUR苏里南|SWE瑞典|CHE瑞士|SYR叙利亚|TWN中国台湾|TJK塔吉克斯坦|TZA坦桑尼亚|" +
        "THA泰国|TLS东帝汶|TGO多哥|TON汤加|TTO特立尼达和多巴哥|TUN突尼斯|TUR土耳其|TKM土库曼斯坦|TUV图瓦卢|UGA乌干达|" +
        "UKR乌克兰|ARE阿联酋|GBR英国|USA美国|URY乌拉圭|UZB乌兹别克斯坦|VUT瓦努阿图|VEN委内瑞拉|VNM越南|YEM也门|" +
        "ZMB赞比亚|ZWE津巴布韦|GRL格陵兰|GUM关岛|NCL新喀里多尼亚|PYF法属波利尼西亚|REU留尼汪|GLP瓜德罗普|MTQ马提尼克|GUF法属圭亚那";

    /// <summary>Other spellings that appear on declarations or from OCR, mapped to the table names.</summary>
    private static readonly (string Alias, string Name)[] Aliases =
    [
        ("阿拉伯联合酋长国", "阿联酋"), ("大韩民国", "韩国"), ("俄罗斯联邦", "俄罗斯"), ("美利坚合众国", "美国"),
        ("英国（联合王国）", "英国"), ("联合王国", "英国"), ("土耳其（土耳其共和国）", "土耳其"), ("香港", "中国香港"),
        ("澳门", "中国澳门"), ("台湾", "中国台湾"), ("斯威士", "斯威士兰"), ("捷克共和国", "捷克"), ("刚果民主共和国", "刚果（金）"),
        ("刚果共和国", "刚果（布）"), ("象牙海岸", "科特迪瓦"), ("缅甸联邦", "缅甸"), ("老挝人民民主共和国", "老挝")
    ];

    private static readonly Dictionary<string, string> ByCode;
    /// <summary>Names and aliases, longest first so "印度尼西亚" wins over "印度".</summary>
    private static readonly (string Text, string Name)[] ByText;

    static CountryNames()
    {
        ByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var texts = new List<(string, string)>();
        foreach (var entry in Table.Split('|'))
        {
            var code = entry[..3];
            var name = entry[3..];
            ByCode[code] = name;
            texts.Add((name, name));
        }
        texts.AddRange(Aliases);
        ByText = texts.OrderByDescending(x => x.Item1.Length).ToArray();
    }

    public static string? FromCode(string? code) =>
        code is { Length: 3 } && ByCode.TryGetValue(code, out var name) ? name : null;

    public static bool IsName(string text) => ByText.Any(x => x.Text == text);

    /// <summary>
    /// The country named first in <paramref name="text"/> (longest name at the same position),
    /// or null. "阿尔及利亚 斯基克达（阿尔及利亚）" gives 阿尔及利亚; "印度尼西亚" is not read as "印度".
    /// </summary>
    public static string? FirstNameIn(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var bestIndex = int.MaxValue;
        string? best = null;
        foreach (var (candidate, name) in ByText)
        {
            var index = text.IndexOf(candidate, StringComparison.Ordinal);
            if (index < 0 || index >= bestIndex) continue;
            bestIndex = index;
            best = name;
        }
        return best;
    }
}
