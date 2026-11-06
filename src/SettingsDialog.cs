using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;

namespace ZWLibrary
{
    public sealed class SettingsDialog : Form
    {
        public SettingsDialog()
        {
            var value = Settings.Load();
            Text = "ZWLibrary 설정"; Width = 680; Height = 660;
            MinimumSize = new Size(560, 580); StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 9 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int h in new[] { 26, 40, 36, 36, 36 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.Controls.Add(new Label { Text = "라이브러리 폴더", AutoSize = true }, 0, 0);
            var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            var path = new TextBox { Text = value.LibraryRoot, Dock = DockStyle.Fill };
            var browse = new Button { Text = "찾아보기", Dock = DockStyle.Fill };
            browse.Click += delegate
            {
                using (var f = new FolderBrowserDialog { Description = "catalog.xml 및 assets가 있는 폴더", SelectedPath = path.Text })
                    if (f.ShowDialog(this) == DialogResult.OK) path.Text = f.SelectedPath;
            };
            folderRow.Controls.Add(path, 0, 0); folderRow.Controls.Add(browse, 1, 0); layout.Controls.Add(folderRow, 0, 1);
            var rotation = new CheckBox { Text = "회전각도 입력 사용 (끄면 0°)", Checked = value.AskRotation, AutoSize = true };
            var device = new CheckBox { Text = "기존 부품의 기본 기기번호 입력 (부품별 설정 우선)", Checked = value.AskDeviceNumber, AutoSize = true };
            var panel = new CheckBox { Text = "판넬번호 입력 사용 (끄면 미지정)", Checked = value.AskPanelNumber, AutoSize = true };
            layout.Controls.Add(rotation, 0, 2); layout.Controls.Add(device, 0, 3); layout.Controls.Add(panel, 0, 4);
            var snap = new CheckBox { Text = "삽입 위치 선택 직전 객체스냅 자동 OFF→ON (기존 설정 유지)", Checked = value.RefreshOsnapBeforePoint, AutoSize = true };
            layout.Controls.Add(snap, 0, 5);
            var batch = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
            batch.Items.AddRange(new object[] { "연속 삽입 (위치를 하나씩 클릭)", "일괄 삽입 (첫 위치만 클릭)" }); batch.SelectedIndex = value.BatchLayoutInsertion ? 1 : 0;
            var columns = new NumericUpDown { Minimum = 1, Maximum = 100, Value = Math.Max(1, Math.Min(100, value.LayoutColumns)), Width = 70 };
            var gap = new NumericUpDown { Minimum = 0.1M, Maximum = 1000000, DecimalPlaces = 1, Increment = 1, Value = double.IsNaN(value.LayoutGapMm) || double.IsInfinity(value.LayoutGapMm) ? 10 : (decimal)Math.Max(0.1, Math.Min(1000000, value.LayoutGapMm)), Width = 100 };
            var insertion = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            insertion.Controls.Add(new Label { Text = "선택 심볼 → 배치 삽입 방식", AutoSize = true }); insertion.Controls.Add(batch);
            var dimensions = new FlowLayoutPanel { AutoSize = true }; dimensions.Controls.Add(new Label { Text = "가로 개수", AutoSize = true }); dimensions.Controls.Add(columns); dimensions.Controls.Add(new Label { Text = "도형 사이 간격(mm)", AutoSize = true }); dimensions.Controls.Add(gap); insertion.Controls.Add(dimensions);
            insertion.Controls.Add(new Label { Text = "기기번호 자연순으로 삽입합니다. 일괄 배치는 오른쪽으로 진행 후 아래 행으로 이어집니다.", AutoSize = true });
            Action updateBatch = () => { columns.Enabled = gap.Enabled = batch.SelectedIndex == 1; }; batch.SelectedIndexChanged += delegate { updateBatch(); }; updateBatch();
            layout.Controls.Add(insertion, 0, 6);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "위치 선택을 시작하기 직전에 켜진 스냅만 잠깐 껐다 켭니다. 위치·각도 입력 중에는 원래 스냅 상태와 종류를 유지합니다.\n일괄 삽입은 현재 UCS 기준입니다.\n부품별 기기번호 설정이 우선하며 폴더 변경은 파일을 이동하지 않습니다.", AutoEllipsis = true }, 0, 7);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Height = 30 };
            var save = new Button { Text = "저장", Height = 30 };
            save.Click += delegate
            {
                try
                {
                    Settings.Save(new UserSettings { LibraryRoot = path.Text.Trim(), AskRotation = rotation.Checked, AskDeviceNumber = device.Checked, AskPanelNumber = panel.Checked,
                        BatchLayoutInsertion = batch.SelectedIndex == 1, LayoutColumns = (int)columns.Value, LayoutGapMm = (double)gap.Value, RefreshOsnapBeforePoint = snap.Checked });
                    DialogResult = DialogResult.OK; Close();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "설정 저장 실패"); }
            };
            buttons.Controls.Add(cancel); buttons.Controls.Add(save); layout.Controls.Add(buttons, 0, 8);
            Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        }
    }
}
