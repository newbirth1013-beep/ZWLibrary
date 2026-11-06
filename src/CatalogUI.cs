using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;

namespace ZWLibrary
{
    public sealed class GeometryDialog : Form
    {
        public GeometryAsset Value { get; private set; }
        public GeometryDialog(GeometryAsset initial, bool registration, string hint)
        {
            Value = initial.Copy(); Text = registration ? "도형 등록 — 기준점 및 실제 단위" : "도형 정보 수정";
            Width = 620; Height = 350; StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Font;
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var name = new TextBox { Text = initial.Name, Dock = DockStyle.Fill, MaxLength = 200 };
            var category = new TextBox { Text = initial.Category, Dock = DockStyle.Fill, MaxLength = 200 };
            table.Controls.Add(new Label { Text = "도형 이름 *" }, 0, 0); table.Controls.Add(name, 1, 0);
            table.Controls.Add(new Label { Text = "분류" }, 0, 1); table.Controls.Add(category, 1, 1);
            var unit = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            unit.Items.AddRange(new object[] { "선택하세요", "1 mm", "10 mm (cm)", "1000 mm (m)", "25.4 mm (inch)" }); unit.SelectedIndex = Array.IndexOf(new[] { 0.0, 1, 10, 1000, 25.4 }, initial.SourceUnitMm); if (unit.SelectedIndex < 0) unit.SelectedIndex = 0;
            var filename = new TextBox { Text = initial.Asset ?? "", Dock = DockStyle.Fill, MaxLength = 180 };
            if (!registration)
            {
                table.Controls.Add(new Label { Text = "DWG 파일명 (.dwg)", AutoSize = true }, 0, 2); table.Controls.Add(filename, 1, 2);
                table.Controls.Add(new Label { Text = "원본 1단위: " + initial.SourceUnitMm + " mm (유지)\n내부 ID와 부품 연결은 변경하지 않습니다.", AutoSize = true }, 1, 3);
            }
            if (registration)
            {
                table.Controls.Add(new Label { Text = "원본 1단위 *" }, 0, 2); table.Controls.Add(unit, 1, 2);
                var info = new Label { Text = "도면 설정: " + hint + "\n선택 기준점 / DWG INSBASE를 원점으로 옮기고 mm로 저장합니다.", AutoSize = true };
                table.Controls.Add(info, 0, 3); table.SetColumnSpan(info, 2);
            }
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "저장" };
            ok.Click += delegate
            {
                if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Any(char.IsControl)) { MessageBox.Show(this, "도형 이름을 입력하세요."); return; }
                if (registration && unit.SelectedIndex == 0) { MessageBox.Show(this, "실제 원본 단위를 선택하세요."); return; }
                if (!registration) { try { AssetNames.Validate(filename.Text.Trim()); } catch (Exception ex) { MessageBox.Show(this, ex.Message); return; } Value.Asset = filename.Text.Trim(); }
                Value.Name = name.Text.Trim(); Value.Category = category.Text.Trim();
                if (registration) Value.SourceUnitMm = new[] { 0.0, 1, 10, 1000, 25.4 }[unit.SelectedIndex];
                DialogResult = DialogResult.OK;
            };
            buttons.Controls.Add(cancel); buttons.Controls.Add(ok); Controls.Add(table); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel;
        }
    }
    public sealed class PartEditor : Form
    {
        public Part Value { get; private set; }
        public string NextCommand { get; private set; }
        public PartEditor(Part initial, Catalog catalog)
        {
            Value = initial.Copy(); Text = "개별 부품 정보 / 공용 도형 연결"; Width = 680; Height = 710;
            StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(600, 500);
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            string[] labels = { "모델 *", "제조사 *", "품명", "규격", "분류" };
            string[] values = { initial.Model, initial.Brand, initial.Name, initial.Spec, initial.Category };
            var fields = new TextBox[labels.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                fields[i] = new TextBox { Text = values[i] ?? "", MaxLength = 200, Dock = DockStyle.Fill };
                table.Controls.Add(new Label { Text = labels[i], AutoSize = true }, 0, i); table.Controls.Add(fields[i], 1, i);
            }
            var symbol = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            var layout = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            symbol.Items.Add("미등록 / 연결 해제"); foreach (var g in catalog.Symbols) symbol.Items.Add(g);
            layout.Items.Add("미등록 / 연결 해제"); foreach (var g in catalog.Layouts) layout.Items.Add(g);
            symbol.SelectedItem = catalog.Symbols.FirstOrDefault(g => g.Id == initial.SymbolId);
            layout.SelectedItem = catalog.Layouts.FirstOrDefault(g => g.Id == initial.LayoutId);
            if (symbol.SelectedIndex < 0) symbol.SelectedIndex = 0;
            if (layout.SelectedIndex < 0) layout.SelectedIndex = 0;
            var numbering = new CheckBox { Text = "삽입 시 기기번호 입력", Checked = DeviceNumbers.InputEnabled(initial, Settings.Load()), AutoSize = true };
            var prefix = new TextBox { Text = initial.DeviceNumberPrefix ?? "", Dock = DockStyle.Fill, MaxLength = 200, Enabled = numbering.Checked };
            var example = new Label { AutoSize = true };
            Action updateExample = () => { prefix.Enabled = numbering.Checked; string text = prefix.Text.Trim(); example.Text = numbering.Checked ? "예: " + (text.Length == 0 ? "01" : text + " 01") + " (삽입할 때 01만 입력)\n접두어가 비어 있으면 입력한 번호만 사용합니다." : "기기번호를 묻지 않습니다. 원본 문자는 유지합니다."; };
            numbering.CheckedChanged += delegate { updateExample(); }; prefix.TextChanged += delegate { updateExample(); }; updateExample();
            table.Controls.Add(new Label { Text = "기기번호" }, 0, 5); table.Controls.Add(numbering, 1, 5);
            table.Controls.Add(new Label { Text = "기기번호 접두어" }, 0, 6); table.Controls.Add(prefix, 1, 6);
            table.Controls.Add(example, 1, 7);
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = "심볼 (선택)" }, 0, 8); table.Controls.Add(Connection(symbol, catalog, false), 1, 8);
            table.Controls.Add(new Label { Text = "배치도형 (선택)" }, 0, 9); table.Controls.Add(Connection(layout, catalog, true), 1, 9);
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            var bom = new CheckBox { Text = "BOM 집계에 포함", Checked = initial.Bom, AutoSize = true }; table.Controls.Add(bom, 1, 10);
            var registration = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var selection = new Button { Text = "저장 후 배치 선택 등록", AutoSize = true };
            var dwg = new Button { Text = "저장 후 배치 DWG 등록", AutoSize = true };
            registration.Controls.Add(selection); registration.Controls.Add(dwg); table.Controls.Add(registration, 1, 11);
            table.Controls.Add(new Label { Text = "심볼과 배치도형은 각각 선택 사항입니다. 연결한 도형만 삽입할 수 있습니다.\n검색/선택에서 이름·분류·연결 부품의 제조사로 찾을 수 있습니다.\n공용 도형 변경은 이미 삽입한 객체에 적용되지 않습니다.", AutoSize = true }, 1, 12);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel }; var save = new Button { Text = "저장" };
            Action<string> finish = command =>
            {
                try
                {
                    Value.Model = fields[0].Text.Trim(); Value.Brand = fields[1].Text.Trim(); Value.Name = fields[2].Text.Trim();
                    Value.Spec = fields[3].Text.Trim(); Value.Category = fields[4].Text.Trim(); Value.Bom = bom.Checked;
                    Value.NumberingEnabled = numbering.Checked; Value.DeviceNumberPrefix = prefix.Text.Trim();
                    var s = symbol.SelectedItem as GeometryAsset; var l = layout.SelectedItem as GeometryAsset;
                    Value.SymbolId = s == null ? null : s.Id; Value.LayoutId = l == null ? null : l.Id;
                    Value.Asset = null; Value.LayoutAsset = null; PartEditing.Prepare(Value);
                    NextCommand = command; DialogResult = DialogResult.OK;
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message); }
            };
            save.Click += delegate { finish(null); }; selection.Click += delegate { finish("ZLIBLAYADD"); }; dwg.Click += delegate { finish("ZLIBLAYDWG"); };
            footer.Controls.Add(cancel); footer.Controls.Add(save); Controls.Add(table); Controls.Add(footer); AcceptButton = save; CancelButton = cancel;
        }
        private Control Connection(ComboBox combo, Catalog catalog, bool layout)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            var find = new Button { Text = "검색/선택…", Dock = DockStyle.Fill, Margin = new Padding(3, 0, 0, 0) };
            find.Click += delegate
            {
                var selected = combo.SelectedItem as GeometryAsset;
                using (var dialog = new GeometryPicker(catalog, layout, selected == null ? null : selected.Id))
                    if (dialog.ShowDialog(this) == DialogResult.OK) combo.SelectedItem = dialog.Value == null ? combo.Items[0] : (object)dialog.Value;
            };
            row.Controls.Add(combo, 0, 0); row.Controls.Add(find, 1, 0); return row;
        }
    }
    public sealed class GeometryPicker : Form
    {
        public GeometryAsset Value { get; private set; }
        private sealed class FilterChoice
        {
            internal string Value;
            public override string ToString() { return Value == null ? "전체" : Value.Length == 0 ? "미지정" : Value; }
        }
        public GeometryPicker(Catalog catalog, bool layout, string selectedId)
        {
            Text = layout ? "배치도형 검색 / 연결" : "심볼 검색 / 연결";
            Width = 900; Height = 580; MinimumSize = new Size(600, 400); StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Font;
            var choices = GeometrySearch.Choices(catalog, layout);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 4 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var filters = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 2 };
            filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var search = new TextBox { Dock = DockStyle.Fill };
            var category = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            var maker = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            category.Items.Add(new FilterChoice()); maker.Items.Add(new FilterChoice());
            foreach (var value in choices.Select(x => BomLogic.Clean(x.Category)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)) category.Items.Add(new FilterChoice { Value = value });
            foreach (var value in choices.SelectMany(x => x.MakerValues.Length == 0 ? new[] { "" } : x.MakerValues).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)) maker.Items.Add(new FilterChoice { Value = value });
            category.SelectedIndex = 0; maker.SelectedIndex = 0;
            filters.Controls.Add(new Label { Text = "검색", AutoSize = true }, 0, 0); filters.Controls.Add(search, 1, 0); filters.SetColumnSpan(search, 3);
            filters.Controls.Add(new Label { Text = "분류", AutoSize = true }, 0, 1); filters.Controls.Add(category, 1, 1); filters.Controls.Add(new Label { Text = "제조사", AutoSize = true }, 2, 1); filters.Controls.Add(maker, 3, 1);
            root.Controls.Add(filters, 0, 0);
            root.Controls.Add(new Label { Text = "제조사는 이 도형에 연결된 부품 기준입니다. 미연결 도형은 ‘미지정’으로 찾을 수 있습니다.", AutoSize = true }, 0, 1);
            var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            string[] properties = { "Name", "Category", "Makers", "FileName" }, labels = { "도형 이름", "분류", "연결 부품 제조사", "DWG 파일명" };
            for (int i = 0; i < properties.Length; i++) grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = properties[i], HeaderText = labels[i], MinimumWidth = 90, FillWeight = i == 0 ? 150 : 100 });
            root.Controls.Add(grid, 0, 2);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "선택" }; var clear = new Button { Text = "연결 해제", AutoSize = true };
            Action select = () => { var row = grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as GeometryChoice; if (row == null) return; Value = row.Geometry; DialogResult = DialogResult.OK; };
            ok.Click += delegate { select(); }; grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) select(); };
            clear.Click += delegate { Value = null; DialogResult = DialogResult.OK; };
            grid.SelectionChanged += delegate { ok.Enabled = grid.CurrentRow != null; };
            Action filter = () =>
            {
                string previous = grid.CurrentRow == null ? selectedId : ((GeometryChoice)grid.CurrentRow.DataBoundItem).Geometry.Id;
                var rows = GeometrySearch.Filter(choices, search.Text, ((FilterChoice)category.SelectedItem).Value, ((FilterChoice)maker.SelectedItem).Value);
                grid.DataSource = rows; int index = rows.FindIndex(x => x.Geometry.Id == previous); if (index >= 0) grid.CurrentCell = grid.Rows[index].Cells[0]; ok.Enabled = rows.Count > 0;
            };
            search.TextChanged += delegate { filter(); }; category.SelectedIndexChanged += delegate { filter(); }; maker.SelectedIndexChanged += delegate { filter(); };
            footer.Controls.Add(cancel); footer.Controls.Add(ok); footer.Controls.Add(clear); root.Controls.Add(footer, 0, 3); Controls.Add(root); AcceptButton = ok; CancelButton = cancel; filter();
        }
    }
}
