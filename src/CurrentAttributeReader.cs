using System;
using System.Collections.Generic;
using System.Linq;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;

namespace ZWLibrary
{
    public partial class Plugin
    {
        private static Dictionary<string, List<string>> AttributeMap(BlockReference br, Transaction tr)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in br.AttributeCollection)
            {
                var ar = tr.GetObject(id, OpenMode.ForRead) as AttributeReference;
                if (ar == null) continue;
                string text = ar.TextString;
                if (ar.IsMTextAttribute) using (var mt = ar.MTextAttribute) text = mt.Text;
                AddAttribute(map, ar.Tag, text);
            }
            var definition = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in definition)
            {
                var ad = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                if (ad != null && ad.Constant) AddAttribute(map, ad.Tag, ad.TextString);
            }
            return map;
        }
        private static void AddAttribute(Dictionary<string, List<string>> map, string tag, string value)
        {
            tag = (tag ?? "").Trim().ToUpperInvariant(); if (!CurrentAttributes.Tags.Contains(tag)) return;
            List<string> values; if (!map.TryGetValue(tag, out values)) map[tag] = values = new List<string>(); values.Add(value ?? "");
        }
        private static void NestedAttributes(BlockReference br, Transaction tr, Dictionary<string, List<string>> map, HashSet<ObjectId> path, int depth, ref int visited)
        {
            if (depth > 64 || ++visited > 12000) throw new InvalidOperationException("속성 읽기 블록 중첩/객체 한도를 초과했습니다.");
            if (!path.Add(br.BlockTableRecord)) throw new InvalidOperationException("순환 중첩 블록 속성을 읽을 수 없습니다.");
            var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in def)
            {
                var child = tr.GetObject(id, OpenMode.ForRead) as BlockReference; if (child == null) continue;
                var childDef = (BlockTableRecord)tr.GetObject(child.BlockTableRecord, OpenMode.ForRead);
                if (child is MInsertBlock || child.IsDynamicBlock || childDef.IsFromExternalReference) throw new InvalidOperationException("동적/MINSERT/외부참조의 중첩 속성은 지원하지 않습니다.");
                foreach (var item in AttributeMap(child, tr)) foreach (var value in item.Value) AddAttribute(map, item.Key, value);
                NestedAttributes(child, tr, map, path, depth + 1, ref visited);
            }
            path.Remove(br.BlockTableRecord);
        }
        private static Instance ReadCurrentInstance(BlockReference br, Transaction tr)
        {
            var stored = ReadInstance(br, tr); if (stored == null) return null;
            var outer = AttributeMap(br, tr); var nested = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (CurrentAttributes.Tags.Any(tag => !outer.ContainsKey(tag))) { int visited = 0; NestedAttributes(br, tr, nested, new HashSet<ObjectId>(), 0, ref visited); }
            var current = CurrentAttributes.Read(stored, outer, nested); current.PhysicalKey = br.Handle.ToString(); return current;
        }
        private sealed class SelectedData
        {
            internal readonly List<Instance> Instances = new List<Instance>();
            internal readonly List<string> Errors = new List<string>();
            internal int Excluded, Total;
        }
        private static SelectedData ReadSelected(Database db, SelectionSet selection)
        {
            var result = new SelectedData { Total = selection.Count };
            using (var tr = db.TransactionManager.StartTransaction())
                foreach (ObjectId id in selection.GetObjectIds())
                {
                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) { result.Excluded++; continue; }
                    try
                    {
                        var owner = (BlockTableRecord)tr.GetObject(br.OwnerId, OpenMode.ForRead);
                        var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                        if (!owner.IsLayout || br is MInsertBlock || br.IsDynamicBlock || def.IsFromExternalReference) { result.Excluded++; continue; }
                        var data = ReadCurrentInstance(br, tr); if (data == null) result.Excluded++; else result.Instances.Add(data);
                    }
                    catch (System.Exception ex) { result.Errors.Add("블록 " + br.Handle + ": " + ex.Message); }
                }
            result.Instances.Sort((a, b) => string.CompareOrdinal(a.PhysicalKey, b.PhysicalKey));
            return result;
        }
    }
}
