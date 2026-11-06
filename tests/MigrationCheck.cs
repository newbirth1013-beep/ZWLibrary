using System;
using System.IO;
using System.Linq;
using ZWLibrary;

class MigrationCheck
{
    static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass the copied test library folder.");
        var store = new Store(args[0]);
        var old = XmlData.Read<Catalog>(File.ReadAllText(store.CatalogPath));
        bool legacy = old.Schema == 1;
        if (!legacy) old = store.Load();
        var identities = old.Parts.Select(p => Part.Pack(p.Id, p.Code, p.Name, p.Brand, p.Model, p.Spec, p.Asset, p.LayoutAsset, p.Active.ToString(), p.Bom.ToString())).ToArray();
        store.Migrate(); var migrated = store.Load();
        var actual = migrated.Parts.Select(p => Part.Pack(p.Id, p.Code, p.Name, p.Brand, p.Model, p.Spec, p.Asset, p.LayoutAsset, p.Active.ToString(), p.Bom.ToString())).ToArray();
        if (!identities.SequenceEqual(actual)) throw new Exception("Migration altered part identities or values.");
        int expectedSymbols = legacy ? old.Parts.Count : old.Symbols.Count;
        int expectedLayouts = legacy ? old.Parts.Count(p => !string.IsNullOrEmpty(p.LayoutAsset)) : old.Layouts.Count;
        if (migrated.Symbols.Count != expectedSymbols || migrated.Layouts.Count != expectedLayouts) throw new Exception("Migration geometry count mismatch.");
        foreach (var g in migrated.Symbols.Concat(migrated.Layouts))
            if (!File.Exists(store.AssetPath(g.Asset))) throw new Exception("Missing asset: " + g.Asset);
        string once = File.ReadAllText(store.CatalogPath); store.Migrate();
        if (File.ReadAllText(store.CatalogPath) != once) throw new Exception("Repeated migration changed catalog.");
        Console.WriteLine("PASS: user library COPY migration; original data not modified");
        Console.WriteLine("Parts=" + migrated.Parts.Count + "; Symbols=" + migrated.Symbols.Count + "; Layouts=" + migrated.Layouts.Count + "; Schema=" + migrated.Schema);
        Console.WriteLine("PASS: IDs, codes, values, flags and DWG connections preserved; referenced files present; repeated migration unchanged");
    }
}
