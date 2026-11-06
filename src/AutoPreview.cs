using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;

namespace ZWLibrary
{
    internal static class PreviewCache
    {
        private sealed class Entry { internal string Stamp; internal PreviewData Data; internal long Used; }
        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static long sequence;
        internal static void Clear() { entries.Clear(); }
        internal static PreviewData Read(string path)
        {
            var file = new FileInfo(path); if (!file.Exists) throw new FileNotFoundException("등록 DWG 파일이 없습니다.", path);
            string key = file.FullName, stamp = file.Length + "|" + file.LastWriteTimeUtc.Ticks;
            Entry value;
            if (entries.TryGetValue(key, out value) && value.Stamp == stamp) { value.Used = ++sequence; return value.Data; }
            var data = PreviewData.Read(key);
            entries[key] = new Entry { Stamp = stamp, Data = data, Used = ++sequence };
            while (entries.Count > 16) entries.Remove(entries.OrderBy(x => x.Value.Used).First().Key);
            return data;
        }
    }
    internal static class PreviewScheduler
    {
        private static readonly Dictionary<object, Action> pending = new Dictionary<object, Action>();
        private static readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
        static PreviewScheduler() { CadApp.Idle += Idle; }
        internal static void Queue(object owner, Action work) { pending[owner] = work; }
        internal static void Cancel(object owner) { pending.Remove(owner); }
        private static void Idle(object sender, EventArgs args)
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread || pending.Count == 0 || Plugin.IsRunning) return;
            var doc = CadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || !string.IsNullOrEmpty(doc.CommandInProgress)) return;
            var item = pending.First(); pending.Remove(item.Key); item.Value();
        }
    }
    internal sealed class AutoPreviewSession : IDisposable
    {
        private readonly PreviewCanvas canvas;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 180 };
        private string path, missing;
        private Func<bool> stillSelected;
        private long generation;
        private bool disposed;
        internal AutoPreviewSession(PreviewCanvas target)
        {
            canvas = target;
            timer.Tick += delegate
            {
                timer.Stop(); long ticket = generation; string requested = path; var valid = stillSelected;
                PreviewScheduler.Queue(this, () =>
                {
                    if (disposed || ticket != generation || !valid()) return;
                    PreviewData data = null; string message;
                    try { data = PreviewCache.Read(requested); message = data.HasGeometry ? "" : "표시 가능한 도형이 없습니다. (생략 " + data.Skipped + "개)"; }
                    catch (System.Exception ex) { message = "미리보기 실패: " + ex.Message; }
                    if (disposed || ticket != generation || !valid()) return;
                    canvas.Data = data; canvas.Message = message; canvas.Invalidate();
                });
            };
        }
        internal void Request(string file, string emptyMessage, Func<bool> current)
        {
            generation++; timer.Stop(); PreviewScheduler.Cancel(this);
            path = file; missing = emptyMessage; stillSelected = current;
            canvas.Data = null; canvas.Message = file == null ? missing : "미리보기 불러오는 중…"; canvas.Invalidate();
            if (file != null) timer.Start();
        }
        public void Dispose() { disposed = true; generation++; timer.Dispose(); PreviewScheduler.Cancel(this); }
    }
}
