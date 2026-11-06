// Standalone WinForms layout checks. CAD adapters below are intentionally isolated.
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ZWLibrary;
class UiSmokeTests
{
    static int count;
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
    static System.Collections.Generic.IEnumerable<Control> Children(Control c)
    { foreach (Control child in c.Controls) { yield return child; foreach (var nested in Children(child)) yield return nested; } }
    [STAThread]
    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception ex) { Console.WriteLine("UI FAIL: " + ex.Message); return 1; }
    }
    static void Run(string[] args)
    {
        Application.EnableVisualStyles();
        string folder = Path.GetFullPath(args[0]); Directory.CreateDirectory(folder);
        string data = Path.Combine(folder, "fixture-" + Guid.NewGuid().ToString("N")); Settings.TestRoot = data;
        try
        {
            var store = Settings.Current;
            store.Save(0, c => { for (int i = 0; i < 20; i++) { var p = new Part { Model = "Model-" + i, Brand = "Maker", Category = "Breaker", DeviceNumberPrefix = "MCCB" }; PartEditing.Prepare(p); c.Parts.Add(p); } });
            store.Save(store.Load().Revision, c => { c.Symbols.Add(new GeometryAsset { Name = "Alpha", Category = "Control", Asset = "alpha.dwg" }); c.Symbols.Add(new GeometryAsset { Name = "Beta", Category = "Power", Asset = "beta.dwg" }); });
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            using (var panel = new LibraryPanel())
            {
                panel.Font = new Font("Malgun Gothic", 9f * scale);
                panel.Size = new Size((int)(720 * scale), (int)(780 * scale));
                panel.CreateControl(); panel.PerformLayout();
                using (var bitmap = new Bitmap(panel.Width, panel.Height)) { panel.DrawToBitmap(bitmap, panel.ClientRectangle); bitmap.Save(Path.Combine(folder, "parts-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png")); }
                var root = (TableLayoutPanel)panel.Controls[0]; var actions = root.GetControlFromPosition(0, 1); var split = (SplitContainer)root.GetControlFromPosition(0, 2);
                var grid = (DataGridView)split.Panel1.Controls[0];
                Check(split.Top - actions.Bottom <= 12 * scale, "part list begins directly below drawing actions at font scale " + scale);
                Check(grid.Height >= 200 * scale, "part list receives remaining window space at font scale " + scale);
                Check(!grid.Columns.Cast<DataGridViewColumn>().Any(c => c.DataPropertyName == "Code"), "internal code hidden at font scale " + scale);
                Check(grid.Columns.Cast<DataGridViewColumn>().Any(c => c.DataPropertyName == "DeviceNumberPrefix"), "part list displays device number prefix at font scale " + scale);
                Check((string)grid.Rows[0].Cells[2].Value == "MCCB", "part list binds saved prefix value at font scale " + scale);
                Check(Children(panel).OfType<PreviewCanvas>().All(c => c.HighlightInsertionPoint), "part previews highlight insertion points at font scale " + scale);
                var buttons = Children(panel).OfType<Button>().ToList();
                Check(buttons.Single(b => b.Text == "신규 등록").Enabled, "new registration available without symbols at font scale " + scale);
                Check(!buttons.Single(b => b.Text == "심볼 삽입").Enabled && !buttons.Single(b => b.Text == "배치도형 삽입").Enabled, "missing geometry disables insertion at font scale " + scale);
            }
            using (var editor = new PartEditor(new Part(), store.Load()))
            {
                editor.CreateControl(); editor.PerformLayout();
                foreach (var c in Children(editor)) { var handle = c.Handle; }
                using (var bitmap = new Bitmap(editor.ClientSize.Width, editor.ClientSize.Height)) { foreach (Control c in editor.Controls) c.DrawToBitmap(bitmap, c.Bounds); bitmap.Save(Path.Combine(folder, "part-editor.png")); }
                var controls = Children(editor).ToList();
                Check(controls.OfType<Label>().Any(c => c.Text == "모델 *") && controls.OfType<Label>().Any(c => c.Text == "제조사 *"), "editor marks model and maker as required");
                Check(!controls.OfType<Label>().Any(c => c.Text.Contains("부품코드")), "editor hides internal code");
                Check(controls.OfType<ComboBox>().All(c => c.SelectedIndex == 0), "both geometry connections default to unregistered");
                var save = controls.OfType<Button>().Single(c => c.Text == "저장");
                var fields = controls.OfType<TextBox>().ToList(); fields[0].Text = "Model-New"; fields[1].Text = "Maker-New";
                fields[5].Text = "MCCB";
                var numbering = controls.OfType<CheckBox>().Single(c => c.Text == "삽입 시 기기번호 입력");
                Check(numbering.Checked && fields[5].Enabled, "number input enabled for new part");
                numbering.Checked = false;
                Check(!fields[5].Enabled, "number prefix disabled when number input is OFF");
                typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(save, new object[] { EventArgs.Empty });
                Check(editor.DialogResult == DialogResult.OK && editor.Value.Code == "P-" + editor.Value.Id, "editor saves model and maker without name or geometry");
                Check(editor.Value.NumberingEnabled == false && editor.Value.DeviceNumberPrefix == "MCCB", "editor saves per part OFF without losing prefix");
                using (var reopened = new PartEditor(editor.Value, store.Load()))
                {
                    Check(!Children(reopened).OfType<CheckBox>().Single(c => c.Text == "삽입 시 기기번호 입력").Checked && Children(reopened).OfType<TextBox>().Last().Text == "MCCB", "editor restores stored numbering options");
                }
            }
            var catalog = store.Load(); var geometry = new GeometryAsset { Name = "Layout", Asset = "layout.dwg", Category = "Breaker" }; catalog.Layouts.Add(geometry); catalog.Parts[0].LayoutId = geometry.Id;
            using (var picker = new GeometryPicker(catalog, true, geometry.Id))
            {
                picker.CreateControl(); picker.PerformLayout();
                foreach (var c in Children(picker)) { var handle = c.Handle; }
                using (var bitmap = new Bitmap(picker.ClientSize.Width, picker.ClientSize.Height)) { foreach (Control c in picker.Controls) c.DrawToBitmap(bitmap, c.Bounds); bitmap.Save(Path.Combine(folder, "geometry-picker.png")); }
                var grid = Children(picker).OfType<DataGridView>().Single(); var query = Children(picker).OfType<TextBox>().Single();
                Check(grid.Rows.Count == 1, "picker shows matching geometry"); query.Text = "missing";
                Check(grid.Rows.Count == 0 && !Children(picker).OfType<Button>().Single(b => b.Text == "선택").Enabled, "picker disables selection for empty results");
                query.Clear(); Check(grid.Rows.Count == 1, "picker restores matches after clearing query");
            }
            using (var management = new GeometryPanel(false))
            {
                management.Size = new Size(720, 780); management.CreateControl(); management.PerformLayout();
                using (var bitmap = new Bitmap(management.Width, management.Height)) { management.DrawToBitmap(bitmap, management.ClientRectangle); bitmap.Save(Path.Combine(folder, "geometry-management.png")); }
                var grid = Children(management).OfType<DataGridView>().Single();
                Check(!grid.Columns.Cast<DataGridViewColumn>().Any(c => c.DataPropertyName.StartsWith("Base")), "geometry management hides original base coordinates");
                Check(Children(management).OfType<PreviewCanvas>().Single().HighlightInsertionPoint, "geometry management highlights insertion point");
                var query = Children(management).OfType<TextBox>().Single(); var category = Children(management).OfType<ComboBox>().Single();
                Check(grid.Rows.Count == 2 && category.Items.Count == 3, "management populates geometry categories");
                category.SelectedIndex = 1; Check(grid.Rows.Count == 1, "management filters category");
                query.Text = "Beta"; Check(grid.Rows.Count == 0, "management combines query and category");
                category.SelectedIndex = 0; Check(grid.Rows.Count == 1, "management query finds other category after reset");
                management.Reload(); Check(grid.Rows.Count == 1, "management preserves query on reload");
            }
            using (var settings = new SettingsDialog())
            {
                settings.CreateControl(); settings.PerformLayout(); foreach (var c in Children(settings)) { var handle = c.Handle; }
                using (var bitmap = new Bitmap(settings.ClientSize.Width, settings.ClientSize.Height)) { foreach (Control c in settings.Controls) c.DrawToBitmap(bitmap, c.Bounds); bitmap.Save(Path.Combine(folder, "settings.png")); }
                var mode = Children(settings).OfType<ComboBox>().Single(); var numbers = Children(settings).OfType<NumericUpDown>().ToList();
                var snap = Children(settings).OfType<CheckBox>().Single(c => c.Text.StartsWith("삽입 위치 선택 직전 객체스냅"));
                Check(snap.Checked, "settings enables before-point snap refresh by default"); snap.Checked = false;
                Check(mode.SelectedIndex == 0 && numbers.All(c => !c.Enabled), "continuous mode disables batch controls");
                mode.SelectedIndex = 1; Check(numbers.All(c => c.Enabled), "batch mode enables columns and gap controls");
                numbers[0].Value = 3; numbers[1].Value = 12.5M;
                var save = Children(settings).OfType<Button>().Single(c => c.Text == "저장");
                typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(save, new object[] { EventArgs.Empty });
                Check(Settings.Saved.BatchLayoutInsertion && Settings.Saved.LayoutColumns == 3 && Settings.Saved.LayoutGapMm == 12.5, "settings dialog saves insertion mode columns and millimeter gap");
                Check(!Settings.Saved.RefreshOsnapBeforePoint, "settings can disable automatic snap refresh");
            }
            using (var preview = new PreviewCanvas { Size = new Size(300, 200), HighlightInsertionPoint = true })
            {
                var previewData = new PreviewData { MinX = 10, MinY = 10, MaxX = 30, MaxY = 30 };
                previewData.Lines.Add(new PreviewData.Stroke { Points = new[] { new PointF(10, 10), new PointF(30, 30) } }); preview.Data = previewData;
                using (var bitmap = new Bitmap(preview.Width, preview.Height))
                {
                    preview.DrawToBitmap(bitmap, preview.ClientRectangle); bitmap.Save(Path.Combine(folder, "insertion-point.png"));
                    int orange = 0;
                    for (int y = 30; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) { var c = bitmap.GetPixel(x, y); if (c.R > 220 && c.G > 100 && c.G < 200 && c.B < 60) orange++; }
                    Check(orange > 30, "off geometry insertion origin is visible as orange marker");
                }
            }
            Console.WriteLine("UI TOTAL PASS: " + count);
        }
        finally { if (Directory.Exists(data)) Directory.Delete(data, true); }
    }
}
namespace ZWLibrary
{
    internal sealed class PreviewData
    {
        internal sealed class Stroke { public PointF[] Points; }
        internal sealed class Caption { public string Text = ""; public RectangleF Bounds = RectangleF.Empty; }
        internal readonly System.Collections.Generic.List<Stroke> Lines = new System.Collections.Generic.List<Stroke>();
        internal readonly System.Collections.Generic.List<Caption> Texts = new System.Collections.Generic.List<Caption>();
        internal double MinX, MinY, MaxX, MaxY;
        internal bool HasGeometry { get { return Lines.Count + Texts.Count > 0; } }
    }
    internal sealed class AutoPreviewSession : IDisposable
    { internal AutoPreviewSession(PreviewCanvas c) { } internal void Request(string path, string message, Func<bool> current) { } public void Dispose() { } }
    internal static class PreviewCache { internal static void Clear() { } }
    public static class Plugin { internal static bool IsRunning; public static void Queue(string command, string id) { } internal static void RefreshPanels() { } }
    public static class Settings { public static string TestRoot; public static UserSettings Saved; public static UserSettings Load() { return new UserSettings { LibraryRoot = TestRoot }; } public static void Save(UserSettings value) { Saved = value; } public static Store Current { get { return new Store(TestRoot); } } }
}
namespace ZwSoft.ZwCAD.ApplicationServices
{
    public static class Application
    {
        public sealed class TestDocument { public string CommandInProgress = ""; }
        public sealed class TestManager { public TestDocument MdiActiveDocument = new TestDocument(); }
        public static TestManager DocumentManager = new TestManager();
        public static DialogResult ShowModalDialog(Form form) { return DialogResult.Cancel; }
    }
}
