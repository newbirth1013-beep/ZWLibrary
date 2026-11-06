using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
using ZwSoft.ZwCAD.Runtime;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;

namespace ZWLibrary
{
    public partial class Plugin
    {
        [CommandMethod("ZLIBAI", CommandFlags.UsePickSet)]
        public void OpenAgent()
        {
            Run("ZLIBAI", (doc, unused) => {
                var selected = doc.Editor.SelectImplied();
                if (selected.Status != PromptStatus.OK) selected = doc.Editor.GetSelection();
                if (selected.Status != PromptStatus.OK) return;
                if (selected.Value.Count > 200) throw new InvalidOperationException("한 번에 200개 이하를 선택하세요.");
                var data = ReadSelected(doc.Database, selected.Value);
                var context = new StringBuilder("선택 " + data.Total + "개 / 관리 부품 " + data.Instances.Count + "개 / 제외 " + data.Excluded + "개\r\n");
                foreach (var i in data.Instances) context.AppendLine(AgentApi.Json.Serialize(new { handle = i.PhysicalKey, tag = i.Tag, panel = i.Panel, model = i.Part.Model, maker = i.Part.Brand, name = i.Part.Name, linked = !string.IsNullOrEmpty(i.LinkId) || !string.IsNullOrEmpty(i.LayoutSourceHandle) }));
                foreach (var i in data.Instances.Where(i => string.IsNullOrWhiteSpace(i.Part.Model) || string.IsNullOrWhiteSpace(i.Tag))) context.AppendLine("확인 필요: " + i.PhysicalKey + " 품번 또는 기기번호 없음");
                foreach (var group in data.Instances.Where(i => !string.IsNullOrWhiteSpace(i.Tag)).GroupBy(i => Part.Pack(i.Panel, i.Tag)).Where(g => g.Count() > 1)) context.AppendLine("번호 중복 후보 (심볼/배치 쌍일 수 있음): " + string.Join(",", group.Select(i => i.PhysicalKey)));
                foreach (var error in data.Errors) context.AppendLine(error);
                if (context.Length > 100000) throw new InvalidOperationException("선택 정보가 너무 많습니다. 범위를 줄이세요.");
                using (var dialog = new AgentDialog(context.ToString())) {
                    if (CadApp.ShowModalDialog(dialog) != DialogResult.OK || dialog.Plan == null) return;
                    var plan = dialog.Plan; plan.Validate();
                    if (!ReferenceEquals(doc, CadApp.DocumentManager.MdiActiveDocument)) throw new InvalidOperationException("도면이 변경되었습니다.");
                    var original = data.Instances.SingleOrDefault(i => i.PhysicalKey == plan.handle);
                    if (original == null || plan.old_tag != (original.Tag ?? "")) throw new InvalidOperationException("선택 범위 밖이거나 기존 번호가 일치하지 않습니다.");
                    using (var tr = doc.Database.TransactionManager.StartTransaction()) {
                        var id = doc.Database.GetObjectId(false, new Handle(Convert.ToInt64(plan.handle, 16)), 0);
                        var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                        var current = ReadCurrentInstance(br, tr);
                        if (current == null || current.Tag != original.Tag || current.Panel != original.Panel || current.Part.Signature != original.Part.Signature) throw new InvalidOperationException("부품 정보가 변경되었습니다. 다시 조회하세요.");
                        if (!string.IsNullOrEmpty(current.LinkId) || !string.IsNullOrEmpty(current.LayoutSourceHandle) || !string.IsNullOrEmpty(current.LinkOwnerHandle)) throw new InvalidOperationException("연결 부품의 동시 번호 변경은 이번 개발본에서 지원하지 않습니다.");
                        var blocks = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                        foreach (ObjectId blockId in blocks) {
                            var space = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead); if (!space.IsLayout) continue;
                            foreach (ObjectId otherId in space) {
                                if (otherId == id) continue;
                                var other = tr.GetObject(otherId, OpenMode.ForRead) as BlockReference; if (other == null) continue;
                                var instance = ReadCurrentInstance(other, tr);
                                if (instance != null && string.Equals(instance.Panel ?? "", current.Panel ?? "", StringComparison.OrdinalIgnoreCase) && string.Equals(instance.Tag, plan.new_tag, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("같은 판넬에 해당 기기번호가 이미 존재합니다.");
                            }
                        }
                        current.Tag = plan.new_tag; current.DeviceNumberInputEnabled = true; br.UpgradeOpen();
                        if (ReadDeviceNumbers(br, tr).Length != 1) throw new InvalidOperationException("최상위 UNIT_NO 속성이 하나인 부품만 변경할 수 있습니다.");
                        int changed = ApplyAttributes(br, current, tr);
                        if (current.GeometryKind != null) ApplyLayoutAttributes(br, current, tr, new HashSet<ObjectId>(), 0);
                        if (changed == 0) throw new InvalidOperationException("직접 수정 가능한 기기번호 속성이 없습니다. 기존 편집 기능을 사용하세요.");
                        WriteInstance(br, current, tr);
                        var verified = ReadCurrentInstance(br, tr);
                        if (verified == null || verified.Tag != plan.new_tag) throw new InvalidOperationException("변경 확인 실패. 작업을 취소합니다.");
                        tr.Commit();
                    }
                    doc.Editor.Regen(); doc.Editor.WriteMessage("\nAI 번호 변경 완료: " + plan.handle + " " + plan.old_tag + " → " + plan.new_tag + " (U 명령으로 취소 가능, 저장은 별도)\n");
                }
            });
        }
    }
}
