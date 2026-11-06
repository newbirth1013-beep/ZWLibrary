using System;
using System.IO;
using System.Linq;
using ZWLibrary;

static class StructureTests
{
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
    static void Throws(Action action, string name) { bool thrown = false; try { action(); } catch { thrown = true; } Check(thrown, name); }
    public static int Run()
    {
        count = 0;
        string root = Path.Combine(Path.GetTempPath(), "zwlib-structure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        try
        {
            var store = new Store(root);
            var p = new Part { Code = "A", Name = "차단기", Brand = "MakerA", Model = "2P-A", Asset = Guid.NewGuid().ToString("N") + ".dwg", LayoutAsset = Guid.NewGuid().ToString("N") + ".dwg" };
            var q = p.Copy(); q.Id = Guid.NewGuid().ToString("N"); q.Code = "B"; q.Model = "2P-B"; q.Brand = "MakerB";
            var legacy = new Catalog { Revision = 9 }; legacy.Parts.Add(p); legacy.Parts.Add(q);
            string old = XmlData.Write(legacy); File.WriteAllText(store.CatalogPath, old);
            File.WriteAllText(store.CatalogPath + ".bak", "prior recovery catalog");
            File.WriteAllText(store.AssetPath(p.Asset), "symbol bytes"); File.WriteAllText(store.AssetPath(p.LayoutAsset), "layout bytes");
            var c = store.Load();
            Check(c.Schema == 2 && c.Symbols.Count == 2 && c.Layouts.Count == 2, "conservative migration does not merge identical legacy assets");
            Check(c.Parts[0].SymbolId != p.Id && c.Parts[0].LayoutId != p.Id && c.Parts[0].SymbolId != c.Parts[0].LayoutId, "migrated part symbol and layout identities are distinct");
            Check(File.ReadAllText(store.CatalogPath) == old, "legacy load is read only");
            Check(c.Parts[0].Id == p.Id && c.Parts[0].Code == p.Code && c.Parts[0].Model == p.Model && c.Parts[0].LayoutAsset == p.LayoutAsset, "legacy identity and connections preserved");
            CatalogModel.Upgrade(c); CatalogModel.Upgrade(c);
            Check(c.Symbols.Count == 2 && c.Layouts.Count == 2, "in memory migration is idempotent");
            store.Migrate();
            var backup = Directory.GetDirectories(Path.Combine(root, "migration-backups")).Single();
            Check(File.ReadAllText(Path.Combine(backup, "catalog.xml")) == old, "migration original catalog backup");
            Check(File.ReadAllText(Path.Combine(backup, "catalog.xml.bak")) == "prior recovery catalog", "migration preserves previous recovery catalog");
            Check(File.ReadAllText(Path.Combine(backup, "assets", p.Asset)) == "symbol bytes" && File.ReadAllText(Path.Combine(backup, "assets", p.LayoutAsset)) == "layout bytes", "migration geometry backup");
            string migrated = File.ReadAllText(store.CatalogPath); store.Migrate();
            Check(XmlData.Read<Catalog>(migrated).Parts.All(x => x.Asset == null && x.LayoutAsset == null && x.SymbolId != null), "new catalog writes geometry references separately from part metadata");
            Check(File.ReadAllText(store.CatalogPath) == migrated && Directory.GetDirectories(Path.Combine(root, "migration-backups")).Length == 1, "repeated disk migration creates no duplicates or revisions");
            c = store.Load();
            store.Save(c.Revision, data => { data.Parts[1].SymbolId = data.Parts[0].SymbolId; data.Parts[1].LayoutId = data.Parts[0].LayoutId; });
            c = store.Load();
            Check(CatalogModel.Geometry(c, c.Parts[0], false).Id == CatalogModel.Geometry(c, c.Parts[1], false).Id, "different parts share one symbol by ID");
            Check(CatalogModel.Geometry(c, c.Parts[0], true).Id == CatalogModel.Geometry(c, c.Parts[1], true).Id, "different parts share one layout by ID");
            var symbol = CatalogModel.Geometry(c, c.Parts[0], false); var layout = CatalogModel.Geometry(c, c.Parts[0], true);
            store.Save(c.Revision, data => { data.Symbols.Single(x => x.Id == symbol.Id).Name = "renamed symbol"; data.Layouts.Single(x => x.Id == layout.Id).Name = "renamed layout"; });
            c = store.Load();
            Check(c.Parts.All(x => x.SymbolName == "renamed symbol" && x.LayoutName == "renamed layout"), "rename preserves stable ID connections");
            Throws(() => store.Save(c.Revision, data => CatalogModel.DeleteGeometry(data, symbol.Id, false)), "referenced symbol deletion blocked");
            Throws(() => store.Save(c.Revision, data => CatalogModel.DeleteGeometry(data, layout.Id, true)), "referenced layout deletion blocked");
            store.Save(c.Revision, data => data.Parts.ForEach(x => x.Active = false)); c = store.Load();
            Throws(() => store.Save(c.Revision, data => CatalogModel.DeleteGeometry(data, symbol.Id, false)), "archived parts protect geometry reference");
            var duplicate = CatalogModel.Duplicate(c.Parts[0]);
            Check(duplicate.Id != p.Id && duplicate.Code == "" && duplicate.Active && duplicate.SymbolId == symbol.Id && duplicate.LayoutId == layout.Id, "part duplicate active new identity with shared links");
            duplicate.Code = "C"; duplicate.Model = "new-model"; duplicate.Spec = "new-spec"; duplicate.Active = true;
            Check(c.Parts[0].Model == "2P-A" && c.Parts[0].Spec != "new-spec", "duplicate edit preserves source");
            store.Save(c.Revision, data => data.Parts.Add(duplicate)); c = store.Load();
            var a = new Instance { Part = c.Parts[0].Copy(), GeometryKind = "Symbol", GeometryId = symbol.Id, Tag = "01", Panel = "CP1", DeviceNumberInputEnabled = true };
            var b = new Instance { Part = c.Parts[1].Copy(), GeometryKind = "Layout", GeometryId = layout.Id, Tag = "02", Panel = "CP1", DeviceNumberInputEnabled = true };
            string value;
            Check(AttributeValues.TryGet("UNIT_MODEL", a, out value) && value == "2P-A" && AttributeValues.TryGet("UNIT_MODEL", b, out value) && value == "2P-B", "shared geometry model values independent");
            Check(AttributeValues.TryGet("UNIT_MAKER", a, out value) && value == "MakerA" && AttributeValues.TryGet("UNIT_MAKER", b, out value) && value == "MakerB", "shared geometry maker values independent");
            var snap = XmlData.Read<Instance>(XmlData.Write(a));
            store.Save(c.Revision, data => data.Parts[0].Model = "changed catalog model");
            Check(a.Part.Model == "2P-A" && snap.Part.Model == "2P-A", "catalog edits preserve inserted snapshots");
            Check(snap.GeometryKind == "Symbol" && snap.GeometryId == symbol.Id && snap.Part.Id == p.Id, "instance part identity and geometry kind round trip");
            var direct = InstanceFactory.Direct(p, layout, true, new UserSettings { AskDeviceNumber = false });
            Check(direct.GeometryKind == "Layout" && direct.GeometryId == layout.Id && direct.DeviceNumberInputEnabled == false && direct.LinkId == null && direct.LayoutSourceHandle == null, "production direct layout factory stores independent identity and policy");
            direct.Part.Model = "independent edit";
            Check(p.Model == "2P-A", "production direct factory copies part values");
            snap.Tag = "CURRENT01"; snap.Panel = "PANEL-SNAPSHOT"; snap.LinkOwnerHandle = "A1"; snap.LinkId = "stable-link";
            var fromSymbol = InstanceFactory.FromSymbol(snap, layout);
            Check(fromSymbol.Tag == "CURRENT01" && fromSymbol.Panel == "PANEL-SNAPSHOT" && fromSymbol.Part.Model == "2P-A" && fromSymbol.LayoutSourceHandle == "A1", "production symbol layout factory uses stored information");
            fromSymbol.Part.Model = "independent layout edit";
            Check(snap.Part.Model == "2P-A", "production linked layout factory copies snapshot values");
            Check(b.LinkId == null && b.LayoutSourceHandle == null && BomLogic.Build(new[] { b }).Rows.Single()[6] == "1", "direct layout BOM without source or link");
            var linked = XmlData.Read<Instance>(XmlData.Write(b)); linked.LayoutSourceHandle = "DELETED"; linked.LinkId = "link";
            Check(BomLogic.Build(new[] { linked }).Rows.Single()[6] == "1", "linked layout BOM after source deletion");
            Check(BomLogic.Build(new[] { a, b }).Rows.Count == 2, "BOM groups by part code rather than geometry");
            linked.Part = a.Part.Copy(); linked.Tag = a.Tag;
            var both = BomLogic.Build(new[] { a, linked });
            Check(both.Rows.Single()[6] == "2" && both.Errors.Count == 1, "symbol and linked layout counted twice with duplicate error");
            Throws(() => BomLogic.Csv(both), "duplicate number blocks CSV");
            a.Tag = linked.Tag = "";
            Check(BomLogic.Build(new[] { a, linked }).Rows.Single()[6] == "2" && BomLogic.Build(new[] { a, linked }).Errors.Count == 0, "unnumbered selected views counted individually");
            b.DeviceNumberInputEnabled = false; b.Tag = ""; b.OriginalDeviceNumber = "CB00";
            Check(!AttributeValues.TryGet("UNIT_NO", b, out value, false) && DeviceNumbers.Resolve(b, new[] { "CB00" }) == "", "layout input OFF original text remains unnumbered");
            Check(DeviceNumbers.Resolve(b, new[] { "CB01" }) == "CB01", "manual layout UNIT_NO edit reflected after input OFF");
            var forwarded = new Instance { Part = b.Part.Copy(), Tag = DeviceNumbers.Resolve(b, new[] { "CB00" }), DeviceNumberInputEnabled = true, GeometryKind = "Layout" };
            Check(AttributeValues.TryGet("UNIT_NO", forwarded, out value) && value == "", "symbol path does not interpret original text as device number");
            c = store.Load();
            Throws(() => store.Save(c.Revision, data => data.Parts[0].SymbolId = Guid.NewGuid().ToString("N")), "dangling symbol ID rejected");
            Throws(() => store.Save(c.Revision, data => data.Parts[0].LayoutId = Guid.NewGuid().ToString("N")), "dangling layout ID rejected");
            store.Save(c.Revision, data => { data.Parts[0].LayoutId = null; data.Parts[0].LayoutAsset = null; }); c = store.Load();
            Check(c.Parts[0].LayoutId == null, "part without layout remains valid");
            Throws(() => CatalogModel.Geometry(c, c.Parts[0], true), "missing layout registration guidance");
            var unused = c.Symbols.Single(g => g.Id == CatalogModel.LegacyGeometryId(q.Id, false));
            store.Save(c.Revision, data => CatalogModel.DeleteGeometry(data, unused.Id, false));
            Check(File.Exists(store.AssetPath(p.Asset)), "geometry deletion retains recovery DWG");
            return count;
        }
        finally { Directory.Delete(root, true); }
    }
}
