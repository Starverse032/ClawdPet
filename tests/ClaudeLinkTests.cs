using System;
using System.IO;
using System.Text;
using ClawdPet;

class ClaudeLinkTests
{
    static int checks;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
    static ClaudeEvent Ev(string session, string ev, long ticks, string tool = "", string notice = "")
    { return new ClaudeEvent { Session = session, Event = ev, Ticks = ticks, Tool = tool, Notification = notice }; }
    static int Main(string[] args)
    {
        try
        {
            string dir = args[0];
            Directory.CreateDirectory(dir);
            long t = DateTime.UtcNow.Ticks;
            ClaudeLink link = new ClaudeLink(dir);
            link.Accept(Ev("a", "SessionStart", t)); link.Select(new DateTime(t));
            Check(link.Current.Kind == "ready", "session connection");
            link.Accept(Ev("a", "UserPromptSubmit", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Thinking, "prompt starts thinking");
            link.Select(new DateTime(t).AddSeconds(10));
            Check(link.Current.Thinking, "thinking does not finish on animation timer");
            link.Accept(Ev("a", "PreToolUse", ++t, "Edit")); link.Select(new DateTime(t));
            Check(link.Current.Text.Contains("写代码"), "edit activity");
            link.Accept(Ev("a", "PermissionRequest", ++t));
            link.Accept(Ev("b", "UserPromptSubmit", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Kind == "waiting", "permission has priority across sessions");
            link.Accept(Ev("b", "Stop", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Kind == "waiting", "other session completion does not clear waiting");
            link.Accept(Ev("a", "PostToolUse", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Thinking, "tool result resumes thinking");
            link.Accept(Ev("a", "Notification", ++t, "", "idle_prompt")); link.Select(new DateTime(t));
            Check(link.Current.Thinking, "late idle notification cannot interrupt working");
            link.Accept(Ev("a", "Stop", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Kind == "done", "response completion");
            link.Accept(Ev("a", "PreToolUse", t - 20, "Bash")); link.Select(new DateTime(t));
            Check(link.Current.Kind == "done", "stale event ignored");
            link.Select(new DateTime(t).AddSeconds(9));
            Check(link.Current == null, "completed notice expires");
            link.Accept(Ev("a", "PreToolUse", ++t, "AskUserQuestion")); link.Select(new DateTime(t));
            Check(link.Current.Kind == "waiting", "question needs user input");
            link.Accept(Ev("a", "StopFailure", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Kind == "error", "API failure notice");
            link.Accept(Ev("a", "PostToolUseFailure", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Thinking && link.Current.Text.Contains("未成功"), "tool failure is not final completion");
            link.Accept(Ev("a", "SessionEnd", ++t)); link.Select(new DateTime(t).AddSeconds(9));
            Check(link.SessionCount == 1, "ended session removed independently");
            link.Accept(Ev("b", "UserPromptSubmit", ++t)); link.Select(new DateTime(t).AddMinutes(31));
            Check(link.Current == null && link.SessionCount == 0, "abandoned activity expires");
            ClaudeEvent parsed = ClaudeLink.ParseHook("{\"session_id\":\"safe\",\"hook_event_name\":\"PreToolUse\",\"tool_name\":\"Bash\",\"prompt\":\"PRIVATE_PROMPT\",\"tool_input\":{\"command\":\"PRIVATE_COMMAND\"}}");
            Check(parsed != null && parsed.Tool == "Bash", "parse official stdin JSON");
            ClaudeLink.WriteEvent(dir, parsed);
            string[] files = Directory.GetFiles(dir, "event-*.json");
            string stored = File.ReadAllText(files[0]);
            Check(!stored.Contains("PRIVATE") && !stored.Contains("tool_input"), "conversation and commands are not persisted");
            File.WriteAllText(Path.Combine(dir, "event-corrupt.json"), "{broken", Encoding.UTF8);
            link.Poll();
            Check(link.Current != null && link.Current.Thinking, "queue consumption survives corrupt event");
            Check(Directory.GetFiles(dir, "event-*.json").Length == 0, "consumed queue files cleaned up");
            link.Publish(true);
            Check(File.ReadAllText(Path.Combine(dir, "status.json")).Contains("working"), "diagnostic status");
            link.Publish(false);
            Check(File.ReadAllText(Path.Combine(dir, "status.json")).Contains("disabled"), "disabled status");
            Check(ClaudeLink.ParseHook("{\"session_id\":\"a\",\"hook_event_name\":\"Stop\",\"agent_id\":\"child\"}") == null, "subagent cannot complete parent");
            Check(ClaudeLink.ParseHook("{\"hook_event_name\":\"Stop\"}") == null, "missing session ignored");
            Check(ClaudeLink.ParseHook("{\"session_id\":\"a\",\"hook_event_name\":\"Unrecognized\"}") == null, "unknown event ignored");
            ClaudeLink desktop = new ClaudeLink(dir);
            t = DateTime.UtcNow.Ticks;
            desktop.Accept(new ClaudeEvent { Session = "desktop:test", Event = "DesktopAction", Source = "desktop", Action = "dance", Text = "你好\n桌宠", Duration = 20, Ticks = t, Request = "r1" });
            desktop.Select(new DateTime(t).AddSeconds(10));
            Check(desktop.Current.Kind == "dance" && desktop.Current.Text == "你好桌宠", "desktop duration and text sanitization");
            desktop.Select(new DateTime(t).AddSeconds(21));
            Check(desktop.Current == null && desktop.SessionCount == 0, "desktop action lease expires");
            desktop.Accept(new ClaudeEvent { Session = "desktop:test", Event = "DesktopAction", Source = "desktop", Action = "thinking", Duration = 20, Ticks = ++t, Request = "r2" });
            desktop.Accept(Ev("code", "PermissionRequest", ++t)); desktop.Select(new DateTime(t));
            Check(desktop.Current.Kind == "waiting" && desktop.Current.Source != "desktop", "Code permission takes priority over chat display");
            desktop.Accept(new ClaudeEvent { Session = "desktop:test", Event = "DesktopAction", Source = "desktop", Action = "idle", Duration = 1, Ticks = ++t, Request = "r3" });
            desktop.Select(new DateTime(t));
            Check(desktop.SessionCount == 1 && desktop.Current.Kind == "waiting", "desktop release preserves Code session");
            desktop.Publish(true);
            string snapshot = File.ReadAllText(Path.Combine(dir, "status.json"));
            Check(snapshot.Contains("r2") && snapshot.Contains("r3"), "concurrent requests retain consumption acknowledgments");
            ClaudeLink mixed = new ClaudeLink(dir);
            mixed.Accept(new ClaudeEvent { Session = "desktop:claude", Source = "desktop", Event = "DesktopAction", Action = "dance", Duration = 20, Ticks = ++t });
            mixed.Accept(new ClaudeEvent { Session = "codex:openai", Source = "codex", Event = "DesktopAction", Action = "happy", Duration = 20, Ticks = ++t, Request = "codex1" });
            mixed.Select(new DateTime(t).AddSeconds(10));
            Check(mixed.Current.Source == "codex" && mixed.Current.Text.StartsWith("ChatGPT"), "ChatGPT label and duration preserved");
            mixed.Publish(true);
            Check(File.ReadAllText(Path.Combine(dir, "status.json")).Contains("\"source\":\"codex\""), "diagnostics identify Codex source");
            mixed.Accept(new ClaudeEvent { Session = "desktop:claude", Source = "codex", Event = "DesktopAction", Action = "idle", Duration = 1, Ticks = ++t });
            Check(mixed.SessionCount == 2, "source cannot release another client namespace");
            mixed.Accept(new ClaudeEvent { Session = "codex:openai", Source = "codex", Event = "DesktopAction", Action = "idle", Duration = 1, Ticks = ++t });
            mixed.Select(new DateTime(t));
            Check(mixed.SessionCount == 1 && mixed.Current.Source == "desktop", "Codex release preserves Claude Desktop action");
            mixed.Accept(Ev("code", "PermissionRequest", ++t));
            mixed.Accept(new ClaudeEvent { Session = "codex:openai", Source = "codex", Event = "DesktopAction", Action = "thinking", Duration = 20, Ticks = ++t });
            mixed.Select(new DateTime(t));
            Check(mixed.Current.Kind == "waiting" && mixed.Current.Source != "codex", "Codex action respects Claude Code permission priority");
            Check(DesktopMcp.StateText("wave").StartsWith("Claude Desktop"), "existing Claude label retained");
            Console.WriteLine("SUCCESS: " + checks + " checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
