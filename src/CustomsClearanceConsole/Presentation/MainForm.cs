namespace CustomsClearanceConsole;

internal sealed partial class MainForm : Form
{
    private enum BatchState { Empty, Ready, Processing, Complete }

    private readonly StateStore _store = new();
    private readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = 200 };
    private AppState _state;
    private int _filterIndex;
    private int _page = 1;
    private List<DeclarationRecord> _visible = [];
    private ScanSession? _session;
    private readonly Dictionary<string, BrowserValidation> _browserSessions = [];
    /// <summary>Column widths the user dragged, in 96-DPI logical pixels.</summary>
    private readonly Dictionary<string, int> _userLogicalWidths = new(StringComparer.Ordinal);
    private bool _applyingWidths;
    private bool _previewProcessing;
    private string _batchFolder = "";
    private int _batchFileCount;
    private string _batchFileSummary = "";
    private DateTime? _scanStartedAt;
    private DateTime? _scanCompletedAt;
    private Responsive.Layout _layout = Responsive.Compute(1200, 720);

    /// <summary>
    /// Snapshot mode only: allows the window to be sized beyond the physical desktop so the
    /// four required logical sizes can be rendered and asserted even on a small cloud
    /// runner. Never enabled for the normal application window.
    /// </summary>
    internal bool AllowOversizeForSnapshot { get; set; }

    private TableLayoutPanel _root = null!;
    private TableLayoutPanel _titleRow = null!;
    private TableLayoutPanel _body = null!;
    /// <summary>
    /// DPI the child bounds are expressed in: 96 (logical) while the form is built, then the
    /// window's real DPI once the handle exists (see DpiLayout).
    /// </summary>
    private int _layoutDpi = DpiLayout.LogicalDpi;
    private TableLayoutPanel _statsRow = null!;
    private TableLayoutPanel _toolbar = null!;
    private TableLayoutPanel _footerPanel = null!;
    private TableLayoutPanel _recordsContent = null!;
    private TitleBlock _titleBlock = null!;
    private Label _headerTitle = null!;
    private Panel _headerDivider = null!;
    private Label _headerVersion = null!;
    private KpiPanel _kpi = null!;
    private MoneySummaryPanel _moneySummary = null!;
    private ProgressStrip _progressStrip = null!;
    private LockBar _lockBar = null!;
    private FilterSegmented _filterSegmented = null!;
    private TextBox _search = null!;
    private SearchField _searchHost = null!;
    private Button _cleanupButton = null!;
    private Button _exportButton = null!;
    private Button _scan = null!;
    private Button _settings = null!;
    private RecordGrid _grid = null!;
    private Panel _gridHost = null!;
    private RecordStatePanel _recordState = null!;
    // Business intent, independent of Control.Visible's effective getter. Before the form is
    // first shown every ancestor reports Visible == false, so reading _recordState.Visible
    // during construction cannot tell us whether the current state wants the panel (P1).
    private bool _recordStateWanted;
    private Label _dropBanner = null!;
    private Label _footer = null!;
    private Label _pageLabel = null!;
    private Button _previous = null!;
    private Button _next = null!;
    private ModernDropDown _pageSize = null!;
    private ToastControl _toast = null!;
    private CopyContextMenu _copyMenu = null!;
    private DesignMenu _exportMenu = null!;
    private DesignMenu _cleanupMenu = null!;

    // ---- snapshot/self-test accessors (same assembly only) ----
    internal TitleBlock TitleBlockForTest => _titleBlock;
    internal KpiPanel KpiForTest => _kpi;
    internal TableLayoutPanel ToolbarForTest => _toolbar;
    internal TableLayoutPanel FooterPanelForTest => _footerPanel;
    internal Button CleanupButtonForTest => _cleanupButton;
    internal Button PreviousForTest => _previous;
    internal Button NextForTest => _next;
    internal Panel GridHostForTest => _gridHost;
    internal DataGridView GridForTest => _grid;
    internal RecordStatePanel RecordStateForTest => _recordState;
    internal bool RecordStateWantedForTest => _recordStateWanted;
    internal Label HeaderTitleForTest => _headerTitle;
    internal Panel HeaderDividerForTest => _headerDivider;
    internal Label HeaderVersionForTest => _headerVersion;

    public MainForm()
    {
        _state = _store.Load();
        if (_state.PageSize is not (50 or 100 or 200)) _state.PageSize = 50;
        _batchFolder = string.IsNullOrWhiteSpace(_state.LastFolder) ? "" : _state.LastFolder;
        if (_batchFolder.Length > 0 && _state.Records.Count == 0) LoadBatchFileSummary(_batchFolder);

        Text = "关单核验台";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1200, 720);
        Size = new Size(1440, 900);
        BackColor = Theme.Canvas;
        Font = Theme.UiFont(13.5F);
        // Built in 96-DPI logical pixels and scaled once the window's real DPI is known
        // (OnHandleCreated / OnDpiChanged). See DpiLayout for why auto-scaling is off.
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(0);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;

        _exportMenu = new DesignMenu(
        [
            new DesignMenu.Entry("excel", "导出为 Excel", null, Ui2.FileExcel, ".xlsx"),
            new DesignMenu.Entry("markdown", "导出为 Markdown", null, Ui2.FileMarkdownInk, ".md")
        ]);
        _exportMenu.ItemSelected += (_, key) => ExportList(key);
        _cleanupMenu = new DesignMenu(
        [
            new DesignMenu.Entry("list", "列表清理", "仅清除识别记录，不删除文件", Ui2.TrashInk),
            new DesignMenu.Entry("docs", "关单清理", "当前层 PDF / 图片移入回收站", Ui2.TrashRed, Danger: true, SeparatorBefore: true),
            new DesignMenu.Entry("screenshots", "截图清理", "核验截图移入回收站", Ui2.TrashRed, Danger: true)
        ]);
        _cleanupMenu.ItemSelected += (_, key) =>
        {
            if (key == "list") ClearList();
            else if (key == "docs") CleanDeclarationFolder();
            else CleanScreenshotFolder();
        };

        BuildWorkspace();
        _pageSize.SelectedItem = $"{_state.PageSize} 条";
        if (_pageSize.SelectedIndex < 0) _pageSize.SelectedItem = "50 条";
        _search.TextChanged += (_, _) => { _searchTimer.Stop(); _searchTimer.Start(); };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); _page = 1; RefreshGrid(resetScroll: true); };
        FormClosing += OnFormClosing;
        ApplyResponsiveLayout();
        RefreshAll();
    }

    /// <summary>
    /// FormClosed is not raised for snapshot forms that are torn down with Dispose(), so stop
    /// the debounce timer here as well; otherwise it keeps firing RefreshGrid on a disposed
    /// window during later DoEvents() calls.
    /// </summary>
    private bool _resourcesDisposed;

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _session?.Cancel();
            _searchTimer.Stop();
            _searchTimer.Dispose();
            _exportMenu?.Dispose();
            _cleanupMenu?.Dispose();
            _copyMenu?.Dispose();
            InvalidateBrowserSessions();
        }
        base.Dispose(disposing);
    }

    private void ShowToast(string message) => _toast.Show(message);

    // ---- persistence / chrome ----

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_session is not null)
        {
            _session.Cancel();
            e.Cancel = true;
            ShowToast("正在停止识别，请完成后关闭窗口。");
            return;
        }
        if (!SaveState() && MessageBox.Show(this,
            "历史记录尚未保存。仍要关闭并放弃本次未保存的修改吗？",
            "历史记录未保存", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        InvalidateBrowserSessions();
    }

    private bool SaveState()
    {
        try
        {
            _state.PageSize = CurrentPageSize();
            _store.Save(_state);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            MessageBox.Show(this, $"历史记录保存失败，当前结果仍保留在窗口中。请导出备份后重试。\n\n{ex.Message}",
                "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
