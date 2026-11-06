using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;

namespace ZWLibrary
{
    public partial class Plugin
    {
        [CommandMethod("ZLIBADD")] public void RegisterSelected() { RegisterGeometry(false, false); }
        [CommandMethod("ZLIBDWG")] public void RegisterFile() { RegisterGeometry(false, true); }
        [CommandMethod("ZLIBLAYADD")] public void RegisterLayoutSelection() { RegisterGeometry(true, false); }
        [CommandMethod("ZLIBLAYDWG")] public void RegisterLayoutFile() { RegisterGeometry(true, true); }
        [CommandMethod("ZLIBSYMREPLACE")] public void ReplaceSymbol() { RegisterGeometry(false, false, true); }
        [CommandMethod("ZLIBLAYREPLACE")] public void ReplaceLayout() { RegisterGeometry(true, false, true); }
        private static void RegisterGeometry(bool layout, bool file, bool replace = false)
        {
            string command = layout ? (file ? "ZLIBLAYDWG" : "ZLIBLAYADD") : (file ? "ZLIBDWG" : "ZLIBADD");
            if (replace) command = layout ? "ZLIBLAYREPLACE" : "ZLIBSYMREPLACE";
            Run(command, (doc, partId) =>
            {
                var store = Settings.Current; var catalog = store.Load();
                string replaceId = replace ? partId : null;
                if (replace)
                {
                    var choice = MessageBox.Show("DWG 파일로 교체하려면 [예], 도면 선택으로 교체하려면 [아니오].\n연결 ID는 유지하며 이미 삽입한 객체는 변경하지 않습니다.", "공용 도형 교체", MessageBoxButtons.YesNoCancel);
                    if (choice == DialogResult.Cancel) return; file = choice == DialogResult.Yes; partId = null;
                }
                Part part = partId == null ? null : catalog.Parts.SingleOrDefault(p => p.Id == partId);
                if (partId != null && part == null) throw new InvalidOperationException("부품이 없습니다. 새로고침하세요.");
                var initial = new GeometryAsset { Name = part == null ? "" : part.Name + (layout ? " 배치" : " 심볼"), Category = part == null ? "미분류" : part.Category };
                if (replace) initial = (layout ? catalog.Layouts : catalog.Symbols).Single(g => g.Id == replaceId).Copy();
                if (file)
                {
                    string path;
                    using (var f = new OpenFileDialog { Filter = "DWG (*.dwg)|*.dwg" })
                    { if (f.ShowDialog() != DialogResult.OK) return; path = f.FileName; }
                    using (var source = new Database(false, true))
                    {
                        source.ReadDwgFile(path, FileShare.Read, true, ""); source.CloseInput(true);
                        var ids = ModelEntities(source);
                        if (!replace && part == null) initial.Name = Path.GetFileNameWithoutExtension(path);
                        using (var dialog = new GeometryDialog(initial, true, source.Insunits.ToString()))
                        {
                            if (CadApp.ShowModalDialog(dialog) != DialogResult.OK) return;
                            ExportGeometry(source, ids, source.Insbase, dialog.Value, dialog.Value.SourceUnitMm, store, catalog.Revision, layout, partId, replaceId);
                        }
                    }
                }
                else
                {
                    var selection = doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\n하나의 도형으로 등록할 최상위 객체 선택: " });
                    if (selection.Status != PromptStatus.OK) return;
                    var ids = new ObjectIdCollection(selection.Value.GetObjectIds());
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in ids)
                        {
                            var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                            if (e == null || e.OwnerId != doc.Database.CurrentSpaceId) throw new InvalidOperationException("현재 공간의 최상위 객체를 선택하세요.");
                        }
                        ValidateEntities(ids, tr, new HashSet<ObjectId>(), 0);
                    }
                    var point = doc.Editor.GetPoint("\n도형 삽입 기준점: "); if (point.Status != PromptStatus.OK) return;
                    var wcs = point.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem);
                    using (var dialog = new GeometryDialog(initial, true, doc.Database.Insunits.ToString()))
                    {
                        if (CadApp.ShowModalDialog(dialog) != DialogResult.OK) return;
                        ExportGeometry(doc.Database, ids, wcs, dialog.Value, dialog.Value.SourceUnitMm, store, catalog.Revision, layout, partId, replaceId);
                    }
                }
                doc.Editor.WriteMessage("\n" + (layout ? "배치도형" : "베이스 심볼") + " 등록 완료.\n");
            });
        }
        private static void ExportGeometry(Database source, ObjectIdCollection ids, Point3d basePoint, GeometryAsset geometry, double mm, Store store, int revision, bool layout, string partId, string replaceId)
        {
            using (var asset = new Database(true, true))
            {
                ObjectId modelId;
                using (var tr = asset.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(asset.BlockTableId, OpenMode.ForRead);
                    modelId = bt[BlockTableRecord.ModelSpace]; tr.Commit();
                }
                using (var map = new IdMapping()) source.WblockCloneObjects(ids, modelId, map, DuplicateRecordCloning.MangleName, false);
                using (var tr = asset.TransactionManager.StartTransaction())
                {
                    var model = (BlockTableRecord)tr.GetObject(modelId, OpenMode.ForRead);
                    foreach (ObjectId id in model)
                    {
                        var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                        e.TransformBy(Matrix3d.Scaling(mm, Point3d.Origin) * Matrix3d.Displacement(Point3d.Origin - basePoint));
                        if (!e.ExtensionDictionary.IsNull)
                        {
                            var dict = (DBDictionary)tr.GetObject(e.ExtensionDictionary, OpenMode.ForRead);
                            if (dict.Contains(RecordKey))
                            { dict.UpgradeOpen(); var old = tr.GetObject(dict.GetAt(RecordKey), OpenMode.ForWrite); dict.Remove(RecordKey); old.Erase(); }
                        }
                    }
                    asset.Insbase = Point3d.Origin; asset.Insunits = UnitsValue.Millimeters; tr.Commit();
                }
                geometry.BaseX = basePoint.X; geometry.BaseY = basePoint.Y; geometry.BaseZ = basePoint.Z;
                geometry.SourceUnitMm = mm; geometry.StoredUnit = "mm"; geometry.Asset = Guid.NewGuid().ToString("N") + ".dwg";
                string path = store.AssetPath(geometry.Asset); Directory.CreateDirectory(Path.GetDirectoryName(path));
                try
                {
                    asset.SaveAs(path, DwgVersion.AC1032);
                    store.Save(revision, c =>
                    {
                        var list = layout ? c.Layouts : c.Symbols;
                        if (replaceId == null) list.Add(geometry);
                        else list[list.FindIndex(g => g.Id == replaceId)] = geometry;
                        if (partId != null)
                        {
                            var p = c.Parts.Single(x => x.Id == partId);
                            if (layout) { p.LayoutId = geometry.Id; p.LayoutAsset = null; }
                            else { p.SymbolId = geometry.Id; p.Asset = null; }
                            p.Revision++;
                        }
                    });
                    PreviewCache.Clear();
                }
                catch { if (File.Exists(path)) File.Delete(path); throw; }
            }
        }
    }
}
