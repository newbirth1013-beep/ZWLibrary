using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;

namespace ZWLibrary
{
    public partial class Plugin
    {
        [CommandMethod("ZLIBPARTLIST")]
        public void PartList()
        {
            Run("ZLIBPARTLIST", (doc, unused) =>
            {
                var selected = doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nPARTLIST로 표시할 관리 블록 선택: " });
                if (selected.Status != PromptStatus.OK) return;
                var input = ReadSelected(doc.Database, selected.Value);
                if (input.Errors.Count > 0) { CadApp.ShowAlertDialog("현재 속성을 확인할 수 없는 블록이 있습니다. 충돌을 해결하고 다시 선택하세요.\n" + string.Join("\n", input.Errors)); return; }
                var result = PartListLogic.Build(input.Instances);
                if (result.Rows.Count == 0) { CadApp.ShowAlertDialog("관리 블록이 없습니다. 제외 " + input.Excluded + "개"); return; }
                double textMm, widthMm;
                if (ShowPartList(result, input.Total, input.Excluded, out textMm, out widthMm))
                    InsertPartListTable(doc, result, textMm, widthMm);
            });
        }
        private static bool ShowPartList(PartListResult result, int total, int excluded, out double textMm, out double widthMm)
        {
            bool insert = false; textMm = 2.5; widthMm = 200;
            using (var form = new Form { Text = "선택 PARTLIST — 블록별 개별 목록", Width = 1000, Height = 650, MinimumSize = new Size(650, 450), StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.Font })
            {
                var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var info = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Height = result.Warnings.Count == 0 ? 45 : 90, ScrollBars = ScrollBars.Vertical,
                    Text = WindowsLines("선택 " + total + "개 / 관리 블록 " + result.Rows.Count + "행 / 일반·비관리·미지원 제외 " + excluded + "개\nBOM 포함 여부와 관계없이 각 블록을 개별 표시합니다.\n" + string.Join("\n", result.Warnings)) };
                root.Controls.Add(info, 0, 0);
                var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize, BackgroundColor = Color.White };
                grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                for (int i = 0; i < PartListLogic.Headers.Length; i++) grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = PartListLogic.Headers[i], FillWeight = i == 0 ? 35 : (i == 2 || i == 4 ? 180 : 90), MinimumWidth = i == 0 ? 40 : 80, SortMode = DataGridViewColumnSortMode.NotSortable });
                foreach (var row in result.Rows) grid.Rows.Add(row.Cast<object>().ToArray());
                root.Controls.Add(grid, 0, 1);
                var controls = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
                var height = new NumericUpDown { Minimum = 0.1M, Maximum = 1000, DecimalPlaces = 2, Value = 2.5M, Width = 75 };
                var width = new NumericUpDown { Minimum = 10, Maximum = 100000, DecimalPlaces = 1, Value = 200, Width = 90 };
                controls.Controls.Add(new Label { Text = "문자 높이(mm)", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }); controls.Controls.Add(height);
                controls.Controls.Add(new Label { Text = "표 전체 폭(mm)", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }); controls.Controls.Add(width);
                var csv = new Button { Text = "CSV 저장", AutoSize = true };
                csv.Click += delegate
                {
                    using (var dialog = new System.Windows.Forms.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "PARTLIST.csv", OverwritePrompt = true })
                        if (dialog.ShowDialog(form) == DialogResult.OK)
                            try { File.WriteAllText(dialog.FileName, PartListLogic.Csv(result), new UTF8Encoding(true)); MessageBox.Show(form, "저장했습니다."); }
                            catch (System.Exception ex) { MessageBox.Show(form, ex.Message); }
                };
                var table = new Button { Text = "도면에 표 삽입", AutoSize = true };
                table.Click += delegate { insert = true; form.DialogResult = DialogResult.OK; };
                var close = new Button { Text = "닫기", AutoSize = true, DialogResult = DialogResult.Cancel };
                controls.Controls.Add(csv); controls.Controls.Add(table); controls.Controls.Add(close); root.Controls.Add(controls, 0, 2);
                form.Controls.Add(root); form.CancelButton = close; CadApp.ShowModalDialog(form);
                textMm = (double)height.Value; widthMm = (double)width.Value;
            }
            return insert;
        }
        private static void InsertPartListTable(Document doc, PartListResult result, double textMm, double widthMm)
        {
            if (!ReferenceEquals(CadApp.DocumentManager.MdiActiveDocument, doc)) throw new InvalidOperationException("도면이 바뀌었습니다. 원래 도면에서 PARTLIST를 다시 실행하세요.");
            double scale;
            if (doc.Database.Insunits == UnitsValue.Undefined)
            {
                var unit = doc.Editor.GetDouble(new PromptDoubleOptions("\n도면 1단위의 길이(mm) <1>: ") { DefaultValue = 1, UseDefaultValue = true, AllowZero = false, AllowNegative = false });
                if (unit.Status != PromptStatus.OK) return; scale = 1 / unit.Value;
            }
            else scale = UnitsConverter.GetConversionFactor(UnitsValue.Millimeters, doc.Database.Insunits);
            if (double.IsInfinity(scale) || double.IsNaN(scale) || scale <= 0) throw new InvalidOperationException("표 단위 변환 오류");
            if (widthMm < textMm * 40) throw new InvalidOperationException("문자 높이에 비해 표 폭이 너무 좁습니다. 전체 폭을 문자 높이의 40배 이상으로 지정하세요.");
            var point = doc.Editor.GetPoint("\nPART LIST 표 삽입 위치: "); if (point.Status != PromptStatus.OK) return;
            if (!ReferenceEquals(CadApp.DocumentManager.MdiActiveDocument, doc)) throw new InvalidOperationException("표 대상 도면이 바뀌었습니다.");
            using (var table = new Table())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                table.SetDatabaseDefaults(doc.Database); table.TableStyle = doc.Database.Tablestyle;
                table.SetSize(result.Rows.Count + 2, 5); table.IsTitleSuppressed = false; table.IsHeaderSuppressed = false;
                double height = textMm * scale, width = widthMm * scale;
                table.HorizontalCellMargin = height * 0.25; table.VerticalCellMargin = height * 0.25;
                double[] fractions = { 0.06, 0.14, 0.30, 0.18, 0.32 };
                for (int c = 0; c < 5; c++) table.SetColumnWidth(c, width * fractions[c]);
                table.MergeCells(CellRange.Create(table, 0, 0, 0, 4)); table.SetTextString(0, 0, "PART LIST");
                for (int c = 0; c < 5; c++) table.SetTextString(1, c, TableText.Literal(PartListLogic.Headers[c]));
                for (int r = 0; r < result.Rows.Count + 2; r++)
                {
                    int lines = 1;
                    for (int c = 0; c < 5; c++)
                    {
                        table.SetTextHeight(r, c, height); table.SetAlignment(r, c, r < 2 || c == 0 ? CellAlignment.MiddleCenter : CellAlignment.MiddleLeft);
                        table.SetGridVisibility(r, c, (short)15, true);
                        if (r >= 2)
                        {
                            int units = Math.Max(2, (int)((width * fractions[c] - height) / (height * 0.65)));
                            string text = TableText.Wrap(result.Rows[r-2][c], units);
                            lines = Math.Max(lines, text.Count(ch => ch == '\n') + 1); table.SetTextString(r, c, TableText.Literal(text));
                        }
                    }
                    table.SetRowHeight(r, height * (lines * 1.65 + 1.2));
                }
                table.Position = point.Value; table.GenerateLayout(); table.TransformBy(doc.Editor.CurrentUserCoordinateSystem);
                var space = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);
                space.AppendEntity(table); tr.AddNewlyCreatedDBObject(table, true); tr.Commit();
            }
            doc.Editor.Regen(); doc.Editor.WriteMessage("\nPART LIST 표 삽입 완료: " + result.Rows.Count + "행 (정적 표)\n");
        }
    }
}
