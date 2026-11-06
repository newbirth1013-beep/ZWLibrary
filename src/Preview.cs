using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Geometry;

namespace ZWLibrary
{
    // Read-only XY outline preview. No CAD objects escape the side-database transaction.
    internal sealed class PreviewData
    {
        internal sealed class Stroke { public PointF[] Points; }
        internal sealed class Caption { public string Text; public RectangleF Bounds; }
        internal readonly List<Stroke> Lines = new List<Stroke>();
        internal readonly List<Caption> Texts = new List<Caption>();
        internal int Skipped, Visited, Points;
        internal bool Limited;
        internal double MinX = double.PositiveInfinity, MinY = double.PositiveInfinity;
        internal double MaxX = double.NegativeInfinity, MaxY = double.NegativeInfinity;
        internal bool HasGeometry { get { return Lines.Count + Texts.Count > 0; } }
        private PointF Point(Point3d p)
        {
            if (double.IsNaN(p.X) || double.IsNaN(p.Y) || Math.Abs(p.X) > 1e15 || Math.Abs(p.Y) > 1e15)
                throw new InvalidDataException("미리보기 좌표 범위 초과");
            MinX = Math.Min(MinX, p.X); MinY = Math.Min(MinY, p.Y);
            MaxX = Math.Max(MaxX, p.X); MaxY = Math.Max(MaxY, p.Y);
            return new PointF((float)p.X, (float)p.Y);
        }
        internal static PreviewData Read(string path)
        {
            var data = new PreviewData();
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(path, FileShare.Read, true, ""); db.CloseInput(true);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in model)
                    {
                        data.ReadEntity((Entity)tr.GetObject(id, OpenMode.ForRead), tr, Matrix3d.Identity, 0, false);
                        if (data.Limited) break;
                    }
                }
            }
            return data;
        }
        private void ReadEntity(Entity e, Transaction tr, Matrix3d transform, int depth, bool insideBlock)
        {
            if (Limited) return;
            if (++Visited > 12000 || Points > 150000 || depth > 32) { Limited = true; return; }
            try
            {
                if (!e.Visible) return;
                if (!e.LayerId.IsNull)
                {
                    var layer = tr.GetObject(e.LayerId, OpenMode.ForRead) as LayerTableRecord;
                    if (layer != null && (layer.IsOff || layer.IsFrozen)) return;
                }
                var br = e as BlockReference;
                if (br != null)
                {
                    if (br is MInsertBlock) { Skipped++; return; }
                    var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    if (def.IsFromExternalReference) { Skipped++; return; }
                    foreach (ObjectId id in def)
                    {
                        ReadEntity((Entity)tr.GetObject(id, OpenMode.ForRead), tr, transform * br.BlockTransform, depth + 1, true);
                        if (Limited) break;
                    }
                    foreach (ObjectId id in br.AttributeCollection)
                        ReadEntity((Entity)tr.GetObject(id, OpenMode.ForRead), tr, transform, depth + 1, false);
                    return;
                }
                var ad = e as AttributeDefinition;
                if (ad != null && (ad.Invisible || (insideBlock && !ad.Constant))) return;
                var ar = e as AttributeReference;
                if (ar != null && ar.Invisible) return;
                var text = e as DBText;
                var mtext = e as MText;
                if (text != null || mtext != null)
                {
                    string content = text != null ? text.TextString : mtext.Text;
                    if (string.IsNullOrWhiteSpace(content)) return;
                    Extents3d ext = e.GeometricExtents; ext.TransformBy(transform);
                    var min = Point(ext.MinPoint); var max = Point(ext.MaxPoint);
                    Texts.Add(new Caption { Text = content, Bounds = RectangleF.FromLTRB(min.X, min.Y, max.X, max.Y) });
                    return;
                }
                var curve = e as Curve;
                if (curve != null)
                {
                    if (curve is Ray || curve is Xline) { Skipped++; return; }
                    double start = curve.StartParam, end = curve.EndParam;
                    int count = curve is Line ? 1 : 128;
                    if (curve is Polyline || curve is Polyline2d || curve is Polyline3d)
                        count = Math.Min(2048, Math.Max(16, (int)Math.Min(2048, Math.Abs(end - start) * 16)));
                    var points = new PointF[count + 1];
                    for (int i = 0; i <= count; i++)
                        points[i] = Point(curve.GetPointAtParameter(start + (end - start) * i / count).TransformBy(transform));
                    Lines.Add(new Stroke { Points = points }); Points += points.Length;
                    return;
                }
                // Dimensions and composite objects may expose drawable 2D pieces.
                using (var pieces = new DBObjectCollection())
                {
                    try
                    {
                        e.Explode(pieces);
                        if (pieces.Count == 0) Skipped++;
                        foreach (DBObject piece in pieces)
                        {
                            var entity = piece as Entity;
                            if (entity != null) ReadEntity(entity, tr, transform, depth + 1, insideBlock);
                            else Skipped++;
                        }
                    }
                    finally { foreach (DBObject piece in pieces) piece.Dispose(); }
                }
            }
            catch (System.Exception) { Skipped++; }
        }
    }
}
