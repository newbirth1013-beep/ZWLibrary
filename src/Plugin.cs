using System;
using System.Collections.Generic;
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
using ZwSoft.ZwCAD.Windows;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;

[assembly: CommandClass(typeof(ZWLibrary.Plugin))]
[assembly: System.Reflection.AssemblyVersion("0.8.4.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.8.4.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace ZWLibrary
{
    public partial class Plugin
    {
        private static int activeOperations;
        internal static bool IsRunning { get { return activeOperations > 0; } }
        [LispFunction("ZWLIBRUNTIMEPATH")]
        public ResultBuffer RuntimePath(ResultBuffer arguments)
        {
            return new ResultBuffer(new TypedValue((int)LispDataType.Text, typeof(Plugin).Assembly.Location.Replace('\\', '/')));
        }
        private const string RecordKey = "ZWLIB_PART_V1";
        private static PaletteSet palette;
        private static LibraryPanel panel;
        private static GeometryPanel symbolsPanel, layoutsPanel;
        internal static void RefreshPanels()
        { if (panel != null) panel.Reload(); if (symbolsPanel != null) symbolsPanel.Reload(); if (layoutsPanel != null) layoutsPanel.Reload(); }
        private static Document queuedDocument;
        private static string queuedPart;
        private static string queuedCommand;
        public static void Queue(string command, string id)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) throw new InvalidOperationException("도면을 먼저 여세요.");
            if (!string.IsNullOrEmpty(doc.CommandInProgress)) throw new InvalidOperationException("현재 명령을 마친 후 실행하세요.");
            if (queuedDocument != null) throw new InvalidOperationException("앞선 작업이 시작될 때까지 기다려 주세요.");
            queuedDocument = doc; queuedPart = id; queuedCommand = command;
            try { doc.SendStringToExecute(command + " ", true, false, false); }
            catch { ClearQueue(); throw; }
        }
        private static void ClearQueue() { queuedDocument = null; queuedPart = null; queuedCommand = null; }
        private static string Take(string command)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            string id = queuedPart;
            bool valid = queuedDocument == null || (ReferenceEquals(doc, queuedDocument) && queuedCommand == command);
            ClearQueue();
            if (!valid) throw new InvalidOperationException("도면 또는 명령이 바뀌었습니다. 관리 창에서 다시 실행하세요.");
            return id;
        }
        private static void Run(string command, Action<Document, string> action)
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { ClearQueue(); return; }
            activeOperations++;
            try { string id = Take(command); action(doc, id); }
            catch (System.Exception ex) { doc.Editor.WriteMessage("\nZWLibrary: " + ex.Message + "\n"); CadApp.ShowAlertDialog(ex.Message); }
            finally { activeOperations--; RefreshPanels(); }
        }
        [CommandMethod("ZLIB")]
        public void Open()
        {
            Run("ZLIB", (doc, id) =>
            {
                if (palette == null)
                {
                    panel = new LibraryPanel();
                    palette = new PaletteSet("ZWLibrary — 부품 라이브러리", new Guid("ab7323ef-6a91-47be-8136-b1db8e75edb9"));
                    palette.MinimumSize = new Size(440, 560);
                    palette.Size = new Size(720, 780);
                    palette.Add("부품", panel);
                    symbolsPanel = new GeometryPanel(false); layoutsPanel = new GeometryPanel(true);
                    palette.Add("심볼 관리", symbolsPanel); palette.Add("배치도형 관리", layoutsPanel);
                    palette.KeepFocus = false;
                }
                panel.Reload(); symbolsPanel.Reload(); layoutsPanel.Reload(); palette.Visible = true;
                // Apply after showing, because the host restores persisted placement on show.
                palette.Dock = DockSides.None;
                var area = Screen.FromHandle(CadApp.MainWindow.Handle).WorkingArea;
                int width = Math.Min(720, Math.Max(1, area.Width - 40));
                int height = Math.Min(780, Math.Max(1, area.Height - 40));
                palette.MinimumSize = new Size(Math.Min(440, width), Math.Min(560, height));
                palette.Size = new Size(width, height);
                palette.Location = new Point(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2);
            });
        }
        [CommandMethod("ZLIBRESET")]
        public void Reset() { ClearQueue(); }

        [CommandMethod("ZLIBPREVIEW")]
        public void Preview()
        {
            Run("ZLIBPREVIEW", (doc, id) =>
            {
                if (id == null || panel == null) throw new InvalidOperationException("ZLIB 목록에서 부품을 선택하고 도형 미리보기를 누르세요.");
                var store = Settings.Current;
                var part = store.Load().Parts.FirstOrDefault(p => p.Id == id);
                if (part == null) throw new InvalidOperationException("부품이 목록에 없습니다. 새로고침하세요.");
                var data = PreviewData.Read(store.AssetPath(part.Asset));
                panel.SetPreview(part, data, store.Root);
                doc.Editor.WriteMessage("\n간략 2D 미리보기: 생략 " + data.Skipped + "개" + (data.Limited ? " / 표시 한도 초과" : "") + "\n");
            });
        }

        private static void ValidateEntities(ObjectIdCollection ids, Transaction tr, HashSet<ObjectId> visited, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("블록 중첩이 너무 깊습니다.");
            foreach (ObjectId id in ids)
            {
                var entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (entity == null) throw new InvalidOperationException("해석할 수 없는 객체가 있습니다.");
                if (entity.GetType().Name.IndexOf("Proxy", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException("프록시 객체는 이번 시험판에서 등록할 수 없습니다.");
                if (entity is RasterImage || entity is UnderlayReference)
                    throw new InvalidOperationException("외부 이미지/PDF 등 첨부 참조는 이번 시험판에서 등록할 수 없습니다.");
                var br = entity as BlockReference;
                if (br == null) continue;
                if (br is MInsertBlock || br.IsDynamicBlock)
                    throw new InvalidOperationException("동적 블록/MINSERT는 사본에서 일반 정적 블록으로 준비한 후 등록하세요.");
                var block = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                if (block.IsFromExternalReference || block.IsFromOverlayReference)
                    throw new InvalidOperationException("외부참조는 이번 시험판의 등록 대상에서 제외됩니다.");
                if (!visited.Add(block.ObjectId)) continue;
                var nested = new ObjectIdCollection(); foreach (ObjectId child in block) nested.Add(child);
                ValidateEntities(nested, tr, visited, depth + 1);
            }
        }
        private static string[] ReadDeviceNumbers(BlockReference br, Transaction tr)
        {
            var values = new List<string>();
            foreach (ObjectId id in br.AttributeCollection)
            {
                var ar = tr.GetObject(id, OpenMode.ForRead) as AttributeReference;
                if (ar != null && string.Equals((ar.Tag ?? "").Trim(), "UNIT_NO", StringComparison.OrdinalIgnoreCase))
                    {
                    if (ar.IsMTextAttribute) { using (var text = ar.MTextAttribute) values.Add(text.Text); }
                    else values.Add(ar.TextString);
                }
            }
            return values.ToArray();
        }
        private static int ApplyAttributes(BlockReference br, Instance instance, Transaction tr, bool updateDeviceNumber = true)
        {
            int count = 0;
            foreach (ObjectId id in br.AttributeCollection)
            {
                var ar = tr.GetObject(id, OpenMode.ForRead) as AttributeReference;
                string value;
                if (ar == null || !AttributeValues.TryGet(ar.Tag, instance, out value, updateDeviceNumber)) continue;
                ar.UpgradeOpen();
                ar.TextString = value;
                if (ar.IsMTextAttribute) ar.UpdateMTextAttribute();
                ar.AdjustAlignment(br.Database);
                count++;
            }
            return count;
        }
        private static string AskString(Editor editor, string message)
        {
            var r = editor.GetString(new PromptStringOptions(message) { AllowSpaces = true });
            if (r.Status != PromptStatus.OK && r.Status != PromptStatus.None) return null;
            string s = r.Status == PromptStatus.None ? "" : r.StringResult.Trim();
            if (s.Length > 200 || s.Any(char.IsControl)) throw new InvalidOperationException("200자 이내로 입력하세요.");
            return s;
        }
        private static void WriteInstance(BlockReference br, Instance value, Transaction tr)
        {
            if (br.ExtensionDictionary.IsNull) br.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(br.ExtensionDictionary, OpenMode.ForWrite);
            Xrecord record;
            if (dict.Contains(RecordKey)) record = (Xrecord)tr.GetObject(dict.GetAt(RecordKey), OpenMode.ForWrite);
            else { record = new Xrecord(); dict.SetAt(RecordKey, record); tr.AddNewlyCreatedDBObject(record, true); }
            string xml = XmlData.Write(value);
            var values = new List<TypedValue>();
            for (int start = 0; start < xml.Length; start += 200)
                values.Add(new TypedValue((int)DxfCode.Text, xml.Substring(start, Math.Min(200, xml.Length - start))));
            using (var data = new ResultBuffer(values.ToArray())) record.Data = data;
        }
        private static Instance ReadInstance(BlockReference br, Transaction tr)
        {
            if (br.ExtensionDictionary.IsNull) return null;
            var dict = (DBDictionary)tr.GetObject(br.ExtensionDictionary, OpenMode.ForRead);
            if (!dict.Contains(RecordKey)) return null;
            var record = (Xrecord)tr.GetObject(dict.GetAt(RecordKey), OpenMode.ForRead);
            using (var data = record.Data)
            {
                if (data == null) throw new InvalidDataException("부품 데이터가 비어 있습니다.");
                var text = new StringBuilder();
                foreach (TypedValue v in data)
                {
                    if (v.TypeCode != (int)DxfCode.Text) throw new InvalidDataException("부품 데이터 형식 오류");
                    text.Append(v.Value as string);
                }
                var result = XmlData.Read<Instance>(text.ToString());
                if (result == null || result.Schema != 1 || result.Part == null) throw new InvalidDataException("부품 데이터 버전 오류");
                return result;
            }
        }
        [CommandMethod("ZLIBTAG")]
        public void EditInstance()
        {
            Run("ZLIBTAG", (doc, unused) =>
            {
                var opt = new PromptEntityOptions("\n관리 부품 블록 선택: ");
                opt.SetRejectMessage("\n블록을 선택하세요."); opt.AddAllowedClass(typeof(BlockReference), false);
                var r = doc.Editor.GetEntity(opt); if (r.Status != PromptStatus.OK) return;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var br = (BlockReference)tr.GetObject(r.ObjectId, OpenMode.ForRead);
                    var data = ReadInstance(br, tr); if (data == null) throw new InvalidOperationException("이 플러그인으로 삽입한 부품이 아닙니다.");
                    data.Tag = DeviceNumbers.Resolve(data, ReadDeviceNumbers(br, tr));
                    doc.Editor.WriteMessage("\n부품: " + data.Part.Model + " / " + data.Part.Brand + " / " + data.Part.Name + "\n현재 판넬/기기: " + data.Panel + " / " + data.Tag);
                    string tag = AskString(doc.Editor, "\n새 기기번호 (Enter=비움): "); if (tag == null) return;
                    string panelName = AskString(doc.Editor, "\n새 판넬번호 (Enter=비움): "); if (panelName == null) return;
                    var k = new PromptKeywordOptions("\nBOM 포함 [Yes/No] <" + (data.Part.Bom ? "Yes" : "No") + ">: ");
                    k.Keywords.Add("Yes"); k.Keywords.Add("No"); k.Keywords.Default = data.Part.Bom ? "Yes" : "No"; k.AllowNone = true;
                    var answer = doc.Editor.GetKeywords(k); if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
                    data.Tag = tag; data.Panel = panelName; data.DeviceNumberInputEnabled = true;
                    if (answer.Status == PromptStatus.OK) data.Part.Bom = answer.StringResult == "Yes";
                    br.UpgradeOpen();
                    int attributesUpdated = ApplyAttributes(br, data, tr);
                    if (data.GeometryKind != null || !string.IsNullOrEmpty(data.LayoutSourceHandle))
                        ApplyLayoutAttributes(br, data, tr, new HashSet<ObjectId>(), 0);
                    WriteInstance(br, data, tr); tr.Commit();
                    doc.Editor.WriteMessage("\n속성 자동 반영: " + attributesUpdated + "개\n");
                }
            });
        }
        [CommandMethod("ZLIBBOM")]
        public void Bom()
        {
            Run("ZLIBBOM", (doc, unused) =>
            {
                var r = doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nBOM으로 집계할 최상위 부품 선택: " });
                if (r.Status != PromptStatus.OK) return;
                var selected = ReadSelected(doc.Database, r.Value);
                var result = BomLogic.Build(selected.Instances); result.Errors.AddRange(selected.Errors);
                string summary = "선택 " + selected.Total + "개 / 관리 블록 " + selected.Instances.Count + "개 / 일반·비관리·미지원 제외 " + selected.Excluded + "개 / BOM 제외 " + result.Excluded + "개\n";
                summary += "현재 모델·제조사·규격이 같은 선택 블록의 수량을 합산합니다.\n심볼·배치는 각각 집계하고 같은 판넬의 기기번호 중복은 CSV 저장을 차단합니다.\n";
                if (result.Rows.Count == 0 && result.Errors.Count == 0) { CadApp.ShowAlertDialog(summary + "집계할 부품이 없습니다."); return; }
                ShowReport(summary, result);
            });
        }
        private static void ShowReport(string summary, BomResult result)
        {
            bool canSave = result.Errors.Count == 0 && result.Rows.Count > 0;
            string csv = canSave ? BomLogic.Csv(result) : null;
            using (var form = new Form
            {
                Text = "선택 BOM — 표 검토 후 CSV 저장 (v0.8.4)",
                Width = 1100, Height = 650, MinimumSize = new Size(800, 480),
                StartPosition = FormStartPosition.CenterParent,
                AutoScaleMode = AutoScaleMode.Font, Font = SystemFonts.MessageBoxFont
            })
            {
                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
                    Padding = new Padding(10)
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, result.Errors.Count > 0 ? 125 : 0));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
                var info = new TextBox
                {
                    Multiline = true, ReadOnly = true, WordWrap = true,
                    ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.None, BackColor = SystemColors.Control,
                    Text = WindowsLines(summary), TabStop = false
                };
                layout.Controls.Add(info, 0, 0);
                var errors = new TextBox
                {
                    Multiline = true, ReadOnly = true, WordWrap = true,
                    ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(255, 244, 240), ForeColor = Color.DarkRed,
                    Visible = result.Errors.Count > 0,
                    Text = WindowsLines("오류를 해결한 후 다시 생성하세요. 아래 표는 확인용이며 저장할 수 없습니다.\n" + string.Join("\n", result.Errors))
                };
                layout.Controls.Add(errors, 0, 1);
                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill, ReadOnly = true,
                    AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                    AllowUserToOrderColumns = false, RowHeadersVisible = false,
                    AutoGenerateColumns = false, BackgroundColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                    ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
                };
                grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                grid.DefaultCellStyle.Padding = new Padding(5, 4, 5, 4);
                grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(242, 247, 252);
                grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                string[] titles = { "부품코드", "품명", "제조사", "모델", "규격", "단위", "수량" };
                int[] weights = { 160, 150, 100, 160, 140, 60, 70 };
                int[] minimums = { 130, 110, 85, 125, 105, 55, 65 };
                for (int i = 0; i < titles.Length; i++)
                {
                    var column = new DataGridViewTextBoxColumn
                    {
                        HeaderText = titles[i], Name = "C" + i,
                        FillWeight = weights[i], MinimumWidth = minimums[i],
                        SortMode = DataGridViewColumnSortMode.NotSortable
                    };
                    if (i == 5) column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    if (i == 6)
                    {
                        column.ValueType = typeof(int);
                        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                        column.DefaultCellStyle.Format = "N0";
                    }
                    grid.Columns.Add(column);
                    if (i == 0) column.Visible = false;
                }
                long total = 0;
                foreach (var row in result.Rows)
                {
                    int quantity = int.Parse(row[6], System.Globalization.CultureInfo.InvariantCulture);
                    grid.Rows.Add(row[0], row[1], row[2], row[3], row[4], row[5], quantity);
                    total += quantity;
                }
                layout.Controls.Add(grid, 0, 2);
                var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
                footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
                footer.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                    Text = (canSave ? "" : "검토용 · ") + result.Rows.Count + "개 품목  /  합계 " + total.ToString("N0") + "개"
                }, 0, 0);
                var button = new Button
                {
                    Text = canSave ? "CSV 저장" : "오류로 저장 불가",
                    Enabled = canSave, Dock = DockStyle.Fill, Margin = new Padding(4)
                };
                button.Click += delegate
                {
                    using (var f = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "BOM.csv", OverwritePrompt = true })
                    {
                        if (f.ShowDialog(form) != DialogResult.OK) return;
                        try { File.WriteAllText(f.FileName, csv, new UTF8Encoding(true)); MessageBox.Show(form, "저장했습니다."); }
                        catch (System.Exception ex) { MessageBox.Show(form, ex.Message); }
                    }
                };
                footer.Controls.Add(button, 1, 0);
                layout.Controls.Add(footer, 0, 3);
                form.Controls.Add(layout);
                CadApp.ShowModalDialog(form);
            }
        }
        private static string WindowsLines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }
    }
}
