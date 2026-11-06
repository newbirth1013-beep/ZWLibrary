using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Runtime;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;

namespace ZWLibrary
{
    public partial class Plugin
    {
        private static ObjectIdCollection ModelEntities(Database db)
        {
            var ids = new ObjectIdCollection();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in model) ids.Add(id);
                if (ids.Count == 0) throw new InvalidOperationException("모형 공간에 도형이 없습니다.");
                ValidateEntities(ids, tr, new HashSet<ObjectId>(), 0);
            }
            return ids;
        }
        [CommandMethod("ZLIBLAYPREVIEW")]
        public void PreviewLayout()
        {
            Run("ZLIBLAYPREVIEW", (doc, partId) =>
            {
                if (partId == null || panel == null) throw new InvalidOperationException("ZLIB 목록에서 부품을 선택하고 배치 미리보기를 누르세요.");
                var store = Settings.Current;
                var part = store.Load().Parts.FirstOrDefault(p => p.Id == partId);
                if (part == null || string.IsNullOrEmpty(part.LayoutAsset)) throw new InvalidOperationException("배치도형이 등록되지 않았습니다.");
                var data = PreviewData.Read(store.AssetPath(part.LayoutAsset));
                panel.SetPreview(part, data, store.Root, "배치도형");
                doc.Editor.WriteMessage("\n배치도형 미리보기: " + part.Model + " / " + part.Brand + "\n");
            });
        }
        private static void ApplyLayoutAttributes(BlockReference br, Instance data, Transaction tr, HashSet<ObjectId> visited, int depth, bool updateDeviceNumber = true)
        {
            if (depth > 64) throw new InvalidOperationException("블록 중첩 한도 초과");
            ApplyAttributes(br, data, tr, updateDeviceNumber);
            if (!visited.Add(br.BlockTableRecord)) return;
            var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in def)
            {
                var child = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (child != null) ApplyLayoutAttributes(child, data, tr, visited, depth + 1, updateDeviceNumber);
            }
        }
        private static void CheckExistingLayout(Database db, Transaction tr, string linkId, string handle)
        {
            if (ReadLayoutLinks(db, tr).Contains(Part.Pack(linkId, handle)))
                throw new InvalidOperationException("이 심볼에 연결된 배치도형이 이미 있습니다. 기존 배치도형을 이동하거나 삭제한 후 다시 삽입하세요.");
        }
        private static HashSet<string> ReadLayoutLinks(Database db, Transaction tr)
        {
            var links = new HashSet<string>(StringComparer.Ordinal);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in bt)
            {
                var space = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead); if (!space.IsLayout) continue;
                foreach (ObjectId child in space)
                {
                    var br = tr.GetObject(child, OpenMode.ForRead) as BlockReference; if (br == null) continue;
                    var data = ReadInstance(br, tr);
                    if (data != null && !string.IsNullOrEmpty(data.LinkId) && !string.IsNullOrEmpty(data.LayoutSourceHandle) && data.LinkOwnerHandle == br.Handle.ToString())
                        links.Add(Part.Pack(data.LinkId, data.LayoutSourceHandle));
                }
            }
            return links;
        }
    }
}
