using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;

namespace ZWLibrary
{
    public partial class Plugin
    {
        private static void RefreshInsertionOsnap(Document doc, UserSettings options)
        {
            try { InsertionOsnapRefresh.Apply(options.RefreshOsnapBeforePoint,
                () => Convert.ToInt32(ZwSoft.ZwCAD.ApplicationServices.Application.GetSystemVariable("OSMODE")),
                value => ZwSoft.ZwCAD.ApplicationServices.Application.SetSystemVariable("OSMODE", value)); }
            catch (System.Exception ex) { doc.Editor.WriteMessage("\n위치 선택 전 객체스냅 갱신에 실패했습니다: " + ex.Message); }
        }
        [CommandMethod("ZLIBINSERT")] public void Insert() { InsertDirect(false); }
        [CommandMethod("ZLIBLAYDIRECT")] public void InsertLayoutDirect() { InsertDirect(true); }
        private static void InsertDirect(bool layout)
        {
            Run(layout ? "ZLIBLAYDIRECT" : "ZLIBINSERT", (doc, partId) =>
            {
                var store = Settings.Current; var catalog = store.Load();
                var part = catalog.Parts.SingleOrDefault(p => p.Id == partId && p.Active);
                if (part == null) throw new InvalidOperationException("부품 목록에서 사용 중인 부품을 선택하세요.");
                Store.Validate(part);
                var geometry = CatalogModel.Geometry(catalog, part, layout);
                var options = Settings.Load();
                var data = InstanceFactory.Direct(part, geometry, layout, options);
                InsertManaged(doc, store.AssetPath(geometry.Asset), data, options, true, ObjectId.Null, null);
            });
        }
        [CommandMethod("ZLIBLAYINSERT", CommandFlags.UsePickSet)]
        public void InsertLayout()
        { Run("ZLIBLAYINSERT", (doc, unused) => InsertSelectedLayouts(doc)); }
        private static bool InsertManaged(Document doc, string path, Instance data, UserSettings options, bool direct, ObjectId symbolId, Instance symbolData, double? knownScale = null, Point3d? knownPoint = null, double? knownAngle = null, bool regen = true, HashSet<string> knownLinks = null, Database cachedSource = null, ObjectIdCollection cachedIds = null)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("등록 도형 파일이 없습니다.", path);
            double scale;
            if (knownScale.HasValue) scale = knownScale.Value;
            else if (doc.Database.Insunits == UnitsValue.Undefined)
            {
                var r = doc.Editor.GetDouble(new PromptDoubleOptions("\n대상 도면 1단위의 길이(mm): ") { AllowNegative = false, AllowZero = false, DefaultValue = 1, UseDefaultValue = true });
                if (r.Status != PromptStatus.OK) return false; scale = 1 / r.Value;
            }
            else scale = UnitsConverter.GetConversionFactor(UnitsValue.Millimeters, doc.Database.Insunits);
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) throw new InvalidOperationException("단위 변환 오류");
            Point3d position;
            if (knownPoint.HasValue) position = knownPoint.Value;
            else
            {
                if (data.GeometryKind == "Layout") RefreshInsertionOsnap(doc, options);
                var point = doc.Editor.GetPoint("\n삽입 위치: "); if (point.Status != PromptStatus.OK) return false; position = point.Value;
            }
            var ucs = doc.Editor.CurrentUserCoordinateSystem; double angle = knownAngle ?? 0;
            if (!knownAngle.HasValue && options.AskRotation)
            {
                var r = doc.Editor.GetAngle(new PromptAngleOptions("\n회전각도 <0>: ") { DefaultValue = 0, UseDefaultValue = true });
                if (r.Status != PromptStatus.OK) return false; angle = r.Value;
            }
            if (direct)
            {
                string prefix = (data.Part.DeviceNumberPrefix ?? "").Trim();
                string prompt = prefix.Length == 0 ? "\n기기번호 (Enter=미지정): " : "\n기기번호 입력 (" + prefix + " + 공백 + 입력값 / Enter=미지정): ";
                data.Tag = data.DeviceNumberInputEnabled == true ? DeviceNumbers.Compose(data.Part, AskString(doc.Editor, prompt)) : "";
                if (data.Tag == null) return false;
                data.Panel = options.AskPanelNumber ? AskString(doc.Editor, "\n판넬번호 (Enter=미지정): ") : "";
                if (data.Panel == null) return false;
            }
            var source = cachedSource ?? new Database(false, true);
            try
            {
                if (cachedSource == null) { source.ReadDwgFile(path, FileShare.Read, true, ""); source.CloseInput(true); }
                var ids = cachedIds ?? ModelEntities(source);
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    if (!symbolId.IsNull)
                    {
                        if (knownLinks == null) CheckExistingLayout(doc.Database, tr, data.LinkId, data.LayoutSourceHandle);
                        else if (knownLinks.Contains(Part.Pack(data.LinkId, data.LayoutSourceHandle))) throw new InvalidOperationException("이 심볼에 연결된 배치도형이 이미 있습니다.");
                    }
                    var br = CreateManagedBlock(doc.Database, tr, source, ids, data, scale, angle, position, ucs, direct);
                    if (!symbolId.IsNull)
                    {
                        data.LinkOwnerHandle = br.Handle.ToString();
                        var symbol = (BlockReference)tr.GetObject(symbolId, OpenMode.ForWrite); WriteInstance(symbol, symbolData, tr);
                    }
                    WriteInstance(br, data, tr); tr.Commit();
                }
                if (knownLinks != null && !symbolId.IsNull) knownLinks.Add(Part.Pack(data.LinkId, data.LayoutSourceHandle));
            }
            finally { if (cachedSource == null) source.Dispose(); }
            if (regen) doc.Editor.Regen();
            doc.Editor.WriteMessage("\n삽입 완료: " + data.Part.Model + " / " + data.Part.Brand + " / " + data.GeometryKind + "\n");
            return true;
        }
        private static BlockReference CreateManagedBlock(Database target, Transaction tr, Database source, ObjectIdCollection ids, Instance data, double scale, double angle, Point3d position, Matrix3d ucs, bool direct)
        {
            var bt = (BlockTable)tr.GetObject(target.BlockTableId, OpenMode.ForWrite);
            // Clone per insertion, including nested definitions: shared catalog assets remain immutable.
            var definition = new BlockTableRecord { Name = "ZWLIB_" + data.GeometryKind + "_" + Guid.NewGuid().ToString("N"), Origin = Point3d.Origin, Units = UnitsValue.Undefined };
            var defId = bt.Add(definition); tr.AddNewlyCreatedDBObject(definition, true);
            using (var map = new IdMapping()) source.WblockCloneObjects(ids, defId, map, DuplicateRecordCloning.MangleName, false);
            var space = (BlockTableRecord)tr.GetObject(target.CurrentSpaceId, OpenMode.ForWrite);
            var br = new BlockReference(Point3d.Origin, defId); br.SetDatabaseDefaults(target);
            br.ScaleFactors = new Scale3d(scale); br.Rotation = angle; br.Position = position; br.TransformBy(ucs);
            space.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
            foreach (ObjectId id in definition)
            {
                var ad = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                if (ad == null || ad.Constant) continue;
                var ar = new AttributeReference(); ar.SetAttributeFromBlock(ad, br.BlockTransform); ar.TextString = ad.TextString;
                br.AttributeCollection.AppendAttribute(ar); tr.AddNewlyCreatedDBObject(ar, true);
            }
            var top = AttributeMap(br, tr); var nested = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!top.ContainsKey("UNIT_NO")) { int visited = 0; NestedAttributes(br, tr, nested, new HashSet<ObjectId>(), 0, ref visited); }
            var original = CurrentAttributes.Values("UNIT_NO", top, nested) ?? new string[0];
            data.OriginalDeviceNumber = original.Length == 1 ? original[0] : null;
            if (direct && original.Length > 1) throw new InvalidOperationException("최상위 UNIT_NO 속성이 여러 개입니다. 하나만 남기세요.");
            ApplyLayoutAttributes(br, data, tr, new HashSet<ObjectId>(), 0, data.DeviceNumberInputEnabled != false);
            return br;
        }
    }
}
