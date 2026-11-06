using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;

namespace ZWLibrary
{
    internal sealed class AvailabilityButton : Button
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled) { base.OnPaint(e); return; }
            ButtonRenderer.DrawButton(e.Graphics, ClientRectangle, System.Windows.Forms.VisualStyles.PushButtonState.Disabled);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Color.FromArgb(90, 90, 90), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
    public sealed class LibraryPanel : UserControl
    {
        private readonly TextBox search = new TextBox { Dock = DockStyle.Fill };
        private readonly ComboBox category = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList }, maker = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox archived = new CheckBox { Text = "사용 중지 포함", AutoSize = true };
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoGenerateColumns = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false };
        private readonly Label status = new Label { AutoSize = true, Dock = DockStyle.Fill, AutoEllipsis = true };
        private readonly PreviewCanvas symbolPreview = new PreviewCanvas(), layoutPreview = new PreviewCanvas();
        private readonly AutoPreviewSession symbolSession, layoutSession;
        private readonly List<Action> enableUpdates = new List<Action>();
        private readonly List<FlowLayoutPanel> groups = new List<FlowLayoutPanel>();
        private readonly System.Windows.Forms.Timer stateTimer = new System.Windows.Forms.Timer { Interval = 400 };
        private Catalog catalog;
        private bool filtering;
        private bool fitting;
        private string previewKey;
        private sealed class Choice { internal string Value; public override string ToString() { return Value == null ? "전체" : Value.Length == 0 ? "(미입력)" : Value; } }
        public LibraryPanel()
        {
            AutoScaleMode = AutoScaleMode.Font; AutoScroll = true;
            symbolPreview.HighlightInsertionPoint = true; layoutPreview.HighlightInsertionPoint = true;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            symbolSession = new AutoPreviewSession(symbolPreview); layoutSession = new AutoPreviewSession(layoutPreview);
            var root = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 4, Padding = new Padding(5) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var filters = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 3 };
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            filters.Controls.Add(new Label { Text = "검색", AutoSize = true }, 0, 0); filters.Controls.Add(search, 1, 0); filters.SetColumnSpan(search, 3);
            filters.Controls.Add(new Label { Text = "분류", AutoSize = true }, 0, 1); filters.Controls.Add(category, 1, 1); filters.Controls.Add(new Label { Text = "제조사", AutoSize = true }, 2, 1); filters.Controls.Add(maker, 3, 1);
            filters.Controls.Add(archived, 0, 2); filters.SetColumnSpan(archived, 2);
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            Add(options, "필터 초기화", ResetFilter, () => catalog != null); Add(options, "설정 창", OpenSettings, () => true);
            filters.Controls.Add(options, 2, 2); filters.SetColumnSpan(options, 2); root.Controls.Add(filters, 0, 0);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 3 };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            FlowLayoutPanel inserts = Group(actions, "삽입", 0), parts = Group(actions, "부품 관리", 1), drawing = Group(actions, "도면 작업", 2);
            Add(inserts, "심볼 삽입", () => Plugin.Queue("ZLIBINSERT", Selected().Id), () => CanInsert(false));
            Add(inserts, "배치도형 삽입", () => Plugin.Queue("ZLIBLAYDIRECT", Selected().Id), () => CanInsert(true));
            Add(parts, "신규 등록", () => EditPart(new Part(), true), () => catalog != null);
            Add(parts, "정보 수정", () => EditPart(Selected(), false), HasSelected);
            var more = new ContextMenuStrip();
            AddMenu(more, "복제", () => EditPart(CatalogModel.Duplicate(Selected()), true), HasSelected);
            AddMenu(more, "사용 중지·복원", Toggle, HasSelected);
            more.Items.Add(new ToolStripSeparator()); AddMenu(more, "삭제…", Delete, HasSelected);
            more.Items.Add(new ToolStripSeparator());
            AddMenu(more, "배치도형 선택 등록", () => Plugin.Queue("ZLIBLAYADD", Selected().Id), HasSelected);
            AddMenu(more, "배치도형 DWG 등록", () => Plugin.Queue("ZLIBLAYDWG", Selected().Id), HasSelected);
            AddMenu(more, "미리보기 새로고침", () => { PreviewCache.Clear(); previewKey = null; UpdateSelection(); }, HasSelected);
            AddMenu(more, "새로고침", Reload, () => true);
            Button moreButton = null; moreButton = Add(parts, "더보기 ▾", () => more.Show(moreButton, new Point(0, moreButton.Height)), () => true);
            grid.ContextMenuStrip = more; more.Opening += delegate { UpdateEnabled(); };
            Add(drawing, "선택 심볼 → 배치", () => Plugin.Queue("ZLIBLAYINSERT", null), CanDrawing);
            Add(drawing, "선택 BOM", () => Plugin.Queue("ZLIBBOM", null), CanDrawing);
            Add(drawing, "선택 PARTLIST", () => Plugin.Queue("ZLIBPARTLIST", null), CanDrawing);
            Add(drawing, "도면 부품정보", () => Plugin.Queue("ZLIBTAG", null), CanDrawing);
            root.Controls.Add(actions, 0, 1);
            string[] names = { "Model", "Brand", "DeviceNumberPrefix", "Name", "Spec", "Category", "SymbolName", "LayoutName", "Bom", "Active" };
            string[] titles = { "모델", "제조사", "기기번호 접두어", "품명", "규격", "분류", "연결 심볼", "연결 배치도형", "BOM", "사용" };
            for (int i = 0; i < names.Length; i++) grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = names[i], HeaderText = titles[i], Width = i == 2 ? 145 : i < 5 ? 110 : 90 });
            var previews = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            previews.Controls.Add(PreviewBox("연결 심볼", symbolPreview), 0, 0); previews.Controls.Add(PreviewBox("연결 배치도형", layoutPreview), 1, 0);
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(600, 380), SplitterDistance = 260, Panel1MinSize = 80, Panel2MinSize = 60, FixedPanel = FixedPanel.Panel2 };
            split.Panel1.Controls.Add(grid); split.Panel2.Controls.Add(previews); root.Controls.Add(split, 0, 2); root.Controls.Add(status, 0, 3); Controls.Add(root);
            grid.SelectionChanged += delegate { if (!filtering) UpdateSelection(); };
            grid.DoubleClick += delegate { if (CanInsert(false)) Safe(() => Plugin.Queue("ZLIBINSERT", Selected().Id)); };
            grid.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e) { if (e.Value is bool) { e.Value = grid.Columns[e.ColumnIndex].DataPropertyName == "Bom" ? ((bool)e.Value ? "포함" : "제외") : ((bool)e.Value ? "사용" : "중지"); e.FormattingApplied = true; } };
            search.TextChanged += delegate { Filter(); }; category.SelectedIndexChanged += delegate { Filter(); }; maker.SelectedIndexChanged += delegate { Filter(); }; archived.CheckedChanged += delegate { Filter(); };
            foreach (var group in groups) { group.Dock = DockStyle.Top; group.AutoSizeMode = AutoSizeMode.GrowAndShrink; }
            actions.Dock = DockStyle.Top; actions.AutoSize = false;
            filters.Dock = DockStyle.Top; filters.AutoSize = false;
            for (int i = 0; i < 3; i++) actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            for (int i = 0; i < 3; i++) filters.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles[0].SizeType = SizeType.Absolute; root.RowStyles[1].SizeType = SizeType.Absolute;
            Action fit = () =>
            {
                if (fitting) return;
                fitting = true;
                try
                {
                int width = Math.Max(170, ClientSize.Width - 120), actionHeight = 0;
                for (int i = 0; i < groups.Count; i++)
                {
                    groups[i].MaximumSize = new Size(width, 0);
                    int h = Math.Max(Font.Height + 18, groups[i].GetPreferredSize(new Size(width, 0)).Height + 6);
                    actions.RowStyles[i].Height = h; actionHeight += h;
                }
                int filterHeight = 0;
                int[] heights = { search.PreferredHeight + 6, Math.Max(category.PreferredHeight, maker.PreferredHeight) + 6, Math.Max(archived.PreferredSize.Height, options.GetPreferredSize(new Size(Math.Max(100, ClientSize.Width / 2 - 20), 0)).Height) + 6 };
                for (int i = 0; i < heights.Length; i++) { filters.RowStyles[i].Height = heights[i]; filterHeight += heights[i]; }
                filters.Height = filterHeight; actions.Height = actionHeight;
                root.RowStyles[0].Height = filterHeight + 6; root.RowStyles[1].Height = actionHeight + 6;
                int minimum = filterHeight + actionHeight + status.PreferredHeight + Math.Max(220, Font.Height * 14) + 30;
                AutoScrollMinSize = new Size(0, minimum); root.Height = Math.Max(ClientSize.Height, minimum);
                }
                finally { fitting = false; }
            };
            SizeChanged += delegate { fit(); }; FontChanged += delegate { fit(); }; fit();
            stateTimer.Tick += delegate { if (!Plugin.IsRunning) UpdateEnabled(); }; stateTimer.Start();
            grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) { grid.ClearSelection(); grid.CurrentCell = grid.Rows[e.RowIndex].Cells[0]; grid.Rows[e.RowIndex].Selected = true; } };
            Reload();
        }
        private static Control PreviewBox(string title, Control canvas)
        { var box = new Panel { Dock = DockStyle.Fill, Margin = new Padding(2) }; box.Controls.Add(canvas); box.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, AutoSize = true, TextAlign = ContentAlignment.MiddleCenter }); return box; }
        private FlowLayoutPanel Group(TableLayoutPanel parent, string title, int row)
        { var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; groups.Add(flow); parent.Controls.Add(new Label { Text = title, AutoSize = true, Padding = new Padding(0, 8, 6, 0) }, 0, row); parent.Controls.Add(flow, 1, row); return flow; }
        private Button Add(FlowLayoutPanel parent, string text, Action action, Func<bool> enabled)
        { var b = new AvailabilityButton { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(5, 2, 5, 2) }; b.Click += delegate { Safe(action); }; parent.Controls.Add(b); enableUpdates.Add(() => b.Enabled = enabled()); return b; }
        private void AddMenu(ContextMenuStrip menu, string text, Action action, Func<bool> enabled)
        { var item = new ToolStripMenuItem(text); item.Click += delegate { Safe(action); }; menu.Items.Add(item); enableUpdates.Add(() => item.Enabled = enabled()); }
        private void Safe(Action action) { try { action(); } catch (System.Exception ex) { MessageBox.Show(this, ex.Message, "ZWLibrary"); } }
        private bool HasSelected() { return catalog != null && grid.SelectedRows.Count > 0 && grid.CurrentRow != null && grid.CurrentRow.DataBoundItem is Part; }
        private bool CanInsert(bool layout) { if (!CanDrawing() || !HasSelected()) return false; var p = Selected(); return p.Active && !string.IsNullOrEmpty(layout ? p.LayoutId : p.SymbolId); }
        private bool CanDrawing() { if (Plugin.IsRunning) return false; var doc = CadApp.DocumentManager.MdiActiveDocument; return doc != null && string.IsNullOrEmpty(doc.CommandInProgress); }
        private void UpdateEnabled() { foreach (var update in enableUpdates) update(); }
        private Part Selected() { if (!HasSelected()) throw new InvalidOperationException("부품을 선택하세요."); return (Part)grid.CurrentRow.DataBoundItem; }
        public void Reload()
        {
            try { catalog = Settings.Current.Load(); filtering = true; Fill(category, catalog.Parts.Select(p => p.Category)); Fill(maker, catalog.Parts.Select(p => p.Brand)); filtering = false; Filter(); }
            catch (System.Exception ex) { filtering = false; catalog = null; grid.DataSource = null; ClearPreviews("목록을 읽을 수 없습니다."); status.Text = ex.Message; UpdateEnabled(); }
        }
        private static string ChoiceValue(ComboBox c) { var value = c.SelectedItem as Choice; return value == null ? null : value.Value; }
        private static void Fill(ComboBox c, IEnumerable<string> values)
        { string previous = ChoiceValue(c); c.Items.Clear(); c.Items.Add(new Choice()); foreach (var v in values.Select(s => (s ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s)) c.Items.Add(new Choice { Value = v }); c.SelectedIndex = 0; for (int i = 1; i < c.Items.Count; i++) if (string.Equals(((Choice)c.Items[i]).Value, previous, StringComparison.OrdinalIgnoreCase)) { c.SelectedIndex = i; break; } }
        private void Filter()
        {
            if (catalog == null || filtering) return;
            string previous = HasSelected() ? Selected().Id : null, q = search.Text.Trim(), cat = ChoiceValue(category), brand = ChoiceValue(maker);
            var rows = catalog.Parts.Where(p => (archived.Checked || p.Active) && (cat == null || string.Equals((p.Category ?? "").Trim(), cat, StringComparison.OrdinalIgnoreCase)) && (brand == null || string.Equals((p.Brand ?? "").Trim(), brand, StringComparison.OrdinalIgnoreCase)) && string.Join(" ", new[] { p.Name, p.Brand, p.Model, p.Spec, p.Category, p.SymbolName, p.LayoutName }).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            filtering = true; try { grid.DataSource = rows; int index = rows.FindIndex(p => p.Id == previous); if (index >= 0) grid.CurrentCell = grid.Rows[index].Cells[0]; } finally { filtering = false; }
            status.Text = rows.Count + " / " + catalog.Parts.Count + "개 | v0.8.4"; UpdateSelection();
        }
        private void ClearPreviews(string message)
        { previewKey = null; symbolSession.Request(null, message, () => true); layoutSession.Request(null, message, () => true); }
        private void UpdateSelection()
        {
            UpdateEnabled(); if (!HasSelected()) { ClearPreviews("부품을 선택하세요."); return; }
            var p = Selected(); var store = Settings.Current; string key = store.Root + "|" + p.Id + "|" + p.Asset + "|" + p.LayoutAsset + "|" + catalog.Revision;
            if (previewKey == key) return; previewKey = key;
            Func<bool> current = () => !IsDisposed && HasSelected() && Selected().Id == p.Id && previewKey == key;
            symbolSession.Request(string.IsNullOrEmpty(p.Asset) ? null : store.AssetPath(p.Asset), "심볼 미등록", current);
            layoutSession.Request(string.IsNullOrEmpty(p.LayoutAsset) ? null : store.AssetPath(p.LayoutAsset), "배치도형 미등록", current);
        }
        internal void SetPreview(Part part, PreviewData data, string root, string kind = "심볼") { previewKey = null; UpdateSelection(); }
        private void ResetFilter() { filtering = true; search.Clear(); category.SelectedIndex = 0; maker.SelectedIndex = 0; archived.Checked = false; filtering = false; Filter(); }
        private void EditPart(Part original, bool create)
        {
            var store = Settings.Current;
            using (var d = new PartEditor(original, catalog)) if (CadApp.ShowModalDialog(d) == DialogResult.OK)
            { if (!create) d.Value.Revision++; store.Save(catalog.Revision, c => { if (create) c.Parts.Add(d.Value); else c.Parts[c.Parts.FindIndex(p => p.Id == original.Id)] = d.Value; }); Plugin.RefreshPanels(); if (d.NextCommand != null) Plugin.Queue(d.NextCommand, d.Value.Id); }
        }
        private void Toggle() { var p = Selected(); if (MessageBox.Show(this, p.Active ? "사용 중지할까요? 기존 도형은 보존됩니다." : "다시 사용하도록 복원할까요?", "ZWLibrary", MessageBoxButtons.OKCancel) != DialogResult.OK) return; Settings.Current.Save(catalog.Revision, c => { var target = c.Parts.Single(x => x.Id == p.Id); target.Active = !target.Active; target.Revision++; }); Plugin.RefreshPanels(); }
        private void Delete() { var p = Selected(); if (MessageBox.Show(this, p.Model + " / " + p.Brand + "\n등록 항목을 삭제할까요? 기존 도면과 복구용 DWG는 보존됩니다.", "부품 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return; Settings.Current.Delete(catalog.Revision, p.Id); Plugin.RefreshPanels(); }
        private void OpenSettings() { using (var form = new SettingsDialog()) if (CadApp.ShowModalDialog(form) == DialogResult.OK) Plugin.RefreshPanels(); }
        protected override void Dispose(bool disposing) { if (disposing) { stateTimer.Dispose(); symbolSession.Dispose(); layoutSession.Dispose(); } base.Dispose(disposing); }
    }
}
