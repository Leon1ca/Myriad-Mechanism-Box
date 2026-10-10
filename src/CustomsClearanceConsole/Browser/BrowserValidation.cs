using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace CustomsClearanceConsole;

internal sealed partial class BrowserValidation : IAsyncDisposable
{
    public const string PrimaryUrl = TargetUrlPolicy.PrimaryUrl;
    private const long DefaultMaxPixels = 60_000_000L;
    private const int TileHeight = 12_000;

    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public string DeclarationNo { get; }
    public bool IsRunning => _cdp is { IsClosed: false };

    public event EventHandler<BrowserCaptureResult>? CaptureCompleted;
    public event EventHandler<string>? StatusChanged;
    /// <summary>Raised once when the dedicated browser process exits (for example the user closed it).</summary>
    public event EventHandler? Ended;

    private readonly string _targetFolder;
    private readonly string? _urlOverride;
    private readonly string? _browserPathOverride;
    private readonly long _maxPixels;
    private readonly bool _headless;
    private readonly bool _allowTestTarget;
    private readonly Uri? _testOrigin;
    /// <summary>
    /// A test target opened with a fragment route is judged by the same fragment-route rule as
    /// the production page, so the E2E exercises the official page's hash routing.
    /// </summary>
    private readonly bool _testRoutesByFragment;
    private readonly IReadOnlyList<string>? _extraBrowserArgs;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);
    // A default execution context is only unique per CDP session: the page session and a
    // flattened OOPIF session both start numbering at 1. Contexts are therefore keyed by
    // (sessionId, contextId) and stored as records (R5-4 / OOPIF).
    private sealed record FrameContext(int ContextId, string FrameId, string? SessionId);

    private readonly Dictionary<string, string> _frameUrls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _frameParents = new(StringComparer.Ordinal);
    private readonly List<FrameContext> _frameContexts = [];
    private readonly Dictionary<string, string> _frameTargetSessions = new(StringComparer.Ordinal);
    private CdpClient? _cdp;
    private readonly object _connectionSync = new();
    private Process? _process;
    private int _debugPort;
    private int _captureGate;
    private int _navigationGeneration;
    private int _viewportReadFailures;
    // Baseline viewports for the bounded render-readiness gate: the card click must wait for
    // the capture's temporary viewport enlargement to be restored, and the production
    // completion must not be published before that restore is observable in the page.
    private (int Width, int Height, double Scale)? _clickBaselineViewport;
    private (int Width, int Height, double Scale)? _captureBaselineViewport;
    private string? _shellScriptIdentifier;
    private string? _profileFolder;
    private int _endedRaised;
    private string _mainFrameId = "";
    private string _currentUrl = "";
    private volatile bool _disposed;

    public BrowserValidation(string declarationNo, string targetFolder, string? urlOverride = null,
        string? browserPathOverride = null, long maxPixels = DefaultMaxPixels, bool headless = false,
        bool allowTestTarget = false, IReadOnlyList<string>? extraBrowserArgs = null)
    {
        DeclarationNo = declarationNo;
        _targetFolder = targetFolder;
        _urlOverride = urlOverride;
        _browserPathOverride = browserPathOverride;
        _maxPixels = maxPixels > 0 ? maxPixels : DefaultMaxPixels;
        _headless = headless;
        _allowTestTarget = allowTestTarget;
        _extraBrowserArgs = extraBrowserArgs;
        if (!string.IsNullOrWhiteSpace(urlOverride))
        {
            if (!allowTestTarget)
                throw new ArgumentException("非生产目标地址只能在显式测试选项中授权。", nameof(urlOverride));
            if (!Uri.TryCreate(urlOverride, UriKind.Absolute, out var testUri) || testUri.Host.Length == 0)
                throw new ArgumentException("测试目标地址必须是绝对 URL。", nameof(urlOverride));
            _testOrigin = new Uri($"{testUri.Scheme}://{testUri.Host}:{testUri.Port}");
            _testRoutesByFragment = testUri.Fragment.Length > 1;
        }
    }
}
