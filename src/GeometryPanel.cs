using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;
namespace ZWLibrary
{
    public sealed class GeometryPanel : UserControl
    {
        private readonly bool layout;
        private Catalog catalog;
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false };
        private readonly TextBox search = new TextBox { Dock = DockStyle.Fill };
        private readonly ComboBox category = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private bool filtering, fitting;
        private sealed class CategoryChoice { internal string Value; public override string ToString() { return Value == null ? "전체" : Value.Length == 0 ? "미분류" : Value; } }
        private readonly PreviewCanvas preview = new PreviewCanvas();
        private readonly Label status = new Label { AutoSize = true, Dock = DockStyle.Fill };
        private readonly AutoPreviewSession session;
        private readonly List<Action> enable = new List<Action>();
        private readonly System.Windows.Forms.Timer stateTimer = new System.Windows.Forms.Timer { Interval = 400 };
        private int generation;
        public GeometryPanel(bool isLayout)
        {
            layout = isLayout; AutoScaleMode = AutoScaleMode.Font; AutoScroll = true; preview.HighlightInsertionPoint = true; session = new AutoPreviewSession(preview);
            var root = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 4, Padding = new Padding(5) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var filters = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 2, AutoSize = true };
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            filters.Controls.Add(new Label { Text = "검색", AutoSize = true }, 0, 0); filters.Controls.Add(search, 1, 0);
            filters.Controls.Add(new Label { Text = "분류", AutoSize = true }, 0, 1); filters.Controls.Add(category, 1, 1); root.Controls.Add(filters, 0, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            Add(actions, "선택 등록", () => Plugin.Queue(layout ? "ZLIBLAYADD" : "ZLIBADD", null), CanDraw);
            Add(actions, "DWG 등록", () => Plugin.Queue(layout ? "ZLIBLAYDWG" : "ZLIBDWG", null), CanDraw);
            Add(actions, "정보 수정", Edit, HasSelected); Add(actions, "도형 교체", () => Plugin.Queue(layout ? "ZLIBLAYREPLACE" : "ZLIBSYMREPLACE", Selected().Id), () => HasSelected() && CanDraw());
            var menu = new ContextMenuStrip();
            AddMenu(menu, "삭제…", Delete, HasSelected); menu.Items.Add(new ToolStripSeparator());
            AddMenu(menu, "미리보기 새로고침", () => { PreviewCache.Clear(); UpdateSelection(); }, HasSelected); AddMenu(menu, "새로고침", Reload, () => true);
            Button more = null; more = Add(actions, "더보기 ▾", () => menu.Show(more, new Point(0, more.Height)), () => true); grid.ContextMenuStrip = menu;
            root.Controls.Add(actions, 0, 1);
            string[] names = { "Name", "Category", "Asset", "SourceUnitMm" }, titles = { "이름", "분류", "DWG 파일명", "원본 단위(mm)" };
            for (int i = 0; i < names.Length; i++) grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = names[i], HeaderText = titles[i], Width = i == 0 || i == 2 ? 180 : 110 });
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(600, 380), SplitterDistance = 200, Panel1MinSize = 30, Panel2MinSize = 30 };
            split.Panel1.Controls.Add(grid); split.Panel2.Controls.Add(preview); split.Panel2.Controls.Add(new Label { Text = layout ? "배치도형 미리보기" : "심볼 미리보기", Dock = DockStyle.Top, AutoSize = true });
            root.Controls.Add(split, 0, 2); root.Controls.Add(status, 0, 3); Controls.Add(root);
            search.TextChanged += delegate { Filter(); }; category.SelectedIndexChanged += delegate { Filter(); }; grid.SelectionChanged += delegate { if (!filtering) UpdateSelection(); };
            actions.Dock = DockStyle.Top; actions.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.RowStyles[0].SizeType = SizeType.Absolute; root.RowStyles[1].SizeType = SizeType.Absolute;
            Action fit = () =>
            {
                if (fitting) return; fitting = true;
                try
                {
                    actions.MaximumSize = new Size(Math.Max(170, ClientSize.Width-20), 0);
                    int filtersHeight = search.PreferredHeight + category.PreferredHeight + 16;
                    int actionsHeight = actions.GetPreferredSize(new Size(Math.Max(170, ClientSize.Width-20), 0)).Height + 6;
                    root.RowStyles[0].Height = filtersHeight; root.RowStyles[1].Height = actionsHeight;
                    int minimum = filtersHeight + actionsHeight + status.PreferredHeight + Math.Max(180, Font.Height * 12) + 20;
                    AutoScrollMinSize = new Size(0, minimum); root.Height = Math.Max(ClientSize.Height, minimum);
                }
                finally { fitting = false; }
            };
            SizeChanged += delegate { fit(); }; FontChanged += delegate { fit(); }; fit();
            stateTimer.Tick += delegate { if (!Plugin.IsRunning) foreach (var update in enable) update(); }; stateTimer.Start(); Reload();
        }
        private bool CanDraw() { if (Plugin.IsRunning) return false; var doc = CadApp.DocumentManager.MdiActiveDocument; return doc != null && string.IsNullOrEmpty(doc.CommandInProgress); }
        private bool HasSelected() { return catalog != null && grid.SelectedRows.Count > 0 && grid.CurrentRow != null && grid.CurrentRow.DataBoundItem is GeometryAsset; }
        private GeometryAsset Selected() { if (!HasSelected()) throw new InvalidOperationException("도형을 선택하세요."); return (GeometryAsset)grid.CurrentRow.DataBoundItem; }
        private Button Add(FlowLayoutPanel flow, string text, Action action, Func<bool> enabled)
        { var b = new Button { Text = text, AutoSize = true }; b.Click += delegate { Safe(action); }; flow.Controls.Add(b); enable.Add(() => b.Enabled = enabled()); return b; }
        private void AddMenu(ContextMenuStrip menu, string text, Action action, Func<bool> enabled)
        { var item = new ToolStripMenuItem(text); item.Click += delegate { Safe(action); }; menu.Items.Add(item); enable.Add(() => item.Enabled = enabled()); }
        private void Safe(Action action) { try { action(); } catch (System.Exception ex) { MessageBox.Show(this, ex.Message); } }
        public void Reload()
        {
            try
            {
                catalog = Settings.Current.Load(); var previous = category.SelectedItem as CategoryChoice; filtering = true;
                category.Items.Clear(); category.Items.Add(new CategoryChoice());
                foreach (var value in (layout ? catalog.Layouts : catalog.Symbols).Select(g => BomLogic.Clean(g.Category)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)) category.Items.Add(new CategoryChoice { Value = value });
                category.SelectedIndex = 0;
                if (previous != null) for (int i = 0; i < category.Items.Count; i++) if (string.Equals(((CategoryChoice)category.Items[i]).Value, previous.Value, StringComparison.OrdinalIgnoreCase)) { category.SelectedIndex = i; break; }
                filtering = false; Filter();
            }
            catch (System.Exception ex) { filtering = false; catalog = null; grid.DataSource = null; session.Request(null, "목록을 읽을 수 없습니다.", () => true); status.Text = ex.Message; }
        }
        private void Filter()
        {
            if (catalog == null || filtering) return;
            string previous = HasSelected() ? Selected().Id : null; var choice = category.SelectedItem as CategoryChoice;
            var rows = GeometrySearch.Filter(GeometrySearch.Choices(catalog, layout), search.Text, choice == null ? null : choice.Value, null).Select(x => x.Geometry).ToList();
            filtering = true;
            try { grid.DataSource = rows; int index = rows.FindIndex(g => g.Id == previous); if (index >= 0) grid.CurrentCell = grid.Rows[index].Cells[0]; }
            finally { filtering = false; }
            status.Text = rows.Count + " / " + (layout ? catalog.Layouts.Count : catalog.Symbols.Count) + "개 / 참조 중인 도형 삭제 차단"; UpdateSelection();
        }
        private void UpdateSelection()
        {
            foreach (var update in enable) update(); int ticket = ++generation;
            if (!HasSelected()) { session.Request(null, layout ? "배치도형 미등록 / 선택 없음" : "심볼 미등록 / 선택 없음", () => true); return; }
            var g = Selected(); session.Request(Settings.Current.AssetPath(g.Asset), "도형 미등록", () => !IsDisposed && ticket == generation && HasSelected() && Selected().Id == g.Id);
        }
        private void Edit() { var g = Selected(); using (var d = new GeometryDialog(g, false, "")) if (CadApp.ShowModalDialog(d) == DialogResult.OK) { Settings.Current.UpdateGeometry(catalog.Revision, d.Value, layout); PreviewCache.Clear(); Plugin.RefreshPanels(); } }
        private void Delete() { var g = Selected(); if (MessageBox.Show(this, g.Name + " 등록을 삭제할까요? 복구용 DWG 파일은 보존됩니다.", "도형 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return; Settings.Current.Save(catalog.Revision, c => CatalogModel.DeleteGeometry(c, g.Id, layout)); PreviewCache.Clear(); Plugin.RefreshPanels(); }
        protected override void Dispose(bool disposing) { if (disposing) { stateTimer.Dispose(); session.Dispose(); } base.Dispose(disposing); }
    }
}
