using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.Runtime;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ZWLibrary.Commands))]

namespace ZWLibrary
{
    // Phase 0: read-only audit. This does not register or change drawing objects.
    public class Commands
    {
        [CommandMethod("ZLIBPERF")]
        public void PerformanceInfo()
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument; if (doc == null) return;
            var report = new StringBuilder("\nZWLibrary v0.8.4 성능 진단 (읽기 전용)\n");
            foreach (string name in new[] { "OSMODE", "3DOSMODE", "DYNMODE", "SELECTIONPREVIEW", "SELECTIONCYCLING", "ROLLOVERTIPS" })
                try { report.AppendLine(name + "=" + Convert.ToString(CadApp.GetSystemVariable(name), CultureInfo.InvariantCulture)); }
                catch { report.AppendLine(name + "=지원하지 않음"); }
            int definitions = 0, entities = 0, libraryDefinitions = 0, references = 0;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in table)
                {
                    var record = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (record.IsFromExternalReference || record.IsFromOverlayReference) continue;
                    definitions++;
                    if ((record.Name ?? "").StartsWith("ZWLIB_", StringComparison.OrdinalIgnoreCase)) libraryDefinitions++;
                    foreach (ObjectId child in record)
                    {
                        entities++;
                        if (record.IsLayout && tr.GetObject(child, OpenMode.ForRead) is BlockReference) references++;
                    }
                }
            }
            report.AppendLine("블록 정의=" + definitions + ", ZWLIB 정의=" + libraryDefinitions + ", 정의 내부 객체 합계=" + entities + ", 최상위 블록=" + references);
            using (var process = System.Diagnostics.Process.GetCurrentProcess()) report.AppendLine("ZWCAD 작업 메모리(MB)=" + (process.WorkingSet64 / 1048576));
            doc.Editor.WriteMessage(report.ToString());
        }
        [CommandMethod("ZLIBHELLO")]
        public void Hello()
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc != null)
                doc.Editor.WriteMessage("\nZWLibrary v0.8.4 로딩 성공. 공용 심볼·개별 부품·배치도형 관리: ZLIB / 도면 검사: ZLIBSCAN\n");
        }

        [CommandMethod("ZLIBSCAN")]
        public void Scan()
        {
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                string report = BuildReport(doc.Database, Path.GetFileName(doc.Name));
                using (var form = new Form())
                using (var text = new TextBox())
                using (var save = new Button())
                {
                    form.Text = "ZWLibrary — 도면 구조 검사 (읽기 전용)";
                    form.Width = 1000;
                    form.Height = 720;
                    form.StartPosition = FormStartPosition.CenterParent;
                    text.Multiline = true;
                    text.ReadOnly = true;
                    text.WordWrap = false;
                    text.ScrollBars = ScrollBars.Both;
                    text.Dock = DockStyle.Fill;
                    text.Text = report;
                    save.Text = "검사 결과 TXT 저장";
                    save.Dock = DockStyle.Bottom;
                    save.Height = 42;
                    save.Click += delegate
                    {
                        using (var dialog = new SaveFileDialog())
                        {
                            dialog.Filter = "텍스트 보고서 (*.txt)|*.txt";
                            dialog.FileName = Path.GetFileNameWithoutExtension(doc.Name) + "_scan.txt";
                            dialog.OverwritePrompt = true;
                            if (dialog.ShowDialog(form) != DialogResult.OK) return;
                            try
                            {
                                File.WriteAllText(dialog.FileName, report, new UTF8Encoding(true));
                                MessageBox.Show(form, "저장했습니다.");
                            }
                            catch (System.Exception ex)
                            {
                                MessageBox.Show(form, ex.Message, "저장 실패");
                            }
                        }
                    };
                    form.Controls.Add(text);
                    form.Controls.Add(save);
                    CadApp.ShowModalDialog(form);
                }
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\n검사 실패: " + ex.Message + "\n");
            }
        }

        private static string BuildReport(Database db, string name)
        {
            var report = new StringBuilder();
            report.AppendLine("ZWLibrary 도면 구조 검사 / v0.8.4");
            report.AppendLine("파일: " + name);
            report.AppendLine("검사 시각 UTC: " + DateTime.UtcNow.ToString("o"));
            report.AppendLine("삽입 단위: " + db.Insunits);
            report.AppendLine("DWG 삽입 기준점 INSBASE: " + db.Insbase);
            report.AppendLine("주의: 이 보고서는 BOM이 아닙니다. 직접 참조만 집계하며 중첩 수량을 전개하지 않습니다.");
            report.AppendLine("블록 및 속성 이름은 표시하지만 속성 값은 수집하지 않습니다.");
            report.AppendLine();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var modelRefs = new Dictionary<ObjectId, int>();
                var paperRefs = new Dictionary<ObjectId, int>();
                var nestedRefs = new Dictionary<ObjectId, int>();
                int nonBlockModel = 0;
                foreach (ObjectId id in table)
                {
                    var block = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    // Do not traverse external-reference contents.
                    if (block.IsFromExternalReference) continue;
                    foreach (ObjectId entityId in block)
                    {
                        var entity = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
                        var reference = entity as BlockReference;
                        if (reference == null)
                        {
                            if (block.Name == BlockTableRecord.ModelSpace) nonBlockModel++;
                            continue;
                        }
                        var counts = !block.IsLayout ? nestedRefs :
                            block.Name == BlockTableRecord.ModelSpace ? modelRefs : paperRefs;
                        int count;
                        counts.TryGetValue(reference.BlockTableRecord, out count);
                        counts[reference.BlockTableRecord] = count + 1;
                    }
                }
                report.AppendLine("모형 공간의 블록 외 객체 수: " + nonBlockModel);
                foreach (ObjectId id in table)
                {
                    var block = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (block.IsLayout) continue;
                    report.AppendLine("--------------------------------------------------");
                    report.AppendLine("블록: " + block.Name);
                    report.AppendLine("기준점: " + block.Origin);
                    report.AppendLine("외부참조: " + block.IsFromExternalReference + "; 익명: " + block.IsAnonymous);
                    report.AppendLine("직접 참조 수 — 모형: " + Count(modelRefs, id) +
                        ", 배치: " + Count(paperRefs, id) + ", 다른 블록 정의 내부: " + Count(nestedRefs, id));
                    if (block.IsFromExternalReference)
                    {
                        report.AppendLine("외부참조 내부 검사 제외");
                        continue;
                    }
                    var types = new SortedDictionary<string, int>();
                    var attributes = new List<string>();
                    foreach (ObjectId entityId in block)
                    {
                        var obj = tr.GetObject(entityId, OpenMode.ForRead);
                        string type = obj.GetType().Name;
                        int count;
                        types.TryGetValue(type, out count);
                        types[type] = count + 1;
                        var attribute = obj as AttributeDefinition;
                        if (attribute != null) attributes.Add(attribute.Tag);
                    }
                    foreach (var item in types)
                        report.AppendLine("  " + item.Key + ": " + item.Value.ToString(CultureInfo.InvariantCulture));
                    report.AppendLine("속성 태그: " + (attributes.Count == 0 ? "없음" : string.Join(", ", attributes.ToArray())));
                }
                // Read-only transaction: no write opens, commit or drawing save.
            }
            return report.ToString();
        }

        private static int Count(Dictionary<ObjectId, int> counts, ObjectId id)
        {
            int result;
            return counts.TryGetValue(id, out result) ? result : 0;
        }
    }
}
