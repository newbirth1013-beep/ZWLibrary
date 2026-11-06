using System;
using System.IO;
using System.Linq;
using ZWLibrary;

class CoreTests
{
    static int count;
    static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
    static void Throws(Action action, string name)
    { bool thrown = false; try { action(); } catch { thrown = true; } Check(thrown, name); }
    static Part Sample() { return new Part { Code = "BRK-001", Name = "차단기", Brand = "테스트", Model = "ABC", Spec = "2P", Asset = Guid.NewGuid().ToString("N") + ".dwg" }; }
    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "zwlib-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Store(root); var p = Sample();
            Check(store.Load().Revision == 0, "empty catalog");
            store.Save(0, c => c.Parts.Add(p));
            Check(store.Load().Parts.Single().Name == "차단기", "UTF-8 XML round trip");
            Check(store.Load().Revision == 1, "catalog revision");
            string before = File.ReadAllText(store.CatalogPath);
            Throws(() => store.Save(0, c => c.Parts.Clear()), "stale write rejected");
            Check(File.ReadAllText(store.CatalogPath) == before, "stale write preserves catalog");
            Throws(() => store.Save(1, c => { var x = Sample(); x.Spec = "3P"; c.Parts.Add(x); }), "conflicting code rejected");
            Check(File.ReadAllText(store.CatalogPath) == before, "validation failure preserves catalog");
            store.Save(1, c => c.Parts[0].Active = false);
            Check(File.Exists(store.CatalogPath + ".bak"), "previous catalog backup");
            Check(!store.Load().Parts[0].Active && store.Load().Parts.Count == 1, "soft delete retains data");
            store.Save(2, c => c.Parts[0].Active = true);
            Check(store.Load().Parts[0].Active, "restore");
            Throws(() => store.AssetPath("../escape.dwg"), "relative path escape rejected");
            Throws(() => store.AssetPath("C:\\escape.dwg"), "absolute path rejected");
            Check(store.AssetPath(p.Asset).StartsWith(root), "asset under selected library");
            var a = new Instance { Part = p.Copy(), Panel = "CP1", Tag = "QF1" };
            var b = new Instance { Part = p.Copy(), Panel = "CP1", Tag = "QF2" };
            var bom = BomLogic.Build(new[] { a, b });
            Check(bom.Errors.Count == 0 && bom.Rows.Single()[6] == "2", "same part two devices = 2");
            b.Tag = "qf1";
            bom = BomLogic.Build(new[] { a, b });
            Check(bom.Errors.Count > 0, "duplicate tag case-insensitive");
            Throws(() => BomLogic.Csv(bom), "invalid BOM cannot export");
            b.Part.Bom = false;
            bom = BomLogic.Build(new[] { a, b });
            Check(bom.Excluded == 1 && bom.Errors.Count == 0 && bom.Rows.Single()[6] == "1", "auxiliary view excluded");
            b.Part.Bom = true; b.Tag = ""; a.Tag = "";
            Check(BomLogic.Build(new[] { a, b }).Rows.Single()[6] == "2", "untagged instances counted separately");
            b.Part.Model = "DIFFERENT";
            Check(BomLogic.Build(new[] { a, b }).Errors.Count == 0 && BomLogic.Build(new[] { a, b }).Rows.Count == 2, "different current models split BOM even with same part code");
            b = XmlData.Read<Instance>(XmlData.Write(a));
            Check(b.Part.Name == "차단기" && b.Schema == 1, "drawing snapshot XML round trip");
            Check(BomLogic.CsvCell("=1+1") == "\"'=1+1\"", "CSV formula escaped");
            Check(BomLogic.CsvCell("  @SUM(1)").StartsWith("\"'"), "CSV whitespace formula escaped");
            Check(BomLogic.CsvCell("a,\"b\"\nc") == "\"a,\"\"b\"\"\nc\"", "CSV delimiters escaped");
            Throws(() => XmlData.Read<Catalog>("<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><Catalog>&x;</Catalog>"), "DTD rejected");
            string mapped;
            var mappedInstance = new Instance { Part = Sample(), Tag = "QF123", Panel = "CP1" };
            Check(AttributeValues.TryGet("UNIT_NO", mappedInstance, out mapped) && mapped == "QF123", "device tag mapping");
            Check(AttributeValues.TryGet("UNIT_MODEL", mappedInstance, out mapped) && mapped == "ABC", "model mapping");
            Check(AttributeValues.TryGet("UNIT_MAKER", mappedInstance, out mapped) && mapped == "테스트", "maker mapping");
            Check(!AttributeValues.TryGet("UNIT_RATING", mappedInstance, out mapped), "unrelated rating unchanged");
            Check(AttributeValues.TryGet(" unit_no ", mappedInstance, out mapped) && mapped == "QF123", "attribute tag normalized");
            mappedInstance.Tag = "";
            Check(AttributeValues.TryGet("UNIT_NO", mappedInstance, out mapped) && mapped == "", "empty device number clears label");
            mappedInstance.Part.Model = "";
            Check(!AttributeValues.TryGet("UNIT_MODEL", mappedInstance, out mapped), "empty catalog model preserves original");
            mappedInstance.Part.Brand = null;
            Check(!AttributeValues.TryGet("UNIT_MAKER", mappedInstance, out mapped), "empty catalog maker preserves original");
            Check(!AttributeValues.TryGet("UNIT_MODEL_EXTRA", mappedInstance, out mapped), "exact tag match only");
            mappedInstance.Tag = "";
            Check(!AttributeValues.TryGet("UNIT_NO", mappedInstance, out mapped, false), "disabled device input preserves original text");
            mappedInstance.Part.Model = "ABC";
            Check(AttributeValues.TryGet("UNIT_MODEL", mappedInstance, out mapped, false) && mapped == "ABC", "disabled device input still maps model");
            mappedInstance.Part.Brand = "LSIS";
            Check(AttributeValues.TryGet("UNIT_MAKER", mappedInstance, out mapped, false) && mapped == "LSIS", "disabled device input still maps maker");
            string config = Path.Combine(root, "config");
            var options = SettingsFile.Load(config);
            Check(options.AskRotation && options.AskDeviceNumber && options.AskPanelNumber, "first run prompts enabled");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "location.txt"), root);
            Check(SettingsFile.Load(config).LibraryRoot == root, "legacy library folder retained");
            for (int mask = 0; mask < 8; mask++)
            {
                SettingsFile.Save(config, new UserSettings { LibraryRoot = root, AskRotation = (mask & 1) != 0, AskDeviceNumber = (mask & 2) != 0, AskPanelNumber = (mask & 4) != 0 });
                options = SettingsFile.Load(config);
                Check(options.LibraryRoot == root && options.AskRotation == ((mask & 1) != 0) && options.AskDeviceNumber == ((mask & 2) != 0) && options.AskPanelNumber == ((mask & 4) != 0), "prompt settings restart round trip " + mask);
            }
            string savedSettings = File.ReadAllText(Path.Combine(config, "settings.xml"));
            Throws(() => SettingsFile.Save(config, new UserSettings { LibraryRoot = "" }), "empty settings folder rejected");
            Check(File.ReadAllText(Path.Combine(config, "settings.xml")) == savedSettings, "invalid settings preserve previous settings");
            Directory.CreateDirectory(Path.GetDirectoryName(store.AssetPath(p.Asset)));
            File.WriteAllText(store.AssetPath(p.Asset), "test asset");
            Throws(() => store.Delete(0, p.Id), "stale delete rejected");
            Check(store.Load().Parts.Count == 1, "stale delete retains catalog item");
            store.Delete(store.Load().Revision, p.Id);
            Check(store.Load().Parts.Count == 0, "delete removes catalog item");
            Check(File.Exists(store.AssetPath(p.Asset)), "delete retains recovery asset");
            Check(XmlData.Read<Catalog>(File.ReadAllText(store.CatalogPath + ".bak")).Parts.Single().Id == p.Id, "delete backup retains removed part");
            Check(BomLogic.Build(new[] { a }).Rows.Single()[6] == "1", "existing drawing snapshot BOM survives catalog deletion");
            Throws(() => store.Delete(store.Load().Revision, p.Id), "missing deletion target rejected");
            var live = new Instance { Part = Sample(), Tag = "01" };
            Check(DeviceNumbers.Resolve(live, new[] { "02" }) == "02", "legacy numbered block reads edited attribute");
            Check(DeviceNumbers.Resolve(live, new[] { "" }) == "", "cleared attribute does not fall back to stale number");
            Check(DeviceNumbers.Resolve(live, new string[0]) == "01", "missing attribute retains stored number");
            Throws(() => DeviceNumbers.Resolve(live, new[] { "01", "02" }), "ambiguous duplicate UNIT_NO rejected");
            live.Tag = "";
            Check(DeviceNumbers.Resolve(live, new[] { "CB00" }) == "", "legacy unnumbered original not mistaken for device number");
            live.DeviceNumberInputEnabled = false; live.OriginalDeviceNumber = "CB00";
            Check(DeviceNumbers.Resolve(live, new[] { "CB00" }) == "", "disabled input original remains unnumbered");
            Check(DeviceNumbers.Resolve(live, new[] { "QF02" }) == "QF02", "disabled input later manual edit is recognized");
            live.DeviceNumberInputEnabled = true;
            Check(DeviceNumbers.Resolve(live, new[] { " 02 " }) == "02", "enabled input reads trimmed current number");
            var roundTrip = XmlData.Read<Instance>(XmlData.Write(live));
            Check(roundTrip.DeviceNumberInputEnabled == true && roundTrip.OriginalDeviceNumber == "CB00", "number policy and original text persist");
            var first = new Instance { Part = Sample(), Tag = "01" };
            var second = new Instance { Part = first.Part.Copy(), Tag = "01" };
            second.Tag = DeviceNumbers.Resolve(second, new[] { "02" });
            Check(BomLogic.Build(new[] { first, second }).Errors.Count == 0, "manual number edit resolves duplicate BOM error");
            second.Tag = DeviceNumbers.Resolve(second, new[] { "01" });
            Check(BomLogic.Build(new[] { first, second }).Errors.Count > 0, "real current number duplicate still rejected");
            var symbolView = new Instance { Part = Sample(), Tag = "QF01", Panel = "CP1", PhysicalKey = "A1", LinkId = "link", LinkOwnerHandle = "A1" };
            var layoutView = new Instance { Part = symbolView.Part.Copy(), Tag = "QF01", Panel = "CP1", PhysicalKey = "A1", LinkId = "link", LinkOwnerHandle = "B1", LayoutSourceHandle = "A1" };
            var paired = BomLogic.Build(new[] { symbolView, layoutView });
            Check(paired.Errors.Count == 1 && paired.Rows.Single()[6] == "2", "selected symbol and layout count twice and flag duplicate number");
            Check(BomLogic.Build(new[] { layoutView }).Rows.Single()[6] == "1", "layout alone counts once");
            Throws(() => BomLogic.Csv(paired), "mixed duplicate selection cannot export CSV");
            Check(BomLogic.Build(new[] { layoutView }).Errors.Count == 0, "layout BOM does not require source symbol in selection");
            Check(BomLogic.Build(new[] { symbolView }).Rows.Single()[6] == "1", "symbol alone counts once");
            symbolView.Tag = layoutView.Tag = "";
            Check(BomLogic.Build(new[] { symbolView, layoutView }).Rows.Single()[6] == "2", "unnumbered selected views count twice");
            layoutView.PhysicalKey = "A2";
            Check(BomLogic.Build(new[] { symbolView, layoutView }).Rows.Single()[6] == "2", "independent unnumbered parts count twice");
            symbolView.Tag = layoutView.Tag = "QF01";
            Check(BomLogic.Build(new[] { symbolView, layoutView }).Errors.Count > 0, "independent same tag still rejected");
            layoutView.PhysicalKey = "A1"; layoutView.Tag = "QF02";
            Check(BomLogic.Build(new[] { symbolView, layoutView }).Errors.Count == 0, "different current numbers accepted independently");
            layoutView.Tag = "QF01"; layoutView.Panel = "CP2";
            Check(BomLogic.Build(new[] { symbolView, layoutView }).Errors.Count == 0, "same number in different panels accepted");
            layoutView.Panel = "CP1"; layoutView.Part.Model = "DIFFERENT";
            Check(BomLogic.Build(new[] { symbolView, layoutView }).Errors.Count > 0, "linked view model mismatch rejected");
            var linkRoundTrip = XmlData.Read<Instance>(XmlData.Write(layoutView));
            layoutView.Part.Code = "OTHER-001";
            layoutView.Tag = "QF02";
            Check(BomLogic.Build(new[] { symbolView, layoutView }).Errors.Count == 0, "linked views may have independent specifications and numbers");
            layoutView.Part.Bom = false;
            var excludedLayout = BomLogic.Build(new[] { symbolView, layoutView });
            Check(excludedLayout.Excluded == 1 && excludedLayout.Rows.Single()[6] == "1", "layout respects its own BOM exclusion");
            string panelValue;
            Check(AttributeValues.TryGet(" unit_panel ", symbolView, out panelValue) && panelValue == "CP1", "panel attribute mapping");
            symbolView.Panel = null;
            Check(AttributeValues.TryGet("UNIT_PANEL", symbolView, out panelValue) && panelValue == "", "unspecified panel clears label");
            Check(linkRoundTrip.LinkId == "link" && linkRoundTrip.LinkOwnerHandle == "B1" && linkRoundTrip.LayoutSourceHandle == "A1" && linkRoundTrip.PhysicalKey == null, "persistent link identity excludes temporary BOM key");
            var linkedPart = Sample(); linkedPart.LayoutAsset = Guid.NewGuid().ToString("N") + ".dwg";
            store.Save(store.Load().Revision, c => c.Parts.Add(linkedPart));
            Check(store.Load().Parts.Single().LayoutAsset == linkedPart.LayoutAsset, "catalog retains layout asset association");
            Check(store.Load().Parts.Single().Asset == linkedPart.Asset, "layout association preserves symbol asset");
            File.WriteAllText(store.CatalogPath, "broken");
            Throws(() => store.Load(), "corrupt catalog fails visibly");
            Check(File.ReadAllText(store.CatalogPath) == "broken", "corrupt catalog not overwritten");
            count += StructureTests.Run();
            count += FeatureTests.Run();
            Console.WriteLine("TOTAL PASS: " + count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
