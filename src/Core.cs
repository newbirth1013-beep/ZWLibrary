using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace ZWLibrary
{
    public class UserSettings
    {
        public string LibraryRoot { get; set; }
        public bool AskRotation { get; set; }
        public bool AskDeviceNumber { get; set; }
        public bool AskPanelNumber { get; set; }
        public bool BatchLayoutInsertion { get; set; }
        public int LayoutColumns { get; set; }
        public double LayoutGapMm { get; set; }
        [XmlElement("RefreshOsnapAfterInsertion")]
        public bool RefreshOsnapBeforePoint { get; set; }
        public UserSettings() { AskRotation = AskDeviceNumber = AskPanelNumber = true; LayoutColumns = 5; LayoutGapMm = 10; RefreshOsnapBeforePoint = true; }
    }
    public static class SettingsFile
    {
        public static UserSettings Load(string folder)
        {
            string path = Path.Combine(folder, "settings.xml");
            if (File.Exists(path))
            {
                var value = XmlData.Read<UserSettings>(File.ReadAllText(path));
                if (value == null || string.IsNullOrWhiteSpace(value.LibraryRoot)) throw new InvalidDataException("설정 파일이 손상되었습니다.");
                return value;
            }
            string legacy = Path.Combine(folder, "location.txt");
            string root = File.Exists(legacy) ? File.ReadAllText(legacy).Trim() : Path.Combine(folder, "Data");
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidDataException("라이브러리 위치 설정이 비어 있습니다.");
            return new UserSettings { LibraryRoot = root };
        }
        public static void Save(string folder, UserSettings value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.LibraryRoot)) throw new InvalidDataException("라이브러리 폴더를 지정하세요.");
            LayoutPacking.Validate(value);
            var store = new Store(value.LibraryRoot); store.Load();
            value.LibraryRoot = store.Root;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "settings.xml"), temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temp, XmlData.Write(value), Encoding.Unicode);
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public class Part
    {
        public string Id { get; set; }
        public int Revision { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Brand { get; set; }
        public string Model { get; set; }
        public string Spec { get; set; }
        public string Category { get; set; }
        public bool? NumberingEnabled { get; set; }
        public string DeviceNumberPrefix { get; set; }
        public string View { get; set; }
        public string Asset { get; set; }
        public string LayoutAsset { get; set; }
        public string SymbolId { get; set; }
        public string LayoutId { get; set; }
        [XmlIgnore] public string SymbolName { get; set; }
        [XmlIgnore] public string LayoutName { get; set; }
        [XmlIgnore] public string LayoutStatus { get { return string.IsNullOrEmpty(LayoutAsset) ? "미등록" : "등록됨"; } }
        public bool Bom { get; set; }
        public bool Active { get; set; }
        public Part() { Id = Guid.NewGuid().ToString("N"); Revision = 1; Active = true; Bom = true; }
        public Part Copy() { return (Part)MemberwiseClone(); }
        // Legacy fields are accepted on read; ID-linked records write only canonical references.
        public bool ShouldSerializeAsset() { return string.IsNullOrEmpty(SymbolId); }
        public bool ShouldSerializeLayoutAsset() { return string.IsNullOrEmpty(LayoutId); }
        public string Signature { get { return Pack(Name, Brand, Model, Spec); } }
        public static string Pack(params string[] values)
        {
            return string.Concat(values.Select(s => (s ?? "").Length.ToString(CultureInfo.InvariantCulture) + ":" + (s ?? "")));
        }
    }
    public static class AttributeValues
    {
        // Only these exact tags are mapped; unrelated attributes retain their values.
        public static bool TryGet(string attributeTag, Instance instance, out string value, bool updateDeviceNumber = true)
        {
            value = null;
            if (instance == null || instance.Part == null) return false;
            switch ((attributeTag ?? "").Trim().ToUpperInvariant())
            {
                case "UNIT_NO": if (!updateDeviceNumber) return false; value = instance.Tag ?? ""; return true;
                case "UNIT_PANEL": value = instance.Panel ?? ""; return true;
                case "UNIT_MODEL": value = instance.Part.Model; break;
                case "UNIT_MAKER": value = instance.Part.Brand; break;
                default: return false;
            }
            // Missing catalog data must not erase a manufacturer's existing label.
            if (instance.CurrentAttributesApplied) { value = value ?? ""; return true; }
            return !string.IsNullOrWhiteSpace(value);
        }
    }
    public class Catalog
    {
        public int Schema { get; set; }
        public int Revision { get; set; }
        public List<Part> Parts { get; set; }
        public List<GeometryAsset> Symbols { get; set; }
        public List<GeometryAsset> Layouts { get; set; }
        public Catalog() { Schema = 1; Parts = new List<Part>(); Symbols = new List<GeometryAsset>(); Layouts = new List<GeometryAsset>(); }
    }
    public class GeometryAsset
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Asset { get; set; }
        public double BaseX { get; set; }
        public double BaseY { get; set; }
        public double BaseZ { get; set; }
        public double SourceUnitMm { get; set; }
        public string StoredUnit { get; set; }
        public GeometryAsset() { Id = Guid.NewGuid().ToString("N"); SourceUnitMm = 1; StoredUnit = "mm"; }
        public GeometryAsset Copy() { return (GeometryAsset)MemberwiseClone(); }
        public override string ToString() { return Name; }
    }
    public static class CatalogModel
    {
        // Deterministic one-to-one legacy conversion; never infer shared geometry.
        public static void Upgrade(Catalog c)
        {
            if (c == null || (c.Schema != 1 && c.Schema != 2) || c.Parts == null || c.Symbols == null || c.Layouts == null)
                throw new InvalidDataException("지원하지 않거나 손상된 카탈로그입니다.");
            foreach (var p in c.Parts)
            {
                if (p == null) throw new InvalidDataException("손상된 부품입니다.");
                if (string.IsNullOrEmpty(p.SymbolId) && !string.IsNullOrEmpty(p.Asset))
                {
                    p.SymbolId = LegacyGeometryId(p.Id, false);
                    if (!c.Symbols.Any(g => g.Id == p.SymbolId)) c.Symbols.Add(new GeometryAsset { Id = p.SymbolId, Name = p.Name, Category = p.Category, Asset = p.Asset });
                }
                if (string.IsNullOrEmpty(p.LayoutId) && !string.IsNullOrEmpty(p.LayoutAsset))
                {
                    p.LayoutId = LegacyGeometryId(p.Id, true);
                    if (!c.Layouts.Any(g => g.Id == p.LayoutId)) c.Layouts.Add(new GeometryAsset { Id = p.LayoutId, Name = p.Name + " 배치", Category = p.Category, Asset = p.LayoutAsset });
                }
            }
            c.Schema = 2;
        }
        public static string LegacyGeometryId(string partId, bool layout)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes("ZWLibrary/v0.4/" + (layout ? "layout/" : "symbol/") + partId));
                var id = new byte[16]; Array.Copy(bytes, id, id.Length); return new Guid(id).ToString("N");
            }
        }
        public static GeometryAsset Geometry(Catalog c, Part p, bool layout)
        {
            string id = layout ? p.LayoutId : p.SymbolId;
            var value = (layout ? c.Layouts : c.Symbols).SingleOrDefault(g => g.Id == id);
            if (value == null) throw new InvalidDataException(layout ? "해당 부품에 배치도형을 등록하거나 연결하세요." : "베이스 심볼을 선택하세요.");
            return value;
        }
        public static void Hydrate(Catalog c)
        {
            foreach (var p in c.Parts)
            {
                var s = c.Symbols.SingleOrDefault(g => g.Id == p.SymbolId);
                var l = c.Layouts.SingleOrDefault(g => g.Id == p.LayoutId);
                p.Asset = s == null ? null : s.Asset; p.SymbolName = s == null ? "미연결" : s.Name;
                p.LayoutAsset = l == null ? null : l.Asset; p.LayoutName = l == null ? "미등록" : l.Name;
            }
        }
        public static Part Duplicate(Part p)
        { var copy = p.Copy(); copy.Id = Guid.NewGuid().ToString("N"); copy.Revision = 1; copy.Code = ""; copy.Active = true; return copy; }
        public static void DeleteGeometry(Catalog c, string id, bool layout)
        {
            if (c.Parts.Any(p => (layout ? p.LayoutId : p.SymbolId) == id)) throw new InvalidOperationException("참조 중인 도형은 삭제할 수 없습니다. 사용 중지 부품의 연결도 먼저 변경하세요.");
            var list = layout ? c.Layouts : c.Symbols;
            var g = list.SingleOrDefault(x => x.Id == id);
            if (g == null) throw new InvalidOperationException("삭제할 도형이 없습니다.");
            list.Remove(g);
        }
        public static void Validate(Catalog c, Store store)
        {
            foreach (var list in new[] { c.Symbols, c.Layouts })
            {
                if (list.Any(g => g == null) || list.GroupBy(g => g.Id).Any(g => g.Count() > 1)) throw new InvalidDataException("도형 ID 중복 또는 손상입니다.");
                foreach (var g in list)
                {
                    Guid id;
                    if (!Guid.TryParseExact(g.Id, "N", out id) || string.IsNullOrWhiteSpace(g.Name) || g.Name.Length > 200 || g.Name.Any(char.IsControl)) throw new InvalidDataException("도형 ID 또는 이름 오류");
                    store.AssetPath(g.Asset);
                    if (g.StoredUnit != "mm" || !Finite(g.SourceUnitMm) || g.SourceUnitMm <= 0 || !Finite(g.BaseX) || !Finite(g.BaseY) || !Finite(g.BaseZ)) throw new InvalidDataException("도형 단위/기준점 오류");
                }
            }
            foreach (var p in c.Parts)
            { if (!string.IsNullOrEmpty(p.SymbolId)) Geometry(c, p, false); if (!string.IsNullOrEmpty(p.LayoutId)) Geometry(c, p, true); }
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
    public class Instance
    {
        public int Schema { get; set; }
        public Part Part { get; set; }
        public string Panel { get; set; }
        public string Tag { get; set; }
        public string LinkId { get; set; }
        public string LinkOwnerHandle { get; set; }
        public string LayoutSourceHandle { get; set; }
        public string GeometryKind { get; set; }
        public string GeometryId { get; set; }
        [XmlIgnore] public string PhysicalKey { get; set; }
        public bool? DeviceNumberInputEnabled { get; set; }
        public string OriginalDeviceNumber { get; set; }
        [XmlIgnore] public bool CurrentAttributesApplied { get; set; }
        public Instance() { Schema = 1; }
        public Instance Copy() { var copy = (Instance)MemberwiseClone(); copy.Part = Part == null ? null : Part.Copy(); return copy; }
    }
    public static class DeviceNumbers
    {
        public static bool InputEnabled(Part part, UserSettings options)
        { return part.NumberingEnabled ?? options.AskDeviceNumber; }
        public static string Compose(Part part, string suffix)
        {
            if (suffix == null) return null;
            string number = suffix.Trim(), prefix = (part.DeviceNumberPrefix ?? "").Trim();
            string value = number.Length == 0 ? "" : (prefix.Length == 0 ? number : prefix + " " + number);
            if (value.Length > 200 || value.Any(char.IsControl)) throw new InvalidDataException("완성된 기기번호는 제어문자 없이 200자 이내로 입력하세요.");
            return value;
        }
        public static string Resolve(Instance data, string[] attributes)
        {
            if (attributes.Length > 1) throw new InvalidDataException("UNIT_NO 속성이 여러 개입니다. 하나만 남기세요.");
            if (attributes.Length == 0) return data.Tag ?? "";
            string current = (attributes[0] ?? "").Trim();
            // Legacy unnumbered blocks have no reliable original-text baseline.
            if (!data.DeviceNumberInputEnabled.HasValue && string.IsNullOrWhiteSpace(data.Tag)) return "";
            if (data.DeviceNumberInputEnabled == false && current == (data.OriginalDeviceNumber ?? "").Trim()) return "";
            if (current.Length > 200 || current.Any(char.IsControl)) throw new InvalidDataException("UNIT_NO는 제어문자 없이 200자 이내로 입력하세요.");
            return current;
        }
    }
    public static class InstanceFactory
    {
        public static Instance Direct(Part part, GeometryAsset geometry, bool layout, UserSettings options)
        {
            return new Instance { Part = part.Copy(), GeometryKind = layout ? "Layout" : "Symbol", GeometryId = geometry.Id,
                DeviceNumberInputEnabled = DeviceNumbers.InputEnabled(part, options) };
        }
        public static Instance FromSymbol(Instance symbol, GeometryAsset layout)
        {
            return new Instance { Part = symbol.Part.Copy(), Tag = symbol.Tag, Panel = symbol.Panel,
                GeometryKind = "Layout", GeometryId = layout.Id, DeviceNumberInputEnabled = true,
                CurrentAttributesApplied = symbol.CurrentAttributesApplied,
                LinkId = symbol.LinkId, LayoutSourceHandle = symbol.LinkOwnerHandle };
        }
    }
    public static class XmlData
    {
        public static string Write<T>(T value)
        {
            using (var w = new StringWriter(CultureInfo.InvariantCulture))
            { new XmlSerializer(typeof(T)).Serialize(w, value); return w.ToString(); }
        }
        public static T Read<T>(string value)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16000000 };
            using (var r = XmlReader.Create(new StringReader(value), settings))
                return (T)new XmlSerializer(typeof(T)).Deserialize(r);
        }
    }
    public class Store
    {
        public string Root { get; private set; }
        public string CatalogPath { get { return Path.Combine(Root, "catalog.xml"); } }
        public Store(string root) { Root = Path.GetFullPath(root); }
        public Catalog Load()
        {
            if (!File.Exists(CatalogPath)) return new Catalog { Schema = 2 };
            var result = XmlData.Read<Catalog>(File.ReadAllText(CatalogPath, Encoding.UTF8));
            if (result == null || (result.Schema != 1 && result.Schema != 2) || result.Parts == null)
                throw new InvalidDataException("지원하지 않거나 손상된 카탈로그입니다. 원본을 보존하고 백업을 확인하세요.");
            if (result.Parts.Any(p => p == null) || result.Parts.GroupBy(p => p.Id).Any(g => g.Count() > 1))
                throw new InvalidDataException("카탈로그 ID 중복 또는 손상입니다.");
            CatalogModel.Upgrade(result);
            CatalogModel.Validate(result, this);
            CatalogModel.Hydrate(result);
            return result;
        }
        public void Save(int expectedRevision, Action<Catalog> change)
        {
            Directory.CreateDirectory(Root);
            using (new FileStream(Path.Combine(Root, "catalog.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                SaveCore(expectedRevision, change);
            }
        }
        private void SaveCore(int expectedRevision, Action<Catalog> change, bool backupLegacy = true)
        {
                var data = Load();
                if (data.Revision != expectedRevision) throw new IOException("다른 작업에서 목록이 변경됐습니다. 새로고침 후 다시 시도하세요.");
                change(data);
                CatalogModel.Upgrade(data);
                foreach (var p in data.Parts) Validate(p);
                CatalogModel.Validate(data, this);
                CatalogModel.Hydrate(data);
                foreach (var group in data.Parts.Where(p => p.Active).GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase))
                    if (group.Select(p => p.Signature).Distinct().Count() > 1)
                        throw new InvalidDataException("같은 부품코드의 품명/제조사/모델/규격이 다릅니다: " + group.Key);
                data.Revision++;
                if (backupLegacy && File.Exists(CatalogPath) && XmlData.Read<Catalog>(File.ReadAllText(CatalogPath)).Schema == 1) BackupLegacy();
                string temp = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    // Serialize as UTF-8 with a matching XML declaration.
                    using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        using (var xw = XmlWriter.Create(fs, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false }))
                            new XmlSerializer(typeof(Catalog)).Serialize(xw, data);
                        fs.Flush(true);
                    }
                    if (File.Exists(CatalogPath)) File.Replace(temp, CatalogPath, CatalogPath + ".bak");
                    else File.Move(temp, CatalogPath);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public void Migrate()
        {
            if (!File.Exists(CatalogPath) || XmlData.Read<Catalog>(File.ReadAllText(CatalogPath)).Schema == 2) return;
            var c = Load(); Save(c.Revision, value => { });
        }
        public void UpdateGeometry(int expectedRevision, GeometryAsset proposed, bool layout)
        {
            AssetNames.Validate(proposed.Asset);
            using (new FileStream(Path.Combine(Root, "catalog.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var catalog = Load(); if (catalog.Revision != expectedRevision) throw new IOException("목록이 변경됐습니다. 새로고침하세요.");
                var original = (layout ? catalog.Layouts : catalog.Symbols).Single(g => g.Id == proposed.Id);
                string oldPath = AssetPath(original.Asset), newPath = AssetPath(proposed.Asset);
                Action<Catalog> edit = c =>
                {
                    var target = (layout ? c.Layouts : c.Symbols).Single(g => g.Id == original.Id);
                    target.Name = proposed.Name; target.Category = proposed.Category;
                    foreach (var g in c.Symbols.Concat(c.Layouts).Where(g => string.Equals(g.Asset, original.Asset, StringComparison.OrdinalIgnoreCase))) g.Asset = proposed.Asset;
                };
                if (string.Equals(original.Asset, proposed.Asset, StringComparison.Ordinal)) { SaveCore(expectedRevision, edit); return; }
                bool samePath = string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
                if (!samePath && (File.Exists(newPath) || catalog.Symbols.Concat(catalog.Layouts).Any(g => string.Equals(g.Asset, proposed.Asset, StringComparison.OrdinalIgnoreCase)))) throw new IOException("같은 DWG 파일명이 이미 있습니다. 다른 이름을 입력하세요.");
                if (!File.Exists(oldPath)) throw new FileNotFoundException("기존 DWG 파일이 없습니다.", oldPath);
                if (XmlData.Read<Catalog>(File.ReadAllText(CatalogPath)).Schema == 1) BackupLegacy();
                string recovery = Path.Combine(Root, "file-rename-backups", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(recovery);
                File.Copy(CatalogPath, Path.Combine(recovery, "catalog.xml"));
                string holding = Path.Combine(Root, "assets", Guid.NewGuid().ToString("N") + ".rename-tmp");
                bool moved = false, intermediate = false;
                // Deny open readers/writers while allowing our own move and rollback on Windows.
                using (var source = new FileStream(oldPath, FileMode.Open, FileAccess.Read, FileShare.Delete))
                {
                    using (var copy = new FileStream(Path.Combine(recovery, original.Asset), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { source.CopyTo(copy); copy.Flush(true); }
                    try
                    {
                        if (samePath) { File.Move(oldPath, holding); intermediate = true; File.Move(holding, newPath); intermediate = false; }
                        else File.Move(oldPath, newPath);
                        moved = true;
                        SaveCore(expectedRevision, edit, false);
                    }
                    catch (Exception failure)
                    {
                        try { if (moved) File.Move(newPath, oldPath); else if (intermediate) File.Move(holding, oldPath); }
                        catch (Exception rollback) { throw new IOException("파일명 변경과 복구에 실패했습니다. file-rename-backups의 원본과 카탈로그를 복원하세요.", new AggregateException(failure, rollback)); }
                        throw;
                    }
                }
            }
        }
        private void BackupLegacy()
        {
            string folder = Path.Combine(Root, "migration-backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.Copy(CatalogPath, Path.Combine(folder, "catalog.xml"));
            if (File.Exists(CatalogPath + ".bak")) File.Copy(CatalogPath + ".bak", Path.Combine(folder, "catalog.xml.bak"));
            if (Directory.Exists(Path.Combine(Root, "assets")))
                foreach (string file in Directory.GetFiles(Path.Combine(Root, "assets"), "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(folder, file.Substring(Root.Length + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target);
                }
        }
        public void Delete(int expectedRevision, string partId)
        {
            Save(expectedRevision, c =>
            {
                var part = c.Parts.SingleOrDefault(p => p.Id == partId);
                if (part == null) throw new InvalidOperationException("삭제할 부품이 없습니다. 새로고침하세요.");
                c.Parts.Remove(part);
            });
            // Keep asset DWGs so catalog.xml.bak remains usable. Existing drawings
            // carry their own geometry and metadata snapshots.
        }
        public string AssetPath(string name)
        {
            AssetNames.Validate(name);
            return Path.Combine(Root, "assets", name);
        }
        public static void Validate(Part p)
        {
            if (string.IsNullOrWhiteSpace(p.Code))
                throw new InvalidDataException("부품 내부 코드가 없습니다.");
            Guid id;
            if (!Guid.TryParseExact(p.Id, "N", out id)) throw new InvalidDataException("부품 ID 오류");
            if (p.Revision < 1) throw new InvalidDataException("개정번호 오류");
            foreach (var s in new[] { p.Code, p.Name, p.Brand, p.Model, p.Spec, p.Category, p.View, p.DeviceNumberPrefix })
                if (s != null && (s.Length > 200 || s.Any(char.IsControl))) throw new InvalidDataException("정보는 제어문자 없이 항목당 200자 이내로 입력하세요.");
        }
    }
    public class BomResult
    {
        public List<string> Errors = new List<string>();
        public List<string[]> Rows = new List<string[]>();
        public int Excluded;
    }
    public static class BomLogic
    {
        public static BomResult Build(IEnumerable<Instance> input)
        {
            var result = new BomResult();
            var valid = new List<Instance>();
            foreach (var i in input)
            {
                if (i == null || i.Schema != 1 || i.Part == null) { result.Errors.Add("부품 데이터 손상 또는 미지원 버전"); continue; }
                if (!i.Part.Bom) { result.Excluded++; continue; }
                valid.Add(i);
            }
            // Count selected instances independently, including layout views.
            foreach (var g in valid.Where(i => !string.IsNullOrWhiteSpace(i.Tag))
                .GroupBy(i => Part.Pack((i.Panel ?? "").Trim().ToUpperInvariant(), i.Tag.Trim().ToUpperInvariant())))
                if (g.Count() > 1) result.Errors.Add("기기번호 중복: " + g.First().Panel + " / " + g.First().Tag + " (" + g.Count() + "개)");
            foreach (var g in valid.Select((i, index) => new { Instance = i, Key = AggregateKey(i, index) }).GroupBy(x => x.Key).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var parts = g.Select(x => x.Instance.Part).ToList(); var p = parts[0];
                result.Rows.Add(new[] { Many(parts.Select(x => x.Code), "식별정보 없음"), Many(parts.Select(x => x.Name), "품명 없음"), Clean(p.Brand), Clean(p.Model), Clean(p.Spec), "EA", g.Count().ToString(CultureInfo.InvariantCulture) });
            }
            return result;
        }
        public static string Clean(string value) { return (value ?? "").Trim(); }
        private static string AggregateKey(Instance i, int index)
        {
            string model = Clean(i.Part.Model), maker = Clean(i.Part.Brand), spec = Clean(i.Part.Spec);
            if (model.Length > 0) return Part.Pack("model", model, maker, spec);
            string code = Clean(i.Part.Code);
            if (code.Length > 0) return Part.Pack("unnamed-code", code, maker, spec);
            string identity = Clean(i.Part.Id);
            if (identity.Length > 0) return Part.Pack("unnamed-id", identity, maker, spec);
            return Part.Pack("unidentified-object", index.ToString(CultureInfo.InvariantCulture), maker, spec);
        }
        private static string Many(IEnumerable<string> input, string missing)
        {
            var values = input.Select(Clean).Select(s => s.Length == 0 ? missing : s).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            return values.Length == 1 ? values[0] : "여러 값: " + string.Join(" / ", values);
        }
        public static string CsvCell(string value)
        {
            string s = value ?? "";
            string trimmed = s.TrimStart();
            if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        public static string Csv(BomResult result)
        {
            if (result.Errors.Count > 0) throw new InvalidOperationException("검사 오류를 해결한 후 저장하세요.");
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", new[] { "품명", "제조사", "모델", "규격", "단위", "수량" }.Select(CsvCell)));
            foreach (var row in result.Rows) sb.AppendLine(string.Join(",", row.Skip(1).Select(CsvCell)));
            return sb.ToString();
        }
    }
}
