// Claude Code hooks -> local, content-free events -> desktop pet.
// C# 5 / .NET Framework 4.x. Hook failures must never affect Claude's work.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ClawdPet
{
    class ClaudeEvent
    {
        public string Session, Event, Tool, Notification;
        public string Source, Action, Text, Request, Turn;
        public int Duration;
        public long Ticks;
    }

    class ClaudeActivity
    {
        public string Kind, Text, Event;
        public string Source, Request, Turn;
        public long Expires;
        public long Ticks;
        public bool Active { get { return Kind == "working" || Kind == "waiting"; } }
        public bool Thinking { get { return Kind == "working"; } }
    }

    class ClaudeLink
    {
        public static readonly string[] Events = { "SessionStart", "UserPromptSubmit", "PreToolUse",
            "PostToolUse", "PostToolUseFailure", "PermissionRequest", "Notification", "Stop", "StopFailure", "SessionEnd" };
        readonly string dir;
        readonly Dictionary<string, ClaudeActivity> sessions = new Dictionary<string, ClaudeActivity>();
        readonly CodexCompletionMonitor codexCompletion = new CodexCompletionMonitor();
        long lastEventTicks;
        string lastEvent, published;
        public ClaudeActivity Current;
        public int SessionCount { get { return sessions.Count; } }
        public string StatusText
        {
            get { return Current == null ? (sessions.Count == 0 ? "Clawd · 等待 AI 联动" : "Clawd · 待命") : Current.Text; }
        }

        public static string DataDir
        {
            get
            {
                string test = Environment.GetEnvironmentVariable("CLAWD_BRIDGE_DIR");
                return string.IsNullOrEmpty(test) ? Path.Combine(PetSettings.Dir, "claude-code") : test;
            }
        }

        public ClaudeLink() : this(DataDir) { }
        public ClaudeLink(string path) { dir = path; }

        static JavaScriptSerializer Json()
        {
            return new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024, RecursionLimit = 100 };
        }

        public static bool? PetRunning()
        {
            try { using (Mutex m = Mutex.OpenExisting("Local\\ClawdPet.SingleInstance")) return true; }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public static void EnsurePetRunning()
        {
            if (Environment.GetEnvironmentVariable("CLAWD_NO_AUTO_START") == "1") return;
            bool? running = PetRunning();
            if (running == true) return;
            if (running == null) throw new UnauthorizedAccessException("Cannot inspect the pet instance.");
            ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath, "--ensure-running");
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process p = Process.Start(start)) { }
        }

        static string Field(Dictionary<string, object> input, string key, int max)
        {
            object value;
            string s = input.TryGetValue(key, out value) ? value as string : null;
            if (s == null) return "";
            if (s.Length > max) s = s.Substring(0, max);
            return s;
        }

        // Keep only event metadata. Prompts, answers, paths and tool arguments are discarded.
        public static ClaudeEvent ParseHook(string input)
        {
            Dictionary<string, object> data = Json().Deserialize<Dictionary<string, object>>(input);
            if (data == null || Field(data, "agent_id", 200) != "") return null;
            string ev = Field(data, "hook_event_name", 80), session = Field(data, "session_id", 200);
            if (Array.IndexOf(Events, ev) < 0 || session.Length == 0) return null;
            return new ClaudeEvent { Session = session, Event = ev, Tool = Field(data, "tool_name", 96),
                Notification = Field(data, "notification_type", 96), Ticks = DateTime.UtcNow.Ticks };
        }

        public static void WriteEvent(string path, ClaudeEvent ev)
        {
            Directory.CreateDirectory(path);
            // A separate file per event prevents concurrent sessions from overwriting each other.
            string stem = Path.Combine(path, "event-" + ev.Ticks.ToString("D19") + "-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(stem + ".tmp", Json().Serialize(ev), new UTF8Encoding(false));
            File.Move(stem + ".tmp", stem + ".json");
        }

        public static int RunHook()
        {
            try
            {
                StringBuilder input = new StringBuilder();
                char[] buf = new char[4096];
                int n;
                while ((n = Console.In.Read(buf, 0, buf.Length)) > 0)
                {
                    if (input.Length + n > 2 * 1024 * 1024) return 0;
                    input.Append(buf, 0, n);
                }
                ClaudeEvent ev = ParseHook(input.ToString());
                if (ev == null) return 0;
                WriteEvent(DataDir, ev);
                // Only the start of a session/turn summons the app; never spawn extra pets.
                if (ev.Event == "SessionStart" || ev.Event == "UserPromptSubmit") EnsurePetRunning();
            }
            catch (Exception) { /* No output or nonzero exit: a pet cannot block a tool or approve it. */ }
            return 0;
        }

        static string ToolText(string name)
        {
            switch (name)
            {
                case "Read": return "正在读文件…";
                case "Edit": case "Write": case "NotebookEdit": return "正在写代码…";
                case "Bash": case "PowerShell": return "正在运行命令…";
                case "Glob": case "Grep": return "正在查找代码…";
                case "WebFetch": case "WebSearch": return "正在查资料…";
                case "Agent": case "Task": return "正在安排子任务…";
                default: return "正在调用工具…";
            }
        }

        public string LastDesktopRequest;
        readonly Queue<string> desktopRequests = new Queue<string>();

        public void Accept(ClaudeEvent ev)
        {
            if (ev == null || string.IsNullOrEmpty(ev.Session)) return;
            if (ev.Source == CodexHooks.Source)
            {
                ClaudeActivity previous;
                sessions.TryGetValue(ev.Session, out previous);
                ClaudeActivity activity = CodexHooks.Activity(ev, previous);
                if (activity == null || (sessions.Count >= 64 && !sessions.ContainsKey(ev.Session))) return;
                sessions[ev.Session] = activity;
                lastEvent = "ChatGPT/" + ev.Event;
                lastEventTicks = Math.Max(lastEventTicks, ev.Ticks);
                return;
            }
            if (ev.Event == "DesktopAction" && DesktopMcp.IsChatSource(ev.Source))
            {
                if (Array.IndexOf(DesktopMcp.States, ev.Action) < 0 || ev.Duration < 1 || ev.Duration > 120) return;
                if (!ev.Session.StartsWith(ev.Source + ":", StringComparison.Ordinal)) return;
                ClaudeActivity previous;
                if (sessions.TryGetValue(ev.Session, out previous) && previous.Ticks > ev.Ticks) return;
                if (sessions.Count >= 64 && !sessions.ContainsKey(ev.Session)) return;
                LastDesktopRequest = ev.Request;
                desktopRequests.Enqueue(ev.Request);
                while (desktopRequests.Count > 32) desktopRequests.Dequeue();
                if (ev.Action == "idle") { sessions.Remove(ev.Session); return; }
                sessions[ev.Session] = new ClaudeActivity { Source = ev.Source, Request = ev.Request,
                    Kind = ev.Action == "thinking" ? "working" : ev.Action, Event = ev.Event, Ticks = ev.Ticks,
                    Expires = ev.Ticks + (long)ev.Duration * TimeSpan.TicksPerSecond,
                    Text = string.IsNullOrWhiteSpace(ev.Text) ? DesktopMcp.StateText(ev.Action, ev.Source) : DesktopMcp.CleanText(ev.Text) };
                return;
            }
            if (Array.IndexOf(Events, ev.Event) < 0) return;
            ClaudeActivity old;
            if (sessions.TryGetValue(ev.Session, out old) && old.Ticks > ev.Ticks) return;
            ClaudeActivity a = new ClaudeActivity { Event = ev.Event, Ticks = ev.Ticks };
            switch (ev.Event)
            {
                case "SessionStart": a.Kind = "ready"; a.Text = "Claude Code 已连接～"; break;
                case "UserPromptSubmit": a.Kind = "working"; a.Text = "Claude Code · 思考中…"; break;
                case "PreToolUse":
                    a.Kind = ev.Tool == "AskUserQuestion" ? "waiting" : "working";
                    a.Text = ev.Tool == "AskUserQuestion" ? "Claude 有问题想问你～" : "Claude · " + ToolText(ev.Tool); break;
                case "PostToolUse": a.Kind = "working"; a.Text = "Claude Code · 继续思考…"; break;
                case "PermissionRequest": a.Kind = "waiting"; a.Text = "Claude Code 需要你确认～"; break;
                case "Notification":
                    if (ev.Notification == "permission_prompt" || ev.Notification == "elicitation_dialog" ||
                        ev.Notification == "elicitation_url_dialog" || ev.Notification == "agent_needs_input")
                    { a.Kind = "waiting"; a.Text = "Claude Code 正在等你～"; }
                    else if (ev.Notification == "idle_prompt")
                    {
                        if (old != null && old.Active) return;
                        a.Kind = "ready"; a.Text = "Claude Code · 等你提问～";
                    }
                    else return;
                    break;
                case "PostToolUseFailure": a.Kind = "working"; a.Text = "工具未成功，Claude 正在处理…"; break;
                case "Stop": a.Kind = "done"; a.Text = "Claude 回复完成啦！"; break;
                case "StopFailure": a.Kind = "error"; a.Text = "Claude 遇到错误，去看一下吧～"; break;
                case "SessionEnd": a.Kind = "ended"; a.Text = "Claude Code 会话已结束～"; break;
                default: return;
            }
            sessions[ev.Session] = a;
            lastEvent = ev.Event;
            lastEventTicks = Math.Max(lastEventTicks, ev.Ticks);
            if (sessions.Count > 64)
            {
                string oldest = null;
                foreach (KeyValuePair<string, ClaudeActivity> item in sessions)
                    if (oldest == null || item.Value.Ticks < sessions[oldest].Ticks) oldest = item.Key;
                sessions.Remove(oldest);
            }
        }

        public void Select(DateTime utc)
        {
            Current = null;
            int priority = -1;
            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, ClaudeActivity> item in sessions)
            {
                ClaudeActivity a = item.Value;
                if (a.Expires > 0 && utc.Ticks >= a.Expires) { remove.Add(item.Key); continue; }
                double age = (utc.Ticks - a.Ticks) / (double)TimeSpan.TicksPerSecond;
                if (age > 1800) { remove.Add(item.Key); continue; } // crashed/abandoned sessions must not stay busy forever
                if (a.Kind == "ended" && age > 8) { remove.Add(item.Key); continue; }
                if (!DesktopMcp.IsChatSource(a.Source) && !a.Active && age > 8) continue;
                int p = DesktopMcp.IsChatSource(a.Source) ? 1 : a.Kind == "waiting" ? 4 : a.Kind == "working" ? 3 : a.Kind == "error" ? 2 : 1;
                if (p > priority || (p == priority && (Current == null || a.Ticks > Current.Ticks)))
                { Current = a; priority = p; }
            }
            foreach (string key in remove) sessions.Remove(key);
            if (Current != null && Current.Source == CodexHooks.Source && Current.Thinking)
                Current.Text = CodexHooks.ThinkText(utc);
        }

        public void Poll()
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    string[] files = Directory.GetFiles(dir, "event-*.json");
                    Array.Sort(files, StringComparer.Ordinal);
                    int count = 0;
                    foreach (string path in files)
                    {
                        if (++count > 256) break;
                        try
                        {
                            if (new FileInfo(path).Length <= 8192)
                            {
                                ClaudeEvent ev = Json().Deserialize<ClaudeEvent>(File.ReadAllText(path, Encoding.UTF8));
                                if (ev != null && ev.Ticks <= DateTime.UtcNow.AddMinutes(1).Ticks &&
                                    ev.Ticks >= DateTime.UtcNow.AddMinutes(-30).Ticks) Accept(ev);
                            }
                        }
                        catch (Exception) { }
                        try { File.Delete(path); } catch (IOException) { }
                    }
                }
                foreach (ClaudeEvent ev in codexCompletion.Poll(sessions, DateTime.UtcNow)) Accept(ev);
                Select(DateTime.UtcNow);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        // A small diagnostic snapshot; never includes conversation content or session identifiers.
        public void Publish(bool enabled)
        {
            try
            {
                string snapshot = Json().Serialize(new { enabled = enabled, processId = Process.GetCurrentProcess().Id,
                    state = !enabled ? "disabled" : Current == null ? "idle" : Current.Kind,
                    text = !enabled ? "AI 联动已暂停" : StatusText, sessions = sessions.Count,
                    source = Current == null ? null : Current.Source == CodexHooks.Source ? "chatgpt" : DesktopMcp.IsChatSource(Current.Source) ? Current.Source : "code",
                    currentRequest = Current == null ? null : Current.Request, lastDesktopRequest = LastDesktopRequest,
                    desktopRequests = desktopRequests.ToArray(),
                    lastEvent = lastEvent, lastEventUtc = lastEventTicks == 0 ? null : new DateTime(lastEventTicks, DateTimeKind.Utc).ToString("o") });
                if (published == snapshot) return;
                Directory.CreateDirectory(dir);
                string dest = Path.Combine(dir, "status.json"), temp = dest + ".tmp";
                File.WriteAllText(temp, snapshot, new UTF8Encoding(false));
                if (File.Exists(dest)) File.Replace(temp, dest, null); else File.Move(temp, dest);
                published = snapshot;
            }
            catch (Exception ex) { Log.Error(ex); }
        }
    }
}
