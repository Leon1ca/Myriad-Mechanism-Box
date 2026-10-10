namespace CustomsClearanceConsole;

internal sealed partial class MainForm
{
    private void BuildWorkspace()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, BackColor = Theme.Canvas };
        _root = root;
        // Explicit 100% columns: an implicit AutoSize column takes its widest child's current
        // width, which after the DPI scale pass is wider than the window.
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTokens.Metrics.Header));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildHeader(), 0, 0);

        _body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty, BackColor = Theme.Canvas };
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _body.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));   // title row
        _body.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));  // stats row
        _body.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // progress strip
        _body.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));    // lock bar
        _body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // records panel

        _body.Controls.Add(BuildTitleRow(), 0, 0);
        _body.Controls.Add(BuildStatsRow(), 0, 1);

        _progressStrip = new ProgressStrip { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        _body.Controls.Add(_progressStrip, 0, 2);
        _lockBar = new LockBar { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        _body.Controls.Add(_lockBar, 0, 3);

        _body.Controls.Add(BuildRecordsPanel(), 0, 4);
        root.Controls.Add(_body, 0, 1);
        Controls.Add(root);
        EdgeHitPassThrough.Attach(this, [root, _body, .. root.Controls.Cast<Control>(), .. EdgeControls]);

        _toast = new ToastControl { Anchor = AnchorStyles.Bottom, Location = new Point(0, ClientSize.Height - 50) };
        Controls.Add(_toast);
        _toast.BringToFront();
        ClientSizeChanged += (_, _) =>
        {
            ApplyResponsiveLayout();
            _toast.Location = new Point((ClientSize.Width - _toast.Width) / 2, ClientSize.Height - _toast.Height - 18);
        };
    }

    /// <summary>Title-bar controls that touch the window edge (see EdgeHitPassThrough).</summary>
    private readonly List<Control> EdgeControls = [];

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Theme.HeaderBg, AccessibleName = "应用标题栏" };
        var logoBack = new RoundedPanel { Location = new Point(20, 10), Size = new Size(28, 28), Radius = 6, BackColor = Theme.Accent, BorderColor = Theme.Accent };
        var logo = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        var appIcon = LoadApplicationIcon();
        if (appIcon is not null) logo.Image = appIcon;
        logoBack.Controls.Add(logo);
        header.Controls.Add(logoBack);
        var title = new Label
        {
            Text = "关单核验台",
            Location = new Point(60, 0),
            Size = new Size(160, UiTokens.Metrics.Header),
            Font = AppFonts.Ui(15F, UiWeight.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        header.Controls.Add(title);
        _headerTitle = title;
        // The title label used to stay 160px wide from x=60 (right edge 220) and covered the
        // fixed divider (x=186) and version (x=197); the real composited window then clipped
        // the version to "5.0" even though DrawToBitmap looked fine. Size the label to its
        // real text and place the divider/version strictly after it (P2).
        // Measured without a device context (system DPI) and converted back to logical pixels,
        // because the whole header is built in logical pixels and scaled once afterwards.
        var titleWidth = Math.Max(1, DpiLayout.MeasuredToLogical(TextRenderer.MeasureText(title.Text, title.Font).Width));
        title.Size = new Size(titleWidth, UiTokens.Metrics.Header);
        var divider = new Panel
        {
            Location = new Point(title.Right + 16, 14),
            Size = new Size(1, 20),
            BackColor = ColorTranslator.FromHtml("#5C6D82")
        };
        header.Controls.Add(divider);
        _headerDivider = divider;
        var version = new Label
        {
            Text = "v" + (Application.ProductVersion.Split('+')[0]),
            Location = new Point(divider.Right + 10, 0),
            Size = new Size(90, UiTokens.Metrics.Header),
            Font = AppFonts.Mono(12F),
            ForeColor = ColorTranslator.FromHtml("#C9D6E8"),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        header.Controls.Add(version);
        _headerVersion = version;

        // Settings, then the minimize / maximize / close caption buttons flush with the top-right
        // corner at full title-bar height, as in every Windows 11 window.
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty, BackColor = Theme.HeaderBg, Padding = Padding.Empty };
        _settings = Theme.IconButton("设置", new RoundedButton
        {
            BackColor = ColorTranslator.FromHtml("#213855"),
            BorderColor = ColorTranslator.FromHtml("#5C6D82"),
            ForeColor = Color.White,
            HoverBackColor = ColorTranslator.FromHtml("#354A65"),
            PressedBackColor = ColorTranslator.FromHtml("#40566F"),
            DisabledFill = Theme.HeaderBg,
            Radius = 6,
            Size = new Size(78, 30),
            Margin = new Padding(0, 9, 16, 9),
            Font = AppFonts.Ui(13F, UiWeight.Medium)
        }, Ui2.SettingsWhite, 16, DeviceDpi);
        _settings.Click += (_, _) => ShowDirectorySettings();
        actions.Controls.Add(_settings);
        foreach (var glyph in new[] { CaptionGlyph.Minimize, CaptionGlyph.Maximize, CaptionGlyph.Close })
        {
            var button = new WindowCaptionButton(glyph) { Margin = new Padding(0), Size = new Size(46, UiTokens.Metrics.Header), BackColor = Theme.HeaderBg };
            var captured = glyph;
            button.Click += (_, _) =>
            {
                if (captured == CaptionGlyph.Close) Close();
                else if (captured == CaptionGlyph.Minimize) WindowState = FormWindowState.Minimized;
                else { ToggleMaximize(); button.RefreshWindowState(); }
            };
            actions.Controls.Add(button);
        }
        header.Controls.Add(actions);
        EdgeControls.Add(actions);
        EdgeControls.AddRange(actions.Controls.Cast<Control>());
        foreach (var control in new Control[] { header, title, version, divider, logoBack })
            control.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) BeginWindowDrag(); };
        foreach (var control in new Control[] { header, title, version, logoBack })
            control.DoubleClick += (_, _) => ToggleMaximize();
        return header;
    }

    private static Bitmap? LoadApplicationIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "ui-v2", "icons", "app", "app-icon-48.png");
            if (File.Exists(path)) { using var source = new Bitmap(path); return new Bitmap(source); }
            var ico = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(ico)) { using var icon = new Icon(ico); return icon.ToBitmap(); }
        }
        catch (Exception ex) { AppLog.Write(ex); }
        return null;
    }

    private Control BuildTitleRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Canvas };
        _titleRow = row;
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        _titleBlock = new TitleBlock { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
        row.Controls.Add(_titleBlock, 0, 0);
        _exportButton = Theme.IconButton("导出列表", Theme.SecondaryButton(""), Ui2.Export, 16, DeviceDpi);
        if (_exportButton is RoundedButton exportRounded) exportRounded.DropDown = DropDownGlyph.Separated;
        _exportButton.Dock = DockStyle.Fill;
        _exportButton.Margin = new Padding(0, 11, 10, 11);
        _exportButton.AccessibleName = "导出列表";
        _exportButton.Click += (_, _) => ShowExportMenu();
        _exportButton.Disposed += (_, _) => _exportMenu?.Dispose();
        row.Controls.Add(_exportButton, 1, 0);
        _scan = Theme.IconButton("开始识别", Theme.PrimaryButton(""), Ui2.Play, 16, DeviceDpi);
        _scan.Dock = DockStyle.Fill;
        _scan.Margin = new Padding(0, 11, 0, 11);
        _scan.AccessibleName = "开始识别";
        _scan.Click += async (_, _) => { if (_session is null) await ScanAsync(); else _session.Cancel(); };
        row.Controls.Add(_scan, 2, 0);
        return row;
    }

    private Control BuildStatsRow()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Canvas };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 480));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _statsRow = row;
        _kpi = new KpiPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 0) };
        _moneySummary = new MoneySummaryPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        row.Controls.Add(_kpi, 0, 0);
        row.Controls.Add(_moneySummary, 1, 0);
        return row;
    }

    private Control BuildRecordsPanel()
    {
        var panel = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface, BorderColor = Theme.Border, Radius = 8, Margin = Padding.Empty };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(1), Margin = Padding.Empty, BackColor = Theme.Surface };
        _recordsContent = content;
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(16, 0, 16, 0), BackColor = Theme.Surface };
        // A single explicit Percent row keeps the nested toolbar inside its fixed 56/50 slot.
        // Without it the implicit AutoSize row inflates to the tallest child's preferred size
        // (a bare Panel defaults to 100px), pushing the cleanup anchor and the search/filter
        // row far outside the visible slot (R4-2).
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 372));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 348));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        _toolbar = toolbar;
        _filterSegmented = new FilterSegmented { Dock = DockStyle.Fill, Margin = new Padding(0, 11, 0, 11) };
        _filterSegmented.SegmentSelected += (_, index) => { _filterIndex = index; _page = 1; RefreshGrid(); };
        _filterSegmented.Disposed += (_, _) => { };
        toolbar.Controls.Add(_filterSegmented, 0, 0);
        _search = new TextBox { PlaceholderText = "搜索单号、文件名、收货人、合同号", BorderStyle = BorderStyle.None, Font = Theme.UiFont(13F), AccessibleName = "搜索关单记录", BackColor = Color.White };
        _searchHost = new SearchField(_search) { Dock = DockStyle.Fill, Margin = new Padding(0, 11, 0, 11) };
        toolbar.Controls.Add(_searchHost, 1, 0);
        _cleanupButton = Theme.IconButton("清理", Theme.QuietButton(""), Ui2.TrashInk, 16, DeviceDpi);
        if (_cleanupButton is RoundedButton cleanupRounded) cleanupRounded.DropDown = DropDownGlyph.Plain;
        _cleanupButton.Dock = DockStyle.Fill;
        _cleanupButton.Margin = new Padding(0, 11, 0, 11);
        _cleanupButton.AccessibleName = "清理";
        _cleanupButton.Click += (_, _) => { if (_session is null) _cleanupMenu.Show(_cleanupButton, _cleanupButton.Height + 6, true); };
        _cleanupButton.Disposed += (_, _) => _cleanupMenu.Dispose();
        toolbar.Controls.Add(_cleanupButton, 3, 0);
        content.Controls.Add(toolbar, 0, 0);

        _grid = BuildGrid();
        _recordState = new RecordStatePanel { Visible = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        _recordState.ActionInvoked += (_, action) => { if (action == "选择关单目录") ShowDirectorySettings(); };
        _gridHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, AllowDrop = true };
        _gridHost.Controls.Add(_grid);
        _gridHost.Controls.Add(_recordState);
        foreach (var target in new Control[] { _gridHost, _grid, _recordState })
        {
            target.AllowDrop = true;
            target.DragEnter += GridDragEnter;
            target.DragOver += GridDragOver;
            target.DragDrop += GridDragDrop;
            target.DragLeave += (_, _) => HideDropFeedback();
        }
        content.Controls.Add(_gridHost, 0, 1);
        content.Controls.Add(BuildFooter(), 0, 2);
        panel.Controls.Add(content);
        return panel;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(16, 0, 16, 0), BackColor = Theme.Surface };
        // Same explicit row as the toolbar: paging buttons and the page-size dropdown must
        // stay inside the fixed 44px footer instead of expanding it (R4-2).
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _footerPanel = footer;
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        _footer = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Font = Theme.UiFont(12.5F), AutoEllipsis = true };
        _previous = Theme.QuietButton("上一页");
        _previous.Dock = DockStyle.Fill;
        _previous.Margin = new Padding(0, 8, 6, 8);
        _previous.Click += (_, _) => { if (_page > 1) { _page--; RefreshGrid(); } };
        _pageLabel = new PageBadge { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = Theme.MonoFont(12.5F, true) };
        _next = Theme.QuietButton("下一页");
        _next.Dock = DockStyle.Fill;
        _next.Margin = new Padding(6, 8, 0, 8);
        _next.Click += (_, _) => { if (_page < PageCount()) { _page++; RefreshGrid(); } };
        var perPage = new Label { Text = "每页", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Muted, Font = Theme.UiFont(12.5F) };
        _pageSize = new ModernDropDown { Dock = DockStyle.Fill, Margin = new Padding(8, 8, 0, 8), ItemHeight = 30, OpenUpward = true, UseMonoValue = true, AccessibleName = "每页显示条数" };
        _pageSize.Items.AddRange(["50 条", "100 条", "200 条"]);
        _pageSize.SelectedIndexChanged += (_, _) => { _page = 1; RefreshGrid(); };
        footer.Controls.Add(_footer, 0, 0);
        footer.Controls.Add(_previous, 1, 0);
        footer.Controls.Add(_pageLabel, 2, 0);
        footer.Controls.Add(_next, 3, 0);
        footer.Controls.Add(perPage, 4, 0);
        footer.Controls.Add(_pageSize, 5, 0);
        return footer;
    }

    private RecordGrid BuildGrid()
    {
        var grid = new RecordGrid
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Theme.Surface,
            BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            // Column borders are dragged through the fit-mode resizer below (live, neighbour
            // compensated); the grid's own resize would change one column and break the fill.
            AllowUserToResizeColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            ReadOnly = true,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = true,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 38,
            // Design 2.5: rows are separated by a 1px divider only; no vertical grid lines.
            CellBorderStyle = DataGridViewCellBorderStyle.None,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowTemplate = { Height = 54 },
            ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable,
            ScrollBars = ScrollBars.Both,
            AllowUserToOrderColumns = false
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.PanelSubtle,
            ForeColor = Theme.Muted,
            Font = Theme.UiFont(12.5F, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            SelectionBackColor = Theme.PanelSubtle,
            SelectionForeColor = Theme.Muted,
            // Zero here: DataGridViewCellStyle inheritance skips a Padding.Empty at a lower
            // level (DataGridViewColumnHeaderCell.GetInheritedStyle falls through cell ->
            // ColumnHeadersDefaultCellStyle -> DefaultCellStyle), so the real zero must be the
            // shared base. Non-index columns opt back into the 8px inset below (P2).
            Padding = Padding.Empty
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.UiFont(13.5F),
            SelectionBackColor = UiTokens.Status.CellSelected,
            SelectionForeColor = Theme.Text,
            // Data cells keep their 8px inset through each column's DefaultCellStyle; the
            // shared default is zero so the narrow index header can inherit a true zero.
            Padding = Padding.Empty,
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            NullValue = "—",
            WrapMode = DataGridViewTriState.False
        };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.Surface;
        grid.GridColor = Theme.Divider;
        AddColumn(grid, "Index", "#", DataGridViewContentAlignment.MiddleCenter);
        AddColumn(grid, "Status", "状态", DataGridViewContentAlignment.MiddleCenter);
        AddColumn(grid, "No", "报关单号 / 来源文件", DataGridViewContentAlignment.MiddleLeft);
        AddColumn(grid, "Consignee", "境外收货人", DataGridViewContentAlignment.MiddleLeft);
        AddColumn(grid, "Contract", "合同协议号", DataGridViewContentAlignment.MiddleLeft);
        AddColumn(grid, "Port", "出境关别", DataGridViewContentAlignment.MiddleLeft);
        AddColumn(grid, "Dest", "目的国", DataGridViewContentAlignment.MiddleLeft);
        AddColumn(grid, "PortDest", "出境关别 / 目的国", DataGridViewContentAlignment.MiddleLeft);
        AddColumn(grid, "Amount", "关单总货值", DataGridViewContentAlignment.MiddleRight);
        AddColumn(grid, "Detail", "查看", DataGridViewContentAlignment.MiddleCenter);
        AddColumn(grid, "Verify", "网页核验", DataGridViewContentAlignment.MiddleCenter);

        // The shared header padding is a true zero so the 28px index column can inherit it
        // and centre-draw the full "#". A local Padding.Empty on the index header would be
        // ignored by DataGridViewCellStyle.ApplyStyle and silently fall back to the inherited
        // 8px inset, which clipped the glyph to a sliver on real screens (P2). Every other
        // column opts back into the 8px inset explicitly, keeping their previous appearance.
        var indexHeader = grid.Columns["Index"].HeaderCell.Style;
        indexHeader.Alignment = DataGridViewContentAlignment.MiddleCenter;
        foreach (DataGridViewColumn column in grid.Columns)
            if (column.Name != "Index")
                column.HeaderCell.Style.Padding = new Padding(8, 0, 8, 0);

        grid.CellPainting += PaintRecordCell;
        grid.CellClick += GridCellClick;
        grid.CellDoubleClick += GridCellDoubleClick;
        grid.CellMouseDown += (_, e) => { SelectCellForContextMenu(e); if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0) _copyMenu.ShowAt(grid, Cursor.Position, grid.SelectedCells.Count > 0); };
        grid.KeyDown += GridKeyDown;
        AttachColumnResizer(grid);
        grid.SizeChanged += (_, _) => ApplyGridColumns();
        grid.VerticalBarVisibilityChanged += (_, _) => ApplyGridColumns();
        // No CellFormatting blanking: custom painting happens in CellPainting with
        // e.Handled = true, so real cell values stay available for copy, tooltips and
        // accessibility. Blanks would make No/Amount/Status copy as empty (R3-2).
        _copyMenu = new CopyContextMenu(CopySelectedCells);
        grid.Disposed += (_, _) => _copyMenu.Dispose();
        return grid;
    }

    private static int LogicalMinimumWidth(string column) => column switch { "Index" => 28, "Status" => 64, "No" => 150, _ => 48 };

    private static void AddColumn(DataGridView grid, string name, string header, DataGridViewContentAlignment alignment) =>
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Resizable = DataGridViewTriState.True,
            MinimumWidth = LogicalMinimumWidth(name),
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = alignment, NullValue = "—", Padding = new Padding(8, 0, 8, 0) }
        });

    private void ApplyResponsiveLayout()
    {
        if (IsDisposed || _body is null || _grid is null || _recordsContent is null) return;
        SuspendLayout();
        _body.SuspendLayout();
        // Everything is expressed in the DPI the children are laid out at (see DpiLayout), and
        // every fixed row/column size is set here from its logical value, so the result never
        // depends on whether an earlier scale pass touched it.
        var dpi = _layoutDpi;
        var logicalWidth = (int)Math.Round(ClientSize.Width * 96.0 / dpi);
        var logicalHeight = (int)Math.Round(ClientSize.Height * 96.0 / dpi);
        _layout = Responsive.Compute(logicalWidth, logicalHeight);
        int S(int px) => (int)Math.Round(px * dpi / 96.0);
        _root.RowStyles[0].Height = S(UiTokens.Metrics.Header);
        _titleRow.ColumnStyles[1].Width = S(158);
        _titleRow.ColumnStyles[2].Width = S(150);
        _recordsContent.RowStyles[2].Height = S(44);
        _toolbar.ColumnStyles[3].Width = S(112);
        var footerColumns = new[] { 72, 64, 72, 44, 104 };
        for (var i = 0; i < footerColumns.Length; i++) _footerPanel.ColumnStyles[i + 1].Width = S(footerColumns[i]);
        _body.Padding = new Padding(S(24), S(_layout.ContentPaddingY), S(24), S(_layout.ContentPaddingY));
        _body.RowStyles[0].Height = S(_layout.CompactHeight ? 56 : 62);
        _body.RowStyles[1].Height = StatsRowHeight();
        var processing = CurrentState == BatchState.Processing;
        _body.RowStyles[2].Height = processing ? S(_layout.CompactHeight ? 92 : 104) : 0;
        _body.RowStyles[3].Height = processing ? S(_layout.CompactHeight ? 34 : 38) : 0;
        _statsRow.ColumnStyles[0].Width = S(_layout.KpiPanelWidth);
        _statsRow.Margin = new Padding(0, 0, 0, S(_layout.SectionGap));
        SizeFilterColumn();
        _toolbar.ColumnStyles[1].Width = S(_layout.SearchWidth + 8);
        _recordsContent.RowStyles[0].Height = S(_layout.Toolbar);
        _grid.ColumnHeadersHeight = S(38);
        _grid.RowTemplate.Height = S(_layout.TableRow);
        ApplyGridColumns();
        ResumeLayout(true);
        _body.ResumeLayout(true);
    }

    /// <summary>Columns that absorb width changes; the others hold fixed-size content.</summary>
    private static readonly string[] FlexibleColumns = ["Consignee", "Contract", "Port", "Dest", "PortDest"];

    /// <summary>
    /// "Fit" column layout (as in PrimeNG / ag-Grid fit mode): the visible columns always fill
    /// the grid's client width exactly, so the first and last columns stay flush with both
    /// edges. The base widths are the responsive design widths, or the widths the user dragged
    /// (kept in logical pixels); any difference to the available width is spread over the
    /// flexible text columns in proportion to their width, never below their minimum.
    /// </summary>
    private void ApplyGridColumns()
    {
        if (_grid is null || _grid.Columns.Count == 0 || _applyingWidths) return;
        var dpi = _layoutDpi;
        int S(int px) => (int)Math.Round(px * dpi / 96.0);
        var table = _layout.Table;
        _applyingWidths = true;
        try
        {
            var merged = table.PortDestMerged;
            _grid.Columns["Port"].Visible = !merged;
            _grid.Columns["Dest"].Visible = !merged;
            _grid.Columns["PortDest"].Visible = merged;
            foreach (DataGridViewColumn each in _grid.Columns) each.MinimumWidth = Math.Max(2, S(LogicalMinimumWidth(each.Name)));
            var visible = _grid.Columns.Cast<DataGridViewColumn>().Where(x => x.Visible).OrderBy(x => x.DisplayIndex).ToList();
            int DesignWidth(string name) => name switch
            {
                "Index" => table.Index, "Status" => table.Status, "No" => table.Number, "Consignee" => table.Consignee,
                "Contract" => table.Contract, "Port" => table.Port, "Dest" => table.Dest, "PortDest" => table.PortDest,
                "Amount" => table.Amount, "Detail" => table.Detail, _ => table.Verify
            };
            var widths = visible.ToDictionary(x => x.Name,
                x => Math.Max(x.MinimumWidth, S(_userLogicalWidths.TryGetValue(x.Name, out var user) ? user : DesignWidth(x.Name))));
            var available = _grid.FitWidth;
            if (available <= 0) available = widths.Values.Sum();
            DistributeWidth(visible, widths, available - widths.Values.Sum());
            foreach (var column in visible) column.Width = widths[column.Name];
        }
        finally { _applyingWidths = false; }
    }

    /// <summary>Spreads <paramref name="delta"/> over the flexible columns (fixed ones only as a last resort).</summary>
    private static void DistributeWidth(List<DataGridViewColumn> visible, Dictionary<string, int> widths, int delta)
    {
        if (delta == 0) return;
        var flexible = visible.Where(x => FlexibleColumns.Contains(x.Name)).ToList();
        if (flexible.Count == 0) flexible = visible;
        for (var pass = 0; pass < 4 && delta != 0; pass++)
        {
            var candidates = delta > 0 ? flexible : flexible.Where(x => widths[x.Name] > x.MinimumWidth).ToList();
            if (candidates.Count == 0) break;
            var basis = Math.Max(1, candidates.Sum(x => widths[x.Name]));
            var remaining = delta;
            for (var i = 0; i < candidates.Count; i++)
            {
                var column = candidates[i];
                var share = i == candidates.Count - 1 ? remaining : (int)Math.Round((double)delta * widths[column.Name] / basis);
                var next = Math.Max(column.MinimumWidth, widths[column.Name] + share);
                remaining -= next - widths[column.Name];
                widths[column.Name] = next;
            }
            delta = remaining;
        }
    }

    // ---- fit-mode column resizing ----

    private int _resizeLeft = -1;
    private int _resizeStartX;
    private int _resizeStartLeftWidth;
    private int _resizeStartRightWidth;

    /// <summary>
    /// Live border dragging in the header: the column left of the border and its right
    /// neighbour change by the same amount, so the total stays equal to the grid width. The
    /// last column's right edge is the grid edge and cannot be dragged. Double-clicking a
    /// border restores the design widths.
    /// </summary>
    private void AttachColumnResizer(RecordGrid grid)
    {
        grid.MouseMove += (_, e) =>
        {
            if (_resizeLeft >= 0)
            {
                var left = VisibleColumnAt(_resizeLeft);
                var right = VisibleColumnAt(_resizeLeft + 1);
                if (left is null || right is null) return;
                var delta = e.X - _resizeStartX;
                delta = Math.Max(delta, left.MinimumWidth - _resizeStartLeftWidth);
                delta = Math.Min(delta, _resizeStartRightWidth - right.MinimumWidth);
                _applyingWidths = true;
                try
                {
                    left.Width = _resizeStartLeftWidth + delta;
                    right.Width = _resizeStartRightWidth - delta;
                }
                finally { _applyingWidths = false; }
                return;
            }
            var border = BorderAt(e.Location);
            var wanted = border >= 0 ? Cursors.SizeWE : Cursors.Default;
            if (grid.Cursor != wanted) grid.Cursor = wanted;
        };
        grid.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            var border = BorderAt(e.Location);
            if (border < 0) return;
            var left = VisibleColumnAt(border);
            var right = VisibleColumnAt(border + 1);
            if (left is null || right is null) return;
            if (e.Clicks >= 2)
            {
                _userLogicalWidths.Clear();
                ApplyGridColumns();
                return;
            }
            _resizeLeft = border;
            _resizeStartX = e.X;
            _resizeStartLeftWidth = left.Width;
            _resizeStartRightWidth = right.Width;
            grid.Capture = true;
        };
        grid.MouseUp += (_, _) =>
        {
            if (_resizeLeft < 0) return;
            _resizeLeft = -1;
            grid.Capture = false;
            // Keep every visible width in logical pixels so later window resizes and DPI
            // changes scale the user's layout instead of discarding it.
            foreach (DataGridViewColumn column in grid.Columns)
                if (column.Visible) _userLogicalWidths[column.Name] = (int)Math.Round(column.Width * 96.0 / _layoutDpi);
        };
        grid.MouseLeave += (_, _) => { if (_resizeLeft < 0 && grid.Cursor == Cursors.SizeWE) grid.Cursor = Cursors.Default; };
    }

    private DataGridViewColumn? VisibleColumnAt(int visibleIndex) =>
        _grid.Columns.Cast<DataGridViewColumn>().Where(x => x.Visible).OrderBy(x => x.DisplayIndex).ElementAtOrDefault(visibleIndex);

    /// <summary>Index of the visible column whose right border is under the header point, or -1.</summary>
    private int BorderAt(Point point)
    {
        if (point.Y < 0 || point.Y > _grid.ColumnHeadersHeight) return -1;
        var grip = Math.Max(3, DpiLayout.Scale(4, _layoutDpi));
        var visible = _grid.Columns.Cast<DataGridViewColumn>().Where(x => x.Visible).OrderBy(x => x.DisplayIndex).ToList();
        for (var i = 0; i < visible.Count - 1; i++)
        {
            var rect = _grid.GetColumnDisplayRectangle(visible[i].Index, false);
            if (rect.Width > 0 && Math.Abs(point.X - rect.Right) <= grip) return i;
        }
        return -1;
    }

    /// <summary>Double-buffered grid so repaints during resize do not flicker.</summary>
    private sealed class RecordGrid : DataGridView
    {
        public event EventHandler? VerticalBarVisibilityChanged;

        public RecordGrid()
        {
            DoubleBuffered = true;
            VerticalScrollBar.VisibleChanged += (_, e) => VerticalBarVisibilityChanged?.Invoke(this, e);
        }

        /// <summary>Width the columns must fill: the client width minus a visible vertical scroll bar.</summary>
        public int FitWidth => ClientSize.Width - (VerticalScrollBar.Visible ? VerticalScrollBar.Width : 0);
    }
}
