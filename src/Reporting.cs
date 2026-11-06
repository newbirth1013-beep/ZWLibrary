using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
namespace ZWLibrary
{
    public static class InsertionOsnapRefresh
    {
        public static void Apply(bool enabled, Func<int> read, Action<int> write)
        {
            if (!enabled) return;
            int original = read();
            if ((original & 16384) != 0 || (original & 16383) == 0) return;
            try { write(original | 16384); }
            finally { write(original); }
        }
    }
    public sealed class LayoutBounds
    {
        public double MinX, MinY, MaxX, MaxY;
    }
    public sealed class LayoutOffset
    {
        public double X, Y;
    }
    public static class LayoutPacking
    {
        public static void Validate(UserSettings options)
        {
            if (options.LayoutColumns < 1 || options.LayoutColumns > 100 || double.IsNaN(options.LayoutGapMm) || double.IsInfinity(options.LayoutGapMm) || options.LayoutGapMm <= 0 || options.LayoutGapMm > 1000000)
                throw new InvalidDataException("일괄 삽입 가로 개수는 1~100, 간격은 0 초과~1,000,000 mm로 설정하세요.");
        }
        public static List<LayoutOffset> Plan(IList<LayoutBounds> bounds, int columns, double gap)
        {
            if (columns < 1 || columns > 100 || double.IsNaN(gap) || double.IsInfinity(gap) || gap <= 0) throw new InvalidDataException("배치 개수/간격 오류");
            var result = new List<LayoutOffset>(); if (bounds.Count == 0) return result;
            foreach (var b in bounds)
                if (b == null || new[] { b.MinX, b.MinY, b.MaxX, b.MaxY }.Any(x => double.IsNaN(x) || double.IsInfinity(x)) || b.MaxX < b.MinX || b.MaxY < b.MinY) throw new InvalidDataException("배치도형 크기를 확인할 수 없습니다.");
            double width = bounds.Max(b => b.MaxX - b.MinX) + gap, height = bounds.Max(b => b.MaxY - b.MinY) + gap;
            for (int i = 0; i < bounds.Count; i++)
                result.Add(new LayoutOffset { X = (i % columns) * width + bounds[0].MinX - bounds[i].MinX, Y = -(i / columns) * height + bounds[0].MaxY - bounds[i].MaxY });
            if (result.Any(p => double.IsNaN(p.X) || double.IsInfinity(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.Y))) throw new InvalidDataException("배치 좌표 범위 초과");
            return result;
        }
    }
    public static class PartEditing
    {
        public static void Prepare(Part value)
        {
            if (string.IsNullOrWhiteSpace(value.Model) || string.IsNullOrWhiteSpace(value.Brand))
                throw new InvalidDataException("모델과 제조사는 필수입니다.");
            if (string.IsNullOrWhiteSpace(value.Code)) value.Code = "P-" + value.Id;
            Store.Validate(value);
        }
    }
    public sealed class GeometryChoice
    {
        public GeometryAsset Geometry { get; set; }
        public string Name { get { return Geometry.Name; } }
        public string Category { get { return Geometry.Category; } }
        public string FileName { get { return Geometry.Asset; } }
        public string Makers { get; set; }
        public string SearchText { get; set; }
        public string[] MakerValues { get; set; }
    }
    public static class GeometrySearch
    {
        public static List<GeometryChoice> Choices(Catalog catalog, bool layout)
        {
            return (layout ? catalog.Layouts : catalog.Symbols).Select(g =>
            {
                var parts = catalog.Parts.Where(p => (layout ? p.LayoutId : p.SymbolId) == g.Id).ToList();
                var makers = parts.Select(p => BomLogic.Clean(p.Brand)).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToArray();
                return new GeometryChoice { Geometry = g, MakerValues = makers, Makers = makers.Length == 0 ? "미지정" : string.Join(" / ", makers),
                    SearchText = string.Join(" ", new[] { g.Name, g.Category, g.Asset }.Concat(parts.SelectMany(p => new[] { p.Name, p.Brand, p.Model, p.Spec }))) };
            }).OrderBy(x => x.Name).ToList();
        }
        public static List<GeometryChoice> Filter(System.Collections.Generic.IEnumerable<GeometryChoice> choices, string query, string category, string maker)
        {
            return choices.Where(x => (category == null || string.Equals(BomLogic.Clean(x.Category), category, StringComparison.OrdinalIgnoreCase)) &&
                (maker == null || (maker.Length == 0 ? x.MakerValues.Length == 0 : x.MakerValues.Contains(maker, StringComparer.OrdinalIgnoreCase))) &&
                x.SearchText.IndexOf((query ?? "").Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
    }
    public static class AssetNames
    {
        public static void Validate(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name != name.Trim() || name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".IndexOf(c) >= 0) || !name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(name) != name)
                throw new InvalidDataException("DWG 파일명만 입력하세요. 경로·제어문자·<>:\"/\\|?*는 사용할 수 없습니다.");
            string stem = Path.GetFileNameWithoutExtension(name).TrimEnd(' ', '.'); string device = stem.Split('.')[0].ToUpperInvariant();
            if (stem.Length == 0 || new[] { "CON", "PRN", "AUX", "NUL" }.Contains(device) || (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && device[3] >= '1' && device[3] <= '9'))
                throw new InvalidDataException("Windows 예약 파일명 또는 빈 파일명입니다.");
        }
    }
    public static class CurrentAttributes
    {
        public static readonly string[] Tags = { "UNIT_NO", "UNIT_MODEL", "UNIT_MAKER" };
        public static string[] Values(string tag, IDictionary<string, List<string>> outer, IDictionary<string, List<string>> nested)
        {
            List<string> values; bool top = outer.TryGetValue(tag, out values);
            if (!top && !nested.TryGetValue(tag, out values)) return null;
            if (top && tag == "UNIT_NO" && values.Count > 1) throw new InvalidDataException("최상위 UNIT_NO 속성이 여러 개입니다. 하나만 남기세요.");
            var distinct = values.Select(v => (v ?? "").Trim()).Distinct(StringComparer.Ordinal).ToArray();
            if (distinct.Length > 1) throw new InvalidDataException((top ? "최상위" : "중첩") + " " + tag + " 속성값이 서로 다릅니다. 값을 통일하거나 최상위 속성으로 지정하세요.");
            return distinct;
        }
        public static Instance Read(Instance stored, IDictionary<string, List<string>> outer, IDictionary<string, List<string>> nested)
        {
            var data = stored.Copy(); var number = Values("UNIT_NO", outer, nested);
            bool unknownOriginal = !outer.ContainsKey("UNIT_NO") && data.DeviceNumberInputEnabled == false && data.OriginalDeviceNumber == null && string.IsNullOrWhiteSpace(data.Tag);
            data.Tag = unknownOriginal ? "" : DeviceNumbers.Resolve(data, number ?? new string[0]);
            var model = Values("UNIT_MODEL", outer, nested); var maker = Values("UNIT_MAKER", outer, nested);
            if (model != null) data.Part.Model = model[0]; if (maker != null) data.Part.Brand = maker[0];
            data.CurrentAttributesApplied = true;
            // No specification tag mapping existed; never infer Spec from current/rating tags.
            return data;
        }
    }
    public sealed class NaturalNumberComparer : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            a = BomLogic.Clean(a); b = BomLogic.Clean(b);
            if (a.Length == 0 || b.Length == 0) return a.Length == b.Length ? 0 : (a.Length == 0 ? 1 : -1);
            int x = 0, y = 0;
            while (x < a.Length && y < b.Length)
            {
                if (a[x] >= '0' && a[x] <= '9' && b[y] >= '0' && b[y] <= '9')
                {
                    int xe = x, ye = y; while (xe < a.Length && a[xe] >= '0' && a[xe] <= '9') xe++; while (ye < b.Length && b[ye] >= '0' && b[ye] <= '9') ye++;
                    string nx = a.Substring(x, xe-x).TrimStart('0'), ny = b.Substring(y, ye-y).TrimStart('0');
                    int compare = nx.Length.CompareTo(ny.Length); if (compare == 0) compare = string.CompareOrdinal(nx, ny);
                    if (compare != 0) return compare; x = xe; y = ye;
                }
                else { int compare = char.ToUpperInvariant(a[x]).CompareTo(char.ToUpperInvariant(b[y])); if (compare != 0) return compare; x++; y++; }
            }
            return (a.Length-x).CompareTo(b.Length-y);
        }
    }
    public sealed class PartListResult
    {
        public readonly List<string[]> Rows = new List<string[]>();
        public readonly List<string> Warnings = new List<string>();
    }
    public static class PartListLogic
    {
        public static readonly string[] Headers = { "순번", "기기번호", "모델", "제조사", "규격" };
        public static PartListResult Build(IEnumerable<Instance> input)
        {
            var result = new PartListResult();
            var list = input.Select((data, index) => new { Data = data, Index = index }).Where(x => x.Data != null && x.Data.Schema == 1 && x.Data.Part != null).OrderBy(x => x.Data.Tag, new NaturalNumberComparer()).ThenBy(x => x.Index).ToList();
            foreach (var x in list) result.Rows.Add(new[] { (result.Rows.Count + 1).ToString(CultureInfo.InvariantCulture), BomLogic.Clean(x.Data.Tag), BomLogic.Clean(x.Data.Part.Model), BomLogic.Clean(x.Data.Part.Brand), BomLogic.Clean(x.Data.Part.Spec) });
            foreach (var g in list.Where(x => !string.IsNullOrWhiteSpace(x.Data.Tag)).GroupBy(x => Part.Pack(BomLogic.Clean(x.Data.Panel).ToUpperInvariant(), BomLogic.Clean(x.Data.Tag).ToUpperInvariant())))
                if (g.Count() > 1) result.Warnings.Add("기기번호 중복: " + g.First().Data.Panel + " / " + g.First().Data.Tag + " (" + g.Count() + "행 유지)");
            return result;
        }
        public static string Csv(PartListResult result)
        { var text = new StringBuilder(); text.AppendLine(string.Join(",", Headers.Select(BomLogic.CsvCell))); foreach (var row in result.Rows) text.AppendLine(string.Join(",", row.Select(BomLogic.CsvCell))); return text.ToString(); }
    }
    public static class TableText
    {
        public static string Literal(string value)
        { return (value ?? "").Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("%", "\\U+0025").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\P"); }
        public static string Wrap(string value, int maxUnits)
        {
            var result = new StringBuilder(); int width = 0;
            foreach (char c in (value ?? "").Replace("\r\n", "\n").Replace("\r", "\n"))
            { if (c == '\n') { result.Append(c); width = 0; continue; } int units = c > 255 ? 2 : 1; if (width > 0 && width + units > maxUnits) { result.Append('\n'); width = 0; } result.Append(c); width += units; }
            return result.ToString();
        }
    }
}
