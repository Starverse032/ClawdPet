// Minimal local stdio MCP server. No network, shell execution or conversation access.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ClawdPet
{
    class DesktopMcp
    {
        public static readonly string[] States = { "wave", "dance", "happy", "thinking", "waiting", "sleep", "error", "idle" };
        static readonly string[] Versions = { "2024-11-05", "2025-03-26", "2025-06-18", "2025-11-25" };
        readonly string source, session;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 24 };
        bool negotiated, ready;
        string protocol;

        public DesktopMcp() : this("desktop") { }
        public DesktopMcp(string clientSource)
        {
            if (!IsChatSource(clientSource)) throw new ArgumentException("Unknown MCP client source.");
            source = clientSource;
            session = source + ":" + Guid.NewGuid().ToString("N");
        }

        public static bool IsChatSource(string value) { return value == "desktop" || value == "codex"; }

        public static string CleanText(string text)
        {
            if (text == null) return "";
            StringBuilder clean = new StringBuilder();
            foreach (char c in text) if (!char.IsControl(c) && clean.Length < 80) clean.Append(c);
            return clean.ToString().Trim();
        }

        public static string StateText(string state) { return StateText(state, "desktop"); }
        public static string StateText(string state, string source)
        {
            string name = source == "codex" ? "ChatGPT" : "Claude Desktop";
            switch (state)
            {
                case "wave": return name + " 向你打招呼～";
                case "dance": return name + " · 跳个舞～";
                case "happy": return name + " · 开心！";
                case "thinking": return source == "codex" ? CodexHooks.ThinkText(DateTime.UtcNow) : name + " · 思考中…";
                case "waiting": return name + " · 等你回应～";
                case "sleep": return name + " · 休息一下～";
                case "error": return name + " · 看看聊天里的提示～";
                default: return name + " · 待命";
            }
        }

        static Dictionary<string, object> Map(object obj) { return obj as Dictionary<string, object>; }
        static object Get(Dictionary<string, object> data, string key)
        { object value; return data != null && data.TryGetValue(key, out value) ? value : null; }
        static bool HasAck(Dictionary<string, object> status, string requestId)
        {
            System.Collections.IEnumerable ids = Get(status, "desktopRequests") as System.Collections.IEnumerable;
            if (ids != null) foreach (object id in ids) if (object.Equals(id, requestId)) return true;
            return object.Equals(Get(status, "lastDesktopRequest"), requestId);
        }
        static object Error(object id, int code, string message)
        { return new { jsonrpc = "2.0", id = id, error = new { code = code, message = message } }; }
        static object Result(object id, object result)
        { return new { jsonrpc = "2.0", id = id, result = result }; }
        static object ToolResult(object data, bool error)
        {
            return new { content = new[] { new { type = "text", text = new JavaScriptSerializer().Serialize(data) } }, isError = error };
        }

        object Tools()
        {
            return new { tools = new object[] {
                new { name = "clawdpet_act", description = "Control the user's local Clawd desktop pet: wave, dance, happy, thinking, waiting, sleep, error, or idle (release this connection's state). Use when asked to interact with the pet or show task status. This is an explicit display command, not automatic monitoring of the conversation. Code tasks that need attention take priority. Actions expire automatically.",
                    inputSchema = new { type = "object", properties = new {
                        state = new { type = "string", @enum = States },
                        text = new { type = "string", maxLength = 80, description = "Optional short speech bubble. Visible on the desktop and briefly stored locally. Do not include secrets or copy conversation content." },
                        duration_seconds = new { type = "integer", minimum = 1, maximum = 120, description = "Display duration, default 8 seconds; thinking/waiting default 30. Send idle or another state to finish sooner." }
                    }, required = new[] { "state" }, additionalProperties = false },
                    annotations = new { title = "Clawd 桌宠动作", readOnlyHint = false, destructiveHint = false, idempotentHint = false, openWorldHint = false } },
                new { name = "clawdpet_status", description = "Read whether Clawd is running, whether AI integration is enabled, and the current local pet status. No conversation or file access.",
                    inputSchema = new { type = "object", properties = new { }, additionalProperties = false },
                    annotations = new { title = "Clawd 桌宠状态", readOnlyHint = true, destructiveHint = false, idempotentHint = true, openWorldHint = false } }
            } };
        }

        Dictionary<string, object> ReadStatus()
        {
            try { return json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(ClaudeLink.DataDir, "status.json"), Encoding.UTF8)); }
            catch (Exception) { return null; }
        }

        void Connection(bool connected)
        {
            try
            {
                string dir = ClaudeLink.DataDir;
                Directory.CreateDirectory(dir);
                // Fixed filename per server process; metadata only, not raw MCP traffic.
                File.WriteAllText(Path.Combine(dir, "desktop-mcp-" + Process.GetCurrentProcess().Id + ".json"),
                    json.Serialize(new { connected = connected, source = source, protocol = protocol, processId = Process.GetCurrentProcess().Id,
                        updatedUtc = DateTime.UtcNow.ToString("o") }), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        void Release()
        {
            try { ClaudeLink.WriteEvent(ClaudeLink.DataDir, new ClaudeEvent { Session = session, Event = "DesktopAction",
                Source = source, Action = "idle", Request = Guid.NewGuid().ToString("N"), Duration = 1, Ticks = DateTime.UtcNow.Ticks }); }
            catch (Exception) { }
        }

        public object Handle(Dictionary<string, object> request)
        {
            object id = Get(request, "id");
            string method = Get(request, "method") as string;
            if (request == null || !object.Equals(Get(request, "jsonrpc"), "2.0") || method == null)
                return Error(id, -32600, "Invalid JSON-RPC request");
            bool hasId = request.ContainsKey("id");
            Dictionary<string, object> p = Map(Get(request, "params"));
            if (!hasId)
            {
                if (method == "notifications/initialized" && negotiated) { ready = true; Connection(true); }
                return null; // Notifications must never receive a response.
            }
            if (method == "ping") return Result(id, new { });
            if (method == "initialize")
            {
                if (negotiated) return Error(id, -32600, "Already initialized");
                string requested = Get(p, "protocolVersion") as string;
                if (requested == null) return Error(id, -32602, "protocolVersion is required");
                protocol = Array.IndexOf(Versions, requested) >= 0 ? requested : "2025-11-25";
                negotiated = true;
                return Result(id, new { protocolVersion = protocol, capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "clawdpet", version = Program.Version },
                    instructions = "ClawdPet controls only a local desktop animation. Invoke its tools when the user requests pet interaction or status signals. It cannot observe chat starts, generation, completion or permissions automatically. If the user asks for task progress, set thinking before the task and happy/idle when done. Never send private conversation content to the pet." });
            }
            if (!ready) return Error(id, -32002, "Initialize the MCP connection first");
            if (method == "tools/list") return Result(id, Tools());
            if (method != "tools/call") return Error(id, -32601, "Method not found");
            string name = Get(p, "name") as string;
            if (Get(p, "arguments") != null && Map(Get(p, "arguments")) == null) return Error(id, -32602, "arguments must be an object");
            Dictionary<string, object> arguments = Map(Get(p, "arguments")) ?? new Dictionary<string, object>();
            if (name == "clawdpet_status")
            {
                if (arguments.Count != 0) return Result(id, ToolResult(new { message = "This tool takes no arguments." }, true));
                return Result(id, ToolResult(new { running = ClaudeLink.PetRunning(), status = ReadStatus() }, false));
            }
            if (name != "clawdpet_act") return Error(id, -32602, "Unknown tool");
            foreach (string key in arguments.Keys)
                if (key != "state" && key != "text" && key != "duration_seconds")
                    return Result(id, ToolResult(new { message = "Unknown argument: " + key }, true));
            string state = Get(arguments, "state") as string;
            if (Array.IndexOf(States, state) < 0) return Result(id, ToolResult(new { message = "Invalid pet state." }, true));
            object rawText = Get(arguments, "text");
            if (rawText != null && (!(rawText is string) || ((string)rawText).Length > 80))
                return Result(id, ToolResult(new { message = "text must be a string of at most 80 characters." }, true));
            int duration = state == "thinking" || state == "waiting" ? 30 : 8;
            object rawDuration = Get(arguments, "duration_seconds");
            if (rawDuration != null)
            {
                if (!(rawDuration is int) || (int)rawDuration < 1 || (int)rawDuration > 120)
                    return Result(id, ToolResult(new { message = "duration_seconds must be an integer from 1 to 120." }, true));
                duration = (int)rawDuration;
            }
            string requestId = Guid.NewGuid().ToString("N");
            try
            {
                ClaudeLink.EnsurePetRunning();
                ClaudeLink.WriteEvent(ClaudeLink.DataDir, new ClaudeEvent { Session = session, Event = "DesktopAction",
                    Source = source, Action = state, Text = CleanText(rawText as string), Request = requestId,
                    Duration = duration, Ticks = DateTime.UtcNow.Ticks });
                // Acknowledge consumption by the UI, not just successful file creation.
                Stopwatch timer = Stopwatch.StartNew();
                do
                {
                    Dictionary<string, object> status = ReadStatus();
                    if (HasAck(status, requestId))
                        return Result(id, ToolResult(new { accepted = true, state = state,
                            selected = object.Equals(Get(status, "currentRequest"), requestId) && object.Equals(Get(status, "enabled"), true),
                            message = state == "idle" ? "已解除此 MCP 连接的桌宠动作。" : "桌宠已收到动作；显示受 Code 任务优先级、联动开关、安静模式和全屏隐藏影响。" }, false));
                    if (Environment.GetEnvironmentVariable("CLAWD_NO_AUTO_START") == "1") break;
                    Thread.Sleep(50);
                } while (timer.ElapsedMilliseconds < 3000);
                return Result(id, ToolResult(new { accepted = false, queued = true, message = "动作已排队，但尚未收到桌宠确认。请确认桌宠运行的是支持 AI 联动的新版。" }, true));
            }
            catch (Exception) { return Result(id, ToolResult(new { message = "无法连接本地桌宠。请检查程序和本地数据目录是否可用。" }, true)); }
        }

        public static int Run() { return Run("desktop"); }
        public static int Run(string source)
        {
            DesktopMcp server = new DesktopMcp(source);
            try
            {
                using (StreamReader input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), true))
                using (StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true })
                {
                    StringBuilder line = new StringBuilder();
                    bool overflow = false;
                    int ch;
                    while ((ch = input.Read()) >= 0)
                    {
                        if (ch != '\n') { if (line.Length < 65536) line.Append((char)ch); else overflow = true; continue; }
                        object response = null;
                        Dictionary<string, object> request = null;
                        if (overflow) response = Error(null, -32700, "Message too large");
                        else
                        {
                            try { request = server.json.Deserialize<Dictionary<string, object>>(line.ToString()); }
                            catch (Exception) { response = Error(null, -32700, "Invalid JSON"); }
                            if (response == null)
                            {
                                try { response = server.Handle(request); }
                                catch (Exception) { response = Error(Get(request, "id"), -32603, "Local request failed"); }
                            }
                        }
                        line.Length = 0; overflow = false;
                        if (response != null) output.WriteLine(server.json.Serialize(response));
                    }
                }
            }
            catch (IOException) { }
            finally { if (server.ready) { server.Release(); server.Connection(false); } }
            return 0;
        }
    }
}
