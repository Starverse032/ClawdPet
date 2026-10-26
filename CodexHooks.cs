// Codex lifecycle hooks -> metadata only. Never return permission or turn decisions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace ClawdPet
{
    static class CodexHooks
    {
        public const string Source = "codex-hook";
        public static readonly string[] Events = { "SessionStart", "UserPromptSubmit", "PreToolUse",
            "PermissionRequest", "PostToolUse", "PreCompact", "PostCompact", "Stop", "Interrupt", "SessionEnd" };

        public static string ThinkText(DateTime utc)
        {
            int index = (int)((utc.Ticks / (5 * TimeSpan.TicksPerSecond)) % Lines.Verbs.Length);
            return "ChatGPT · " + Lines.Verbs[index] + "…";
        }

        public static ClaudeEvent Parse(string input)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = HookInput.MaxLength, RecursionLimit = 100 };
            var data = json.Deserialize<Dictionary<string, object>>(input);
            if (data == null || HookInput.Field(data, "agent_id", 200) != "") return null;
            string kind = HookInput.Field(data, "hook_event_name", 80), session = HookInput.Field(data, "session_id", 200);
            if (session.Length == 0 || Array.IndexOf(Events, kind) < 0) return null;
            // Discard prompts, transcript paths, model replies, tool arguments and results.
            return new ClaudeEvent { Source = Source, Session = Source + ":" + session,
                Event = kind, Turn = HookInput.Field(data, "turn_id", 200), Tool = HookInput.Field(data, "tool_name", 96),
                Notification = HookInput.Field(data, "source", 32), Ticks = DateTime.UtcNow.Ticks };
        }

        public static int Run() { return HookInput.Run(Parse); }

        public static ClaudeActivity Activity(ClaudeEvent ev, ClaudeActivity previous)
        {
            if (!ev.Session.StartsWith(Source + ":", StringComparison.Ordinal) || Array.IndexOf(Events, ev.Event) < 0) return null;
            if (previous != null)
            {
                if (previous.Ticks > ev.Ticks) return null;
                if (ev.Event == "SessionStart" && previous.Active) return null; // resume/compact during a turn
                if (ev.Event != "UserPromptSubmit" && ev.Event != "SessionStart" && ev.Event != "SessionEnd")
                {
                    if (!string.IsNullOrEmpty(previous.Turn) && !string.IsNullOrEmpty(ev.Turn) && previous.Turn != ev.Turn) return null;
                    if (previous.Kind == "done" || previous.Kind == "interrupted" || previous.Kind == "ended") return null;
                }
            }
            var a = new ClaudeActivity { Source = Source, Event = ev.Event, Turn = ev.Turn,
                Ticks = ev.Ticks };
            switch (ev.Event)
            {
                case "SessionStart": a.Kind = "ready"; a.Text = "ChatGPT · 已连接～"; break;
                case "UserPromptSubmit": a.Kind = "working"; a.Text = "ChatGPT · 思考中…"; break;
                case "PreToolUse":
                    string tool = ev.Tool ?? "";
                    bool question = tool == "request_user_input" || tool == "request_user_input_async" ||
                        tool.EndsWith("__request_user_input", StringComparison.Ordinal) || tool == "AskUserQuestion";
                    a.Kind = question ? "waiting" : "working";
                    a.Text = question ? "ChatGPT · 等你回答～" : tool == "Bash" || tool == "exec_command" ?
                        "ChatGPT · 正在运行命令…" : tool == "apply_patch" ? "ChatGPT · 正在修改文件…" : "ChatGPT · 正在使用工具…";
                    break;
                case "PermissionRequest": a.Kind = "waiting"; a.Text = "ChatGPT · 等你确认～"; break;
                case "PostToolUse": a.Kind = "working"; a.Text = "ChatGPT · 继续处理中…"; break;
                case "PreCompact": a.Kind = "working"; a.Text = "ChatGPT · 正在整理上下文…"; break;
                case "PostCompact": a.Kind = "working"; a.Text = "ChatGPT · 继续思考中…"; break;
                case "Stop": a.Kind = "done"; a.Text = "ChatGPT · 回复完成啦！"; break;
                case "Interrupt": a.Kind = "interrupted"; a.Text = "ChatGPT · 本轮已停止"; break;
                case "SessionEnd": a.Kind = "ended"; a.Text = "ChatGPT · 会话已结束～"; break;
                default: return null;
            }
            if (a.Thinking) a.Text = ThinkText(new DateTime(ev.Ticks, DateTimeKind.Utc));
            return a;
        }
    }

    // Some desktop reply paths omit Stop. Reconcile only hook-started turns with
    // their local rollout completion record; never infer completion from silence.
    sealed class CodexCompletionMonitor
    {
        readonly string root;
        readonly Dictionary<string, Watch> watches = new Dictionary<string, Watch>();
        readonly Queue<ClaudeEvent> completed = new Queue<ClaudeEvent>();
        readonly object gate = new object();
        bool busy;
        DateTime nextPoll;
        sealed class Watch
        {
            public string Path, Turn;
            public long Length = -1, Written;
            public DateTime SearchAfter;
        }
        // Only the metadata prefix is retained. Text following turn_id is ignored,
        // including last_agent_message, even when it spans many megabytes.
        static readonly Regex Header = new Regex(@"^\s*\{\s*""timestamp""\s*:\s*""[^""]+""\s*,\s*(?:""ordinal""\s*:\s*\d+\s*,\s*)?""type""\s*:\s*""event_msg""\s*,\s*""payload""\s*:\s*\{\s*""type""\s*:\s*""task_complete""\s*,\s*(?:""root_turn_id""\s*:\s*(?:null|""[0-9a-fA-F-]+"")\s*,\s*)?""turn_id""\s*:\s*""(?<turn>[0-9a-fA-F-]+)""\s*[,}]", RegexOptions.CultureInvariant);

        public CodexCompletionMonitor() : this(Path.Combine(
            Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), "sessions")) { }
        public CodexCompletionMonitor(string sessionsRoot) { root = sessionsRoot; }

        public List<ClaudeEvent> Poll(Dictionary<string, ClaudeActivity> sessions, DateTime utc)
        {
            var result = new List<ClaudeEvent>();
            lock (gate)
            {
                while (completed.Count > 0) result.Add(completed.Dequeue());
                if (busy || utc < nextPoll) return result;
                nextPoll = utc.AddSeconds(1);
                busy = true;
            }
            var active = new Dictionary<string, string>();
            foreach (var item in sessions)
                if (item.Value.Source == CodexHooks.Source && item.Value.Active && !string.IsNullOrEmpty(item.Value.Turn))
                    active[item.Key] = item.Value.Turn;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var remove = new List<string>();
                    foreach (string key in watches.Keys) if (!active.ContainsKey(key)) remove.Add(key);
                    foreach (string key in remove) watches.Remove(key);
                    foreach (var item in active)
                    {
                        ClaudeEvent ev = Check(item.Key, item.Value, utc);
                        if (ev != null) lock (gate) completed.Enqueue(ev);
                    }
                }
                catch (Exception) { /* Optional reconciliation must not stop the pet. */ }
                finally { lock (gate) busy = false; }
            });
            return result;
        }

        // Called on the worker, or directly by the isolated regression tests.
        internal ClaudeEvent Check(string session, string turn, DateTime utc)
        {
            Guid id, turnId;
            string prefix = CodexHooks.Source + ":";
            if (!session.StartsWith(prefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(session.Substring(prefix.Length), "D", out id) ||
                !Guid.TryParseExact(turn, "D", out turnId)) return null;
            try
            {
                Watch watch;
                if (!watches.TryGetValue(session, out watch) || watch.Turn != turn)
                    watches[session] = watch = new Watch { Turn = turn };
                if (watch.Path == null || !File.Exists(watch.Path))
                {
                    if (utc < watch.SearchAfter) return null;
                    watch.SearchAfter = utc.AddSeconds(5);
                    if (!Directory.Exists(root)) return null;
                    string[] paths = Directory.GetFiles(root, "rollout-*-" + id.ToString("D") + ".jsonl", SearchOption.AllDirectories);
                    if (paths.Length != 1) return null;
                    watch.Path = paths[0];
                    watch.Length = -1;
                }
                var info = new FileInfo(watch.Path);
                if (watch.Length == info.Length && watch.Written == info.LastWriteTimeUtc.Ticks) return null;
                using (var stream = new FileStream(watch.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long length = stream.Length;
                    // Include the start of the first tail record, even for a very
                    // large final answer. Retain at most 1 KiB from each line.
                    long start = Math.Max(0, length - 65536);
                    byte[] block = new byte[4096];
                    while (start > 0)
                    {
                        long from = Math.Max(0, start - block.Length);
                        stream.Position = from;
                        int n = stream.Read(block, 0, (int)(start - from));
                        if (n == 0) return null; // concurrent truncation; retry next poll
                        int newline = Array.LastIndexOf(block, (byte)10, n - 1, n);
                        if (newline >= 0) { start = from + newline + 1; break; }
                        start = from;
                    }
                    stream.Position = start;
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096))
                    {
                        var header = new StringBuilder(1024);
                        char[] chars = new char[4096];
                        int n;
                        while ((n = reader.Read(chars, 0, chars.Length)) > 0)
                            for (int i = 0; i < n; i++)
                            {
                                if (chars[i] == '\n')
                                {
                                    Match match = Header.Match(header.ToString());
                                    if (match.Success && match.Groups["turn"].Value == turn)
                                        return new ClaudeEvent { Source = CodexHooks.Source, Session = session,
                                            Turn = turn, Event = "Stop", Ticks = DateTime.UtcNow.Ticks };
                                    header.Length = 0;
                                }
                                else if (header.Length < 1024) header.Append(chars[i]);
                            }
                    }
                    watch.Length = length;
                    watch.Written = info.LastWriteTimeUtc.Ticks;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }
    }
}
