using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ZWLibrary;
static class FeatureTests
{
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
    static void Throws(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, name); }
    static Dictionary<string, List<string>> Map(params string[] pairs)
    { var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase); for (int i = 0; i < pairs.Length; i += 2) { List<string> list; if (!map.TryGetValue(pairs[i], out list)) map[pairs[i]] = list = new List<string>(); list.Add(pairs[i+1]); } return map; }
    static Instance Sample(string code, string model, string maker, string spec, string number)
    { return new Instance { Part = new Part { Code = code, Name = code, Model = model, Brand = maker, Spec = spec }, Tag = number, Panel = "CP1", DeviceNumberInputEnabled = true }; }
    public static int Run()
    {
        count = 0;
        RegistrationTests();
        NumberingTests();
        PackingTests();
        OsnapTests();
        var stored = Sample("A", "Stored", "StoredMaker", "StoredSpec", "QF1");
        var current = CurrentAttributes.Read(stored, Map("UNIT_NO", " QF2 ", "UNIT_MODEL", " New Model / 2P ", "UNIT_MAKER", " Maker-B "), Map());
        Check(current.Tag == "QF2" && current.Part.Model == "New Model / 2P" && current.Part.Brand == "Maker-B", "current attributes override stored values and trim only edges");
        Check(stored.Tag == "QF1" && stored.Part.Model == "Stored", "reading current attributes does not mutate stored instance");
        current = CurrentAttributes.Read(stored, Map("UNIT_NO", "", "UNIT_MODEL", "", "UNIT_MAKER", ""), Map("UNIT_MODEL", "Nested"));
        Check(current.Tag == "" && current.Part.Model == "" && current.Part.Brand == "", "present empty outer attributes stay empty and override nested");
        string mapped; Check(AttributeValues.TryGet("UNIT_MODEL", current, out mapped) && mapped == "", "current empty model maps as empty on linked insertion");
        var forwarded = InstanceFactory.FromSymbol(current, new GeometryAsset());
        Check(AttributeValues.TryGet("UNIT_MAKER", forwarded, out mapped) && mapped == "", "linked layout factory preserves explicit empty maker mapping");
        current = CurrentAttributes.Read(stored, Map(), Map());
        Check(current.Tag == stored.Tag && current.Part.Model == stored.Part.Model && current.Part.Brand == stored.Part.Brand, "missing attributes fall back to stored snapshot");
        current = CurrentAttributes.Read(stored, Map("UNIT_MODEL", "Outer"), Map("UNIT_MODEL", "InnerA", "UNIT_MODEL", "InnerB", "UNIT_MAKER", "NestedMaker", "UNIT_NO", "QF3"));
        Check(current.Part.Model == "Outer" && current.Part.Brand == "NestedMaker" && current.Tag == "QF3", "outer priority with unambiguous nested fallback");
        Throws(() => CurrentAttributes.Read(stored, Map(), Map("UNIT_MODEL", "A", "UNIT_MODEL", "B")), "conflicting nested models rejected explicitly");
        Throws(() => CurrentAttributes.Read(stored, Map("UNIT_MAKER", "A", "UNIT_MAKER", "B"), Map()), "conflicting outer makers rejected explicitly");
        Throws(() => CurrentAttributes.Read(stored, Map("UNIT_NO", "1", "UNIT_NO", "1"), Map()), "duplicate outer UNIT_NO policy retained");
        Check(CurrentAttributes.Read(stored, Map(), Map("UNIT_MODEL", "Same", "UNIT_MODEL", "Same")).Part.Model == "Same", "matching nested values are unambiguous");
        Check(CurrentAttributes.Read(stored, Map("AMP", "30A", "UNIT_SPEC", "IGNORED"), Map()).Part.Spec == "StoredSpec", "unsupported rating/spec tags do not replace stored specification");
        stored.DeviceNumberInputEnabled = false; stored.Tag = ""; stored.OriginalDeviceNumber = "CB00";
        Check(CurrentAttributes.Read(stored, Map("UNIT_NO", "CB00"), Map()).Tag == "", "input OFF original text remains unnumbered");
        Check(CurrentAttributes.Read(stored, Map("UNIT_NO", "CB01"), Map()).Tag == "CB01", "input OFF later direct edit recognized");
        Check(CurrentAttributes.Read(stored, Map(), Map("UNIT_NO", "CB01")).Tag == "CB01", "recorded nested number baseline recognizes later edit");
        stored.OriginalDeviceNumber = null;
        Check(CurrentAttributes.Read(stored, Map(), Map("UNIT_NO", "CB00")).Tag == "", "legacy nested input OFF text without baseline remains unnumbered");
        Instance a = Sample("CODE-A", " M 2P ", " Maker ", " 30A / 2P ", "QF1"), b = Sample("CODE-B", "M 2P", "Maker", "30A / 2P", "QF2");
        var bom = BomLogic.Build(new[] { a, b });
        Check(bom.Rows.Count == 1 && bom.Rows[0][6] == "2" && bom.Errors.Count == 0, "BOM merges matching model maker spec across part codes");
        Check(bom.Rows[0][0].Contains("CODE-A") && bom.Rows[0][0].Contains("CODE-B") && bom.Rows[0][1].Contains("CODE-B"), "merged BOM displays all codes and names");
        b.Part.Brand = "OtherMaker"; Check(BomLogic.Build(new[] { a, b }).Rows.Count == 2, "same model different maker splits BOM");
        b.Part.Brand = "Maker"; b.Part.Spec = "40A / 2P"; Check(BomLogic.Build(new[] { a, b }).Rows.Count == 2, "same model different specification splits BOM");
        b.Part.Spec = "30A / 2P"; b.Part.Model = "M2P"; Check(BomLogic.Build(new[] { a, b }).Rows.Count == 2, "internal model spaces remain significant");
        a.Part.Model = b.Part.Model = ""; Check(BomLogic.Build(new[] { a, b }).Rows.Count == 2, "missing model uses part code fallback instead of merging unrelated parts");
        a.Part.Code = b.Part.Code = ""; a.Part.Id = b.Part.Id = ""; Check(BomLogic.Build(new[] { a, b }).Rows.Count == 2, "unidentified missing models remain separate");
        a = Sample("A", "X", "Maker", "Spec", "QF1"); b = a.Copy();
        bom = BomLogic.Build(new[] { a, b }); Check(bom.Rows[0][6] == "2" && bom.Errors.Count == 1, "duplicate numbers counted but flagged in BOM");
        Throws(() => BomLogic.Csv(bom), "BOM duplicate blocks CSV under new grouping");
        b.Part.Bom = false; Check(BomLogic.Build(new[] { a, b }).Excluded == 1, "per block BOM exclusion retained");
        var list = PartListLogic.Build(new[] { Sample("10", "X", "M", "", "QF10"), Sample("empty", "X", "M", "", ""), Sample("2", "X", "M", "", "QF2"), Sample("1", "X", "M", "", "QF1"), b, a });
        Check(list.Rows.Select(r => r[1]).SequenceEqual(new[] { "QF1", "QF1", "QF1", "QF2", "QF10", "" }), "PARTLIST natural order with blank numbers last");
        Check(list.Rows.Count == 6 && list.Warnings.Count == 1, "PARTLIST retains duplicate and BOM excluded blocks");
        Check(list.Rows[0][0] == "1" && list.Rows[5][0] == "6", "PARTLIST numbering follows sorted rows");
        Check(PartListLogic.Csv(list).Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Length == 7, "PARTLIST CSV includes every row despite duplicate warnings");
        var ties = PartListLogic.Build(new[] { Sample("A", "first", "", "", "QF2"), Sample("B", "second", "", "", "QF2") });
        Check(ties.Rows[0][2] == "first" && ties.Rows[1][2] == "second", "PARTLIST stable tie order");
        Check(new NaturalNumberComparer().Compare("QF9999999999999999999999", "QF10000000000000000000000") < 0, "natural numeric order avoids integer overflow");
        var safe = PartListLogic.Build(new[] { Sample("A", "=FORMULA", "한글", "a,\"b", "QF1") });
        Check(PartListLogic.Csv(safe).Contains("'=FORMULA") && PartListLogic.Csv(safe).Contains("\"\"b"), "PARTLIST uses safe CSV quoting");
        Check(new UTF8Encoding(true).GetPreamble().SequenceEqual(new byte[] { 239, 187, 191 }), "CSV encoding includes UTF8 BOM for Excel");
        Check(TableText.Literal("\\{x}%<field>%").Contains("\\\\") && TableText.Literal("%<field>%").Contains("\\U+0025"), "CAD table text escapes format and field syntax");
        Check(TableText.Wrap("한글ABCD", 4).Replace("\n", "") == "한글ABCD" && TableText.Wrap("한글ABCD", 4).Contains("\n"), "table wrapping preserves all characters");
        RenameTests(); return count;
    }
    static void OsnapTests()
    {
        int mode = 175; var writes = new List<int>();
        Action<int> write = value => { mode = value; writes.Add(value); };
        InsertionOsnapRefresh.Apply(true, () => mode, write);
        Check(writes.SequenceEqual(new[] { 175 | 16384, 175 }), "enabled object snap toggles OFF then ON before point selection");
        Check(mode == 175, "snap refresh preserves all original snap types");
        writes.Clear(); mode = 0; InsertionOsnapRefresh.Apply(true, () => mode, write);
        Check(mode == 0 && writes.Count == 0, "originally disabled snap stays disabled without writes");
        mode = 16384 | 175; InsertionOsnapRefresh.Apply(true, () => mode, write);
        Check(mode == (16384 | 175) && writes.Count == 0, "suppressed snap with saved types stays suppressed");
        InsertionOsnapRefresh.Apply(false, () => { throw new Exception("Must not read"); }, write);
        Check(writes.Count == 0, "disabled refresh option does not read or change CAD state");
        mode = 7; bool first = true;
        Throws(() => InsertionOsnapRefresh.Apply(true, () => mode, value => { mode = value; if (first) { first = false; throw new Exception("setter failure"); } }), "native OFF failure is reported");
        Check(mode == 7, "OFF failure still restores original enabled mask");
        writes.Clear(); mode = 175;
        for (int i = 0; i < 5; i++) InsertionOsnapRefresh.Apply(true, () => mode, write);
        Check(mode == 175 && writes.Count == 10, "each of five placements refreshes and restores enabled snap");
        var legacy = XmlData.Read<UserSettings>("<UserSettings><SuppressInsertionOsnap>true</SuppressInsertionOsnap></UserSettings>");
        Check(legacy.RefreshOsnapBeforePoint, "old input-suppression preference is ignored and before-point refresh defaults ON");
        legacy.RefreshOsnapBeforePoint = false;
        Check(!XmlData.Read<UserSettings>(XmlData.Write(legacy)).RefreshOsnapBeforePoint, "before-point snap refresh preference survives restart");
        Check(!XmlData.Read<UserSettings>("<UserSettings><RefreshOsnapAfterInsertion>false</RefreshOsnapAfterInsertion></UserSettings>").RefreshOsnapBeforePoint, "previously disabled automatic refresh preference stays disabled after timing change");
    }
    static void PackingTests()
    {
        var bounds = new List<LayoutBounds> {
            new LayoutBounds { MinX = -20, MinY = -10, MaxX = 40, MaxY = 20 },
            new LayoutBounds { MinX = 10, MinY = 20, MaxX = 35, MaxY = 100 },
            new LayoutBounds { MinX = -100, MinY = -50, MaxX = -10, MaxY = -20 },
            new LayoutBounds { MinX = 0, MinY = 0, MaxX = 30, MaxY = 30 },
            new LayoutBounds { MinX = -5, MinY = -100, MaxX = 5, MaxY = 5 }
        };
        var plan = LayoutPacking.Plan(bounds, 3, 10);
        Check(plan.Count == bounds.Count && plan[0].X == 0 && plan[0].Y == 0, "grid first insertion point equals user anchor");
        Check(plan[1].X + bounds[1].MinX > plan[0].X + bounds[0].MaxX, "different origins preserve horizontal separation");
        Check(plan[3].Y + bounds[3].MaxY < plan[0].Y + bounds[0].MinY, "grid wraps downward after configured column count");
        Check(plan[0].X + bounds[0].MinX == plan[3].X + bounds[3].MinX, "new rows align their left edges despite different origins");
        bool clear = true;
        for (int i = 0; i < bounds.Count; i++) for (int j = i + 1; j < bounds.Count; j++)
            clear &= plan[i].X + bounds[i].MaxX + 10 <= plan[j].X + bounds[j].MinX || plan[j].X + bounds[j].MaxX + 10 <= plan[i].X + bounds[i].MinX || plan[i].Y + bounds[i].MaxY + 10 <= plan[j].Y + bounds[j].MinY || plan[j].Y + bounds[j].MaxY + 10 <= plan[i].Y + bounds[i].MinY;
        Check(clear, "all grid rectangles retain configured gap for mixed sizes and origins");
        var singleColumn = LayoutPacking.Plan(bounds, 1, 0.01);
        Check(singleColumn[1].Y < singleColumn[0].Y && singleColumn[1].X + bounds[1].MinX == singleColumn[0].X + bounds[0].MinX, "one column creates a vertical list");
        Check(LayoutPacking.Plan(new List<LayoutBounds>(), 5, 10).Count == 0, "empty layout selection has no positions");
        Throws(() => LayoutPacking.Plan(bounds, 0, 10), "zero columns rejected");
        Throws(() => LayoutPacking.Plan(bounds, 101, 10), "oversized column count rejected");
        Throws(() => LayoutPacking.Plan(bounds, 5, 0), "zero gap rejected");
        Throws(() => LayoutPacking.Plan(bounds, 5, double.NaN), "invalid gap rejected");
        Throws(() => LayoutPacking.Plan(new[] { new LayoutBounds { MinX = 5, MaxX = 1 } }, 1, 10), "inverted rectangle rejected");
        Throws(() => LayoutPacking.Plan(new[] { new LayoutBounds { MaxX = double.PositiveInfinity } }, 1, 10), "unbounded layout extents rejected");
        var old = XmlData.Read<UserSettings>("<UserSettings><LibraryRoot>C:\\Old</LibraryRoot></UserSettings>");
        Check(!old.BatchLayoutInsertion && old.LayoutColumns == 5 && old.LayoutGapMm == 10, "legacy settings default to continuous insertion with valid grid defaults");
        var configured = new UserSettings { BatchLayoutInsertion = true, LayoutColumns = 3, LayoutGapMm = 12.5 };
        var saved = XmlData.Read<UserSettings>(XmlData.Write(configured));
        Check(saved.BatchLayoutInsertion && saved.LayoutColumns == 3 && saved.LayoutGapMm == 12.5, "grid insertion options survive XML restart");
        configured.LayoutColumns = 0; Throws(() => LayoutPacking.Validate(configured), "invalid settings grid rejected before saving");
        var large = Enumerable.Range(0, 123).Select(i => new LayoutBounds { MinX = -i, MinY = -i, MaxX = 100 + i, MaxY = 20 + i }).ToList();
        Check(LayoutPacking.Plan(large, 5, 10).Count == 123, "grid handles multiple complete and partial rows");
    }
    static void NumberingTests()
    {
        var part = new Part { Code = "A", Model = "M", Brand = "B", NumberingEnabled = true, DeviceNumberPrefix = " MCCB " };
        var geometry = new GeometryAsset();
        var options = new UserSettings { AskDeviceNumber = false };
        Check(InstanceFactory.Direct(part, geometry, false, options).DeviceNumberInputEnabled == true, "per part ON overrides global OFF for symbol");
        Check(InstanceFactory.Direct(part, geometry, true, options).DeviceNumberInputEnabled == true, "per part ON applies to direct layout");
        Check(DeviceNumbers.Compose(part, " 01 ") == "MCCB 01", "number combines prefix and suffix with one space and preserves zero");
        Check(DeviceNumbers.Compose(part, "A-02") == "MCCB A-02", "number suffix accepts nonnumeric device identifiers");
        Check(DeviceNumbers.Compose(part, " ") == "", "empty suffix remains unassigned instead of prefix only");
        Check(DeviceNumbers.Compose(part, null) == null, "cancelled suffix cancels insertion");
        var saved = XmlData.Read<Part>(XmlData.Write(part));
        Check(saved.NumberingEnabled == true && saved.DeviceNumberPrefix == " MCCB ", "numbering options persist through XML");
        var duplicate = CatalogModel.Duplicate(part);
        Check(duplicate.NumberingEnabled == true && duplicate.DeviceNumberPrefix == part.DeviceNumberPrefix, "part duplication retains numbering options");
        var symbol = InstanceFactory.Direct(part, geometry, false, options); symbol.Tag = DeviceNumbers.Compose(part, "01");
        var layout = InstanceFactory.FromSymbol(symbol, geometry);
        Check(layout.Tag == "MCCB 01", "symbol to layout keeps full number without double prefix");
        part.NumberingEnabled = false; options.AskDeviceNumber = true;
        Check(!DeviceNumbers.InputEnabled(part, options), "per part OFF overrides global ON");
        var noNumber = InstanceFactory.Direct(part, geometry, true, options); noNumber.Tag = ""; noNumber.OriginalDeviceNumber = "ORIGINAL";
        Check(DeviceNumbers.Resolve(noNumber, new[] { "ORIGINAL" }) == "", "per part OFF retains original text as unassigned");
        Check(DeviceNumbers.Resolve(noNumber, new[] { "MANUAL 02" }) == "MANUAL 02", "manual number edits remain recognized for unnumbered parts");
        part.NumberingEnabled = null; Check(DeviceNumbers.InputEnabled(part, options), "legacy part inherits global ON");
        options.AskDeviceNumber = false; Check(!DeviceNumbers.InputEnabled(part, options), "legacy part inherits global OFF");
        part.DeviceNumberPrefix = ""; Check(DeviceNumbers.Compose(part, "CB 03") == "CB 03", "empty prefix preserves existing free entry behavior");
        part.DeviceNumberPrefix = new string('A', 198);
        Check(DeviceNumbers.Compose(part, "1").Length == 200, "combined number accepts exactly 200 characters");
        Throws(() => DeviceNumbers.Compose(part, "12"), "combined number over 200 characters rejected");
        part.DeviceNumberPrefix = "CB\nX"; Throws(() => Store.Validate(part), "prefix control characters rejected on save");
        part.DeviceNumberPrefix = "CB"; Throws(() => DeviceNumbers.Compose(part, "0\t1"), "suffix control characters rejected");
        var legacy = XmlData.Read<Part>("<Part><Model>Old</Model></Part>");
        Check(legacy.NumberingEnabled == null && legacy.DeviceNumberPrefix == null, "old XML defaults to inherited input with no prefix");
    }
    static void RegistrationTests()
    {
        var p = new Part { Model = "Model-A", Brand = "Maker-A" };
        PartEditing.Prepare(p); string code = p.Code;
        Check(code == "P-" + p.Id && string.IsNullOrEmpty(p.Name), "model and maker registration generates hidden code with optional name");
        PartEditing.Prepare(p); Check(p.Code == code, "editing preserves generated code");
        var copy = CatalogModel.Duplicate(p); PartEditing.Prepare(copy);
        Check(copy.Code != p.Code && copy.Id != p.Id, "duplicate receives independent hidden code");
        var legacy = new Part { Code = "LEGACY", Name = "Old" }; Store.Validate(legacy);
        Check(legacy.Code == "LEGACY", "legacy missing model maker remains valid for unrelated catalog operations");
        Throws(() => PartEditing.Prepare(legacy), "editing requires model and maker on legacy part");
        Throws(() => PartEditing.Prepare(new Part { Model = "M", Brand = " " }), "registration rejects missing maker");
        Throws(() => PartEditing.Prepare(new Part { Model = " ", Brand = "B" }), "registration rejects missing model");
        var layout = new GeometryAsset { Name = "Layout", Category = "Breaker", Asset = "layout.dwg" };
        var symbol = new GeometryAsset { Name = "Symbol", Category = "Control", Asset = "symbol.dwg" };
        var unused = new GeometryAsset { Name = "Unlinked", Category = "Breaker", Asset = "unused.dwg" };
        var c = new Catalog { Schema = 2 }; c.Layouts.Add(layout); c.Layouts.Add(unused); c.Symbols.Add(symbol); p.LayoutId = layout.Id; c.Parts.Add(p);
        var store = new Store(Path.Combine(Path.GetTempPath(), "zwlib-registration-" + Guid.NewGuid().ToString("N")));
        try
        {
            store.Save(0, target => { target.Parts.Add(p); target.Layouts.Add(layout); target.Layouts.Add(unused); target.Symbols.Add(symbol); });
            var loaded = store.Load();
            Check(loaded.Parts.Single().SymbolId == null && loaded.Parts.Single().Asset == null && loaded.Parts.Single().LayoutAsset == "layout.dwg", "layout only part saves and reloads without symbol");
            var instance = InstanceFactory.Direct(loaded.Parts.Single(), layout, true, new UserSettings());
            Check(BomLogic.Build(new[] { instance }).Rows.Single()[6] == "1" && PartListLogic.Build(new[] { instance }).Rows.Count == 1, "layout only part supports BOM and PARTLIST");
            Check(!BomLogic.Csv(BomLogic.Build(new[] { instance })).Contains(code), "BOM CSV hides internal part code");
            Throws(() => CatalogModel.Geometry(loaded, loaded.Parts.Single(), false), "missing symbol cannot be resolved for insertion");
            store.Save(loaded.Revision, target => { target.Parts[0].LayoutId = null; target.Parts[0].LayoutAsset = null; });
            Check(store.Load().Parts.Single().LayoutId == null, "both geometry connections may be empty");
            loaded = store.Load(); store.Save(loaded.Revision, target => target.Parts[0].SymbolId = symbol.Id);
            Check(store.Load().Parts.Single().Asset == "symbol.dwg", "symbol only part remains supported");
        }
        finally { if (Directory.Exists(store.Root)) Directory.Delete(store.Root, true); }
        var choices = GeometrySearch.Choices(c, true);
        Check(GeometrySearch.Filter(choices, "model-a", null, null).Single().Geometry.Id == layout.Id, "geometry search finds linked part model without case sensitivity");
        Check(GeometrySearch.Filter(choices, "", "Breaker", "Maker-A").Single().Geometry.Id == layout.Id, "geometry category and maker filters combine");
        Check(GeometrySearch.Filter(choices, "", null, "").Single().Geometry.Id == unused.Id, "unlinked geometry remains discoverable under unspecified maker");
        Check(GeometrySearch.Filter(choices, "no-match", null, null).Count == 0, "geometry search empty results supported");
        Check(GeometrySearch.Choices(c, false).Count == 1, "symbol picker excludes layout assets");
        Check(layout.ToString() == "Layout" && !layout.ToString().Contains(layout.Id), "geometry dropdown hides stable ID");
        p.SymbolId = Guid.NewGuid().ToString("N");
        Throws(() => CatalogModel.Validate(c, store), "optional symbol still rejects nonempty dangling reference");
    }
    static void RenameTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "zwlib-rename-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "assets"));
        try
        {
            var store = new Store(root); string asset = Guid.NewGuid().ToString("N") + ".dwg"; File.WriteAllText(store.AssetPath(asset), "geometry bytes");
            var geometry = new GeometryAsset { Name = "공용 심볼", Asset = asset, SourceUnitMm = 10 };
            Part p = new Part { Code = "A", Name = "A", Model = "A", SymbolId = geometry.Id }, q = new Part { Code = "B", Name = "B", Model = "B", SymbolId = geometry.Id };
            store.Save(0, data => { data.Symbols.Add(geometry); data.Parts.Add(p); data.Parts.Add(q); });
            var proposed = geometry.Copy(); proposed.Name = "차단기 2P"; proposed.Asset = "차단기 2P.dwg";
            store.UpdateGeometry(store.Load().Revision, proposed, false); var c = store.Load();
            Check(!File.Exists(store.AssetPath(asset)) && File.ReadAllText(store.AssetPath(proposed.Asset)) == "geometry bytes", "human readable DWG rename preserves bytes without old file");
            Check(c.Parts.All(x => x.SymbolId == geometry.Id && x.Asset == proposed.Asset && x.SymbolName == proposed.Name), "renamed shared geometry preserves IDs and every part connection");
            Check(c.Symbols.Single().SourceUnitMm == 10, "metadata edit preserves stored geometry unit");
            Check(Directory.GetDirectories(Path.Combine(root, "file-rename-backups")).Length == 1, "DWG rename retains recovery copy and catalog");
            File.WriteAllText(store.AssetPath("occupied.dwg"), "other bytes"); var conflict = proposed.Copy(); conflict.Asset = "occupied.dwg";
            Throws(() => store.UpdateGeometry(c.Revision, conflict, false), "duplicate filename never overwritten");
            Check(File.ReadAllText(store.AssetPath("occupied.dwg")) == "other bytes" && store.Load().Symbols.Single().Asset == proposed.Asset, "duplicate rename failure preserves both files and catalog");
            foreach (var bad in new[] { "../escape.dwg", "C:\\bad.dwg", "bad?.dwg", "CON.dwg", "LPT1.dwg", ".dwg", "bad.dwg " }) Throws(() => store.AssetPath(bad), "invalid file name rejected: " + bad);
            var renamed = proposed.Copy(); renamed.Asset = "new name.dwg";
            using (var held = new FileStream(store.AssetPath(proposed.Asset), FileMode.Open, FileAccess.Read, FileShare.Read)) Throws(() => store.UpdateGeometry(c.Revision, renamed, false), "in use DWG filename change rejected");
            Check(store.Load().Symbols.Single().Asset == proposed.Asset && File.Exists(store.AssetPath(proposed.Asset)), "in use failure preserves file and catalog");
            using (var held = new FileStream(store.CatalogPath, FileMode.Open, FileAccess.Read, FileShare.Read)) Throws(() => store.UpdateGeometry(c.Revision, renamed, false), "catalog commit failure rolls filename back");
            Check(File.Exists(store.AssetPath(proposed.Asset)) && !File.Exists(store.AssetPath(renamed.Asset)) && store.Load().Symbols.Single().Asset == proposed.Asset, "failed catalog commit restores matching filename and catalog");
            Throws(() => store.UpdateGeometry(c.Revision - 1, renamed, false), "stale filename update rejected");
            renamed.Asset = "차단기 2P.DWG"; store.UpdateGeometry(c.Revision, renamed, false);
            Check(store.Load().Symbols.Single().Asset == renamed.Asset && File.Exists(store.AssetPath(renamed.Asset)), "case only filename change supported");
        }
        finally { Directory.Delete(root, true); }
    }
}
