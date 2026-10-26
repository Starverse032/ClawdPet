using System;
using System.IO;
using ClawdPet;

class CodexHooksTests
{
    static int checks;
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); checks++; }
    static ClaudeEvent Ev(string kind, string turn, long ticks, string tool = "")
    { return new ClaudeEvent { Source = CodexHooks.Source, Session = "codex-hook:main", Event = kind, Turn = turn, Ticks = ticks, Tool = tool }; }
    static int Main(string[] args)
    {
        try
        {
            var parsed = CodexHooks.Parse("{\"session_id\":\"main\",\"turn_id\":\"turn-1\",\"hook_event_name\":\"UserPromptSubmit\",\"prompt\":\"PRIVATE_PROMPT\",\"transcript_path\":\"PRIVATE_PATH\",\"last_assistant_message\":\"PRIVATE_REPLY\",\"tool_input\":{\"command\":\"PRIVATE_COMMAND\"}}");
            Check(parsed.Source == CodexHooks.Source && parsed.Session == "codex-hook:main" && parsed.Turn == "turn-1", "parse scoped lifecycle metadata");
            Directory.CreateDirectory(args[0]);
            ClaudeLink.WriteEvent(args[0], parsed);
            Check(!File.ReadAllText(Directory.GetFiles(args[0], "event-*.json")[0]).Contains("PRIVATE"), "prompt, reply, transcript path and tool arguments discarded");
            Check(CodexHooks.Parse("{\"session_id\":\"main\",\"hook_event_name\":\"SubagentStop\"}") == null, "subagent stop cannot complete parent");
            Check(CodexHooks.Parse("{\"session_id\":\"main\",\"hook_event_name\":\"Stop\",\"agent_id\":\"child\"}") == null, "child event ignored");
            Check(CodexHooks.Parse("{\"hook_event_name\":\"Stop\"}") == null, "missing session rejected");
            var link = new ClaudeLink(args[0]);
            long t = DateTime.UtcNow.Ticks;
            link.Accept(Ev("UserPromptSubmit", "one", t)); link.Select(new DateTime(t));
            Check(link.Current.Thinking && link.Current.Text.StartsWith("ChatGPT · "), "new turn begins ChatGPT thinking");
            string firstText = link.Current.Text;
            link.Select(new DateTime(t).AddSeconds(5));
            Check(link.Current.Text != firstText && Array.Exists(Lines.Verbs, x => link.Current.Text == "ChatGPT · " + x + "…"), "thinking rotates existing Think verbs");
            link.Select(new DateTime(t).AddSeconds(30));
            Check(link.Current.Thinking, "thinking does not expire on normal animation timer");
            link.Accept(Ev("PermissionRequest", "one", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Kind == "waiting" && link.Current.Text.Contains("ChatGPT"), "permissions display ChatGPT waiting");
            link.Accept(Ev("PostToolUse", "one", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Thinking, "tool completion resumes thinking");
            link.Accept(Ev("PreToolUse", "one", ++t, "request_user_input_async")); link.Select(new DateTime(t));
            Check(link.Current.Kind == "waiting", "questions show waiting state");
            link.Accept(Ev("UserPromptSubmit", "two", ++t));
            link.Accept(Ev("Stop", "one", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Thinking && link.Current.Turn == "two", "late old-turn stop cannot complete new turn");
            link.Accept(Ev("SessionStart", "", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Thinking && link.Current.Turn == "two", "resume during active turn cannot reset state");
            link.Accept(Ev("Stop", "two", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Kind == "done" && link.Current.Text.Contains("ChatGPT"), "turn completes with ChatGPT label");
            link.Accept(Ev("PostToolUse", "two", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Kind == "done", "late tool event cannot revive completed turn");
            link.Accept(Ev("UserPromptSubmit", "three", ++t));
            link.Accept(Ev("Interrupt", "three", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Kind == "interrupted" && !link.Current.Active, "interrupt clears busy state");
            link.Accept(Ev("PostToolUse", "three", ++t)); link.Select(new DateTime(t));
            Check(link.Current.Kind == "interrupted", "late result cannot revive interrupted turn");
            link.Select(new DateTime(t).AddSeconds(9));
            Check(link.Current == null, "completion notice returns to free activity");
            link.Accept(Ev("UserPromptSubmit", "four", ++t));
            link.Accept(new ClaudeEvent { Session = "codex:mcp", Source = "codex", Event = "DesktopAction", Action = "idle", Duration = 1, Ticks = ++t });
            link.Select(new DateTime(t));
            Check(link.Current.Thinking, "manual MCP idle cannot clear automatic lifecycle");
            link.Publish(true);
            string status = File.ReadAllText(Path.Combine(args[0], "status.json"));
            Check(status.Contains("\"source\":\"chatgpt\"") && !status.Contains("codex-hook:main") && !status.Contains("turn-1"), "diagnostics label ChatGPT without session or turn identifiers");
            link.Accept(Ev("SessionEnd", "", ++t)); link.Select(new DateTime(t).AddSeconds(9));
            Check(link.SessionCount == 0, "session end releases automatic state");
            Console.WriteLine("SUCCESS: " + checks + " lifecycle checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
