namespace CustomsClearanceConsole;

/// <summary>
/// Exact production target identity for the single-window inquiry page. Parsing the Uri
/// and checking scheme + host + route prevents an unrelated page that merely contains the
/// string "singlewindow" (for example https://example.com/?singlewindow) from being
/// accepted as the verification target.
///
/// The official page (verified statically, see planning/official-query-reference.md)
/// embeds its query form in a cross-origin iframe <c>id=myedit</c> served from
/// <c>swapp.singlewindow.cn</c> on <c>/qspserver/sw/qsp/query/view/queryDecStatus</c>.
/// The allowlist therefore has to keep that verified frame, not merely reject every
/// cross-origin frame.
/// </summary>
internal static class TargetUrlPolicy
{
    public const string PrimaryUrl = "https://www.singlewindow.cn/#/publicInquiryDetail?id=pi4";

    public const string VerifiedQueryFrameHost = "swapp.singlewindow.cn";
    public const string VerifiedQueryFramePath = "/qspserver/sw/qsp/query/view/queryDecStatus";

    public static bool IsSingleWindowInquiry(string? url)
    {
        if (!IsSingleWindowOrigin(url)) return false;
        return IsInquiryRoute(new Uri(url!, UriKind.Absolute));
    }

    /// <summary>The inquiry route, taken from the fragment when the page routes by hash.</summary>
    public static bool IsInquiryRoute(Uri uri)
    {
        var route = uri.Fragment.Length > 0 ? uri.Fragment : uri.PathAndQuery;
        return route.Contains("/publicInquiryDetail", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The trusted single-window origins that may host the workspace/query shell.</summary>
    public static bool IsSingleWindowOrigin(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) return false;
        return uri.Host.Equals("www.singlewindow.cn", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("singlewindow.cn", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Authorizes a sub-frame. Same-origin single-window frames inherit the trusted page,
    /// and the one verified official query frame (host + exact route) is explicitly kept.
    /// Any other origin, route or an unknown/opaque URL is rejected.
    /// </summary>
    public static bool IsAuthorizedFrame(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (IsSingleWindowOrigin(url)) return true;
        if (uri.Host.Equals(VerifiedQueryFrameHost, StringComparison.OrdinalIgnoreCase) &&
            uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.Contains(VerifiedQueryFramePath, StringComparison.OrdinalIgnoreCase);
        return false;
    }
}
