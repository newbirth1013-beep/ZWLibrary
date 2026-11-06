using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;

namespace ZWLibrary
{
    public partial class Plugin
    {
        private sealed class LayoutJob
        {
            internal ObjectId SymbolId;
            internal Instance Symbol, Data;
            internal string Path;
            internal LayoutBounds Bounds;
        }
        private sealed class LayoutSources : IDisposable
        {
            private readonly Dictionary<string, Database> sources = new Dictionary<string, Database>(StringComparer.OrdinalIgnoreCase);
            private readonly Queue<string> order = new Queue<string>();
            private readonly Dictionary<string, ObjectIdCollection> entities = new Dictionary<string, ObjectIdCollection>(StringComparer.OrdinalIgnoreCase);
            internal Database Get(string path)
            {
                Database source; if (sources.TryGetValue(path, out source)) return source;
                source = new Database(false, true);
                try { source.ReadDwgFile(path, FileShare.Read, true, ""); source.CloseInput(true); }
                catch { source.Dispose(); throw; }
                if (sources.Count == 4) { string old = order.Dequeue(); sources[old].Dispose(); sources.Remove(old); entities.Remove(old); }
                sources.Add(path, source); order.Enqueue(path); return source;
            }
            internal ObjectIdCollection Ids(string path)
            {
                ObjectIdCollection ids; if (entities.TryGetValue(path, out ids)) return ids;
                ids = ModelEntities(Get(path)); entities.Add(path, ids); return ids;
            }
            public void Dispose() { foreach (var source in sources.Values) source.Dispose(); sources.Clear(); entities.Clear(); }
        }
        private static void InsertSelectedLayouts(Document doc)
        {
            var selected = doc.Editor.SelectImplied();
            if (selected.Status != PromptStatus.OK)
                selected = doc.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\n배치도형으로 삽입할 관리 심볼을 여러 개 선택 (드래그 가능): " });
            if (selected.Status != PromptStatus.OK) return;
            doc.Editor.SetImpliedSelection(new ObjectId[0]);
            var store = Settings.Current; var catalog = store.Load(); var options = Settings.Load(); LayoutPacking.Validate(options);
            var jobs = new List<LayoutJob>(); var warnings = new List<string>(); int ignored = 0; HashSet<string> existingLinks;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                existingLinks = ReadLayoutLinks(doc.Database, tr);
                foreach (var id in selected.Value.GetObjectIds().Distinct())
                {
                    var symbol = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (symbol == null) { ignored++; continue; }
                    try
                    {
                        var owner = (BlockTableRecord)tr.GetObject(symbol.OwnerId, OpenMode.ForRead);
                        var def = (BlockTableRecord)tr.GetObject(symbol.BlockTableRecord, OpenMode.ForRead);
                        if (symbol.OwnerId != doc.Database.CurrentSpaceId || !owner.IsLayout || symbol.IsDynamicBlock || symbol is MInsertBlock || def.IsFromExternalReference) { ignored++; continue; }
                        var data = ReadCurrentInstance(symbol, tr);
                        if (data == null || data.GeometryKind == "Layout" || !string.IsNullOrEmpty(data.LayoutSourceHandle)) { ignored++; continue; }
                        string handle = symbol.Handle.ToString();
                        if (data.LinkOwnerHandle != handle || string.IsNullOrEmpty(data.LinkId)) { data.LinkId = Guid.NewGuid().ToString("N"); data.LinkOwnerHandle = handle; }
                        var part = catalog.Parts.SingleOrDefault(p => p.Id == data.Part.Id);
                        if (part == null) throw new InvalidOperationException("연결된 개별 부품이 목록에 없습니다.");
                        var geometry = CatalogModel.Geometry(catalog, part, true);
                        string path = store.AssetPath(geometry.Asset); if (!File.Exists(path)) throw new FileNotFoundException("배치 DWG 파일이 없습니다.");
                        if (existingLinks.Contains(Part.Pack(data.LinkId, handle))) throw new InvalidOperationException("이 심볼에 연결된 배치도형이 이미 있습니다.");
                        if (!string.Equals(BomLogic.Clean(data.Part.Model), BomLogic.Clean(part.Model), StringComparison.Ordinal) || !string.Equals(BomLogic.Clean(data.Part.Brand), BomLogic.Clean(part.Brand), StringComparison.Ordinal))
                        {
                            if (System.Windows.Forms.MessageBox.Show("선택 심볼의 현재 모델·제조사가 연결 부품과 다릅니다.\n기기번호: " + data.Tag + "\n모델: " + data.Part.Model + "\n제조사: " + data.Part.Brand + "\n연결된 배치도형을 그대로 사용할까요? [아니오]는 이 심볼만 제외합니다.", "배치도형 확인", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question, System.Windows.Forms.MessageBoxDefaultButton.Button2) != System.Windows.Forms.DialogResult.Yes) { ignored++; continue; }
                        }
                        jobs.Add(new LayoutJob { SymbolId = id, Symbol = data, Data = InstanceFactory.FromSymbol(data, geometry), Path = path });
                    }
                    catch (System.Exception ex) { warnings.Add("심볼 " + symbol.Handle + ": " + ex.Message); }
                }
            }
            jobs = jobs.OrderBy(j => j.Data.Tag, new NaturalNumberComparer()).ThenBy(j => j.SymbolId.Handle.Value).ToList();
            if (warnings.Count > 0) doc.Editor.WriteMessage("\n삽입 제외:\n" + string.Join("\n", warnings) + "\n");
            if (jobs.Count == 0) { CadApp.ShowAlertDialog("삽입할 심볼이 없습니다. 일반 객체/배치/미지원 제외 " + ignored + "개\n" + string.Join("\n", warnings)); return; }
            double scale;
            if (doc.Database.Insunits == UnitsValue.Undefined)
            {
                var unit = doc.Editor.GetDouble(new PromptDoubleOptions("\n대상 도면 1단위의 길이(mm): ") { AllowNegative = false, AllowZero = false, DefaultValue = 1, UseDefaultValue = true });
                if (unit.Status != PromptStatus.OK) return; scale = 1 / unit.Value;
            }
            else scale = UnitsConverter.GetConversionFactor(UnitsValue.Millimeters, doc.Database.Insunits);
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) throw new InvalidOperationException("단위 변환 오류");
            doc.Editor.WriteMessage("\n삽입 대상 " + jobs.Count + "개 / 일반·배치 제외 " + ignored + "개 / 오류 제외 " + warnings.Count + "개. 기기번호 자연순 (빈 번호는 뒤)으로 진행합니다.\n");
            if (options.BatchLayoutInsertion) InsertLayoutGrid(doc, jobs, options, scale);
            else
            {
                int inserted = 0;
                var links = existingLinks;
                using (var sources = new LayoutSources())
                try
                {
                    foreach (var job in jobs)
                    {
                        doc.Editor.WriteMessage("\n[" + (inserted + 1) + "/" + jobs.Count + "] " + job.Data.Tag + " / " + job.Data.Part.Model);
                        if (!InsertManaged(doc, job.Path, job.Data, options, false, job.SymbolId, job.Symbol, scale, null, null, false, links, sources.Get(job.Path), sources.Ids(job.Path))) break;
                        inserted++;
                        // Let the next native prompt process graphics; do not queue a forced
                        // graphics flush while this command continues requesting points.
                    }
                }
                finally { if (inserted > 0) doc.Editor.Regen(); doc.Editor.WriteMessage("\n연속 삽입 완료 " + inserted + "/" + jobs.Count + "개. 취소하면 미삽입 항목만 중단하며 완료한 항목은 유지합니다.\n"); }
            }
        }
        private static LayoutBounds ReadLayoutBounds(LayoutJob job, Database source, ObjectIdCollection ids, double scale, double angle)
        {
            using (var temporary = new Database(true, true))
            {
                var previous = HostApplicationServices.WorkingDatabase;
                string stage = "도형 준비";
                try
                {
                    HostApplicationServices.WorkingDatabase = temporary;
                    ObjectId blockId;
                    stage = "임시 블록 생성/속성 반영";
                    using (var tr = temporary.TransactionManager.StartTransaction())
                    {
                        blockId = CreateManagedBlock(temporary, tr, source, ids, job.Data.Copy(), scale, angle, Point3d.Origin, Matrix3d.Identity, false).ObjectId;
                        tr.Commit();
                    }
                    stage = "외곽 크기 계산";
                    using (var tr = temporary.TransactionManager.StartTransaction())
                    {
                        Extents3d? bounds = null;
                        AccumulateLayoutBounds((Entity)tr.GetObject(blockId, OpenMode.ForRead), tr, Matrix3d.Identity, ref bounds, 0);
                        if (!bounds.HasValue) throw new InvalidOperationException("크기를 가진 표시 도형이 없습니다.");
                        return new LayoutBounds { MinX = bounds.Value.MinPoint.X, MinY = bounds.Value.MinPoint.Y, MaxX = bounds.Value.MaxPoint.X, MaxY = bounds.Value.MaxPoint.Y };
                    }
                }
                catch (System.Exception ex) { throw new InvalidOperationException(stage + ": " + ex.Message, ex); }
                finally { HostApplicationServices.WorkingDatabase = previous; }
            }
        }
        private static void AccumulateLayoutBounds(Entity entity, Transaction tr, Matrix3d transform, ref Extents3d? total, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("크기 측정 블록 중첩 한도 초과");
            if (!entity.Visible) return;
            var definition = entity as AttributeDefinition;
            if (definition != null && (definition.Invisible || !definition.Constant)) return;
            var attribute = entity as AttributeReference; if (attribute != null && attribute.Invisible) return;
            var text = entity as DBText; if (text != null && string.IsNullOrWhiteSpace(text.TextString)) return;
            var mtext = entity as MText; if (mtext != null && string.IsNullOrWhiteSpace(mtext.Text)) return;
            var block = entity as BlockReference;
            if (block != null)
            {
                var record = (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead);
                foreach (ObjectId id in record) AccumulateLayoutBounds((Entity)tr.GetObject(id, OpenMode.ForRead), tr, transform * block.BlockTransform, ref total, depth + 1);
                foreach (ObjectId id in block.AttributeCollection) AccumulateLayoutBounds((Entity)tr.GetObject(id, OpenMode.ForRead), tr, transform, ref total, depth + 1);
                return;
            }
            Extents3d bounds;
            try { bounds = entity.GeometricExtents; }
            catch (System.Exception ex) { throw new InvalidOperationException(entity.GetType().Name + " 크기 계산: " + ex.Message, ex); }
            bounds.TransformBy(transform);
            if (!total.HasValue) total = bounds;
            else { var value = total.Value; value.AddExtents(bounds); total = value; }
        }
        private static void InsertLayoutGrid(Document doc, List<LayoutJob> jobs, UserSettings options, double scale)
        {
            RefreshInsertionOsnap(doc, options);
            var first = doc.Editor.GetPoint("\n일괄 삽입: 첫 배치도형의 삽입 위치: "); if (first.Status != PromptStatus.OK) return;
            double angle = 0;
            if (options.AskRotation)
            {
                var rotation = doc.Editor.GetAngle(new PromptAngleOptions("\n전체 배치도형 회전각도 <0>: ") { DefaultValue = 0, UseDefaultValue = true });
                if (rotation.Status != PromptStatus.OK) return; angle = rotation.Value;
            }
            var valid = new List<LayoutJob>();
            using (var sources = new LayoutSources())
            {
            var failures = new List<string>();
            foreach (var job in jobs)
                try { job.Bounds = ReadLayoutBounds(job, sources.Get(job.Path), sources.Ids(job.Path), scale, angle); valid.Add(job); }
                catch (System.Exception ex) { string message = job.Data.Tag + " / " + job.Data.Part.Model + ": " + ex.Message; failures.Add(message); doc.Editor.WriteMessage("\n크기 확인 실패로 제외: " + message); }
            if (valid.Count == 0) throw new InvalidOperationException("크기를 확인할 수 있는 배치도형이 없습니다.\n" + string.Join("\n", failures));
            var offsets = LayoutPacking.Plan(valid.Select(j => j.Bounds).ToList(), options.LayoutColumns, options.LayoutGapMm * scale);
            var ucs = doc.Editor.CurrentUserCoordinateSystem;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var links = ReadLayoutLinks(doc.Database, tr);
                for (int index = 0; index < valid.Count; index++)
                {
                    var job = valid[index]; if (!links.Add(Part.Pack(job.Data.LinkId, job.Data.LayoutSourceHandle))) throw new InvalidOperationException("이 심볼에 연결된 배치도형이 이미 있습니다.");
                    {
                        var source = sources.Get(job.Path); var ids = sources.Ids(job.Path);
                        var position = new Point3d(first.Value.X + offsets[index].X, first.Value.Y + offsets[index].Y, first.Value.Z);
                        var br = CreateManagedBlock(doc.Database, tr, source, ids, job.Data, scale, angle, position, ucs, false);
                        job.Data.LinkOwnerHandle = br.Handle.ToString();
                        WriteInstance((BlockReference)tr.GetObject(job.SymbolId, OpenMode.ForWrite), job.Symbol, tr); WriteInstance(br, job.Data, tr);
                    }
                }
                tr.Commit();
            }
            doc.Editor.Regen(); doc.Editor.WriteMessage("\n일괄 삽입 완료: " + valid.Count + "개 / 가로 " + options.LayoutColumns + "개 / 도형 사이 최소 간격 " + options.LayoutGapMm + " mm\n");
            }
        }
    }
}
