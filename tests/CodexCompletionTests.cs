using System;
using System.IO;
using System.Text;
using System.Threading;
using ClawdPet;

class CodexCompletionTests
{
    static int checks;
    static void Check(bool value, string name)
    { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
    static string Record(string turn, string text)
    { return "{\"timestamp\":\"2026-10-25T11:19:20.811Z\",\"ordinal\":56,\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"root_turn_id\":\"" + turn + "\",\"turn_id\":\"" + turn + "\",\"last_agent_message\":\"" + text + "\"}}\n"; }
    static int Main(string[] args)
    {
        try
        {
            string id = "01a16ebf-3b42-70b0-8fd4-c1f5a31ce96c";
            string turn = "01a172c9-4bf2-7fa0-b916-f65160c79c02";
            string other = "01a172c9-4bf2-7fa0-b916-f65160c79c03";
            string session = "codex-hook:" + id;
            DateTime now = DateTime.UtcNow;
            if (args.Length > 1 && args[1] == "--real")
            {
                var real = new CodexCompletionMonitor(args[0]).Check(session, turn, now);
                Check(real != null && real.Event == "Stop" && real.Turn == turn, "actual stuck desktop turn completion detected (metadata only)");
                return 0;
            }
            string root = args[0];
            Directory.CreateDirectory(root);
            string file = Path.Combine(root, "rollout-test-" + id + ".jsonl");
            if (File.Exists(file)) File.Delete(file);
            var monitor = new CodexCompletionMonitor(root);
            Check(monitor.Check("codex-hook:../invalid", turn, now) == null, "invalid session cannot select files");
            Check(monitor.Check(session, "", now) == null, "missing turn cannot end unrelated activity");
            Check(monitor.Check(session, turn, now) == null, "missing rollout leaves active turn running");
            File.WriteAllText(file, Record(other, "old reply"), new UTF8Encoding(false));
            Check(monitor.Check(session, turn, now.AddSeconds(6)) == null, "older completed turn cannot end current turn");
            File.AppendAllText(file, "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"phase\":\"final_answer\"}}\n");
            Check(monitor.Check(session, turn, now.AddSeconds(7)) == null, "final answer text is not a lifecycle completion");
            string completion = Record(turn, "PRIVATE_REPLY");
            File.AppendAllText(file, completion.TrimEnd('\n'));
            Check(monitor.Check(session, turn, now.AddSeconds(8)) == null, "partially appended record waits for newline");
            File.AppendAllText(file, "\n");
            var end = monitor.Check(session, turn, now.AddSeconds(9));
            Check(end != null && end.Event == "Stop" && end.Turn == turn, "missing Stop recovered from exact completed turn");
            Check(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(end).IndexOf("PRIVATE", StringComparison.Ordinal) < 0, "recovered event retains no reply text or path");
            File.WriteAllText(file, Record(turn, new string('x', 2100000)));
            Check(monitor.Check(session, turn, now.AddSeconds(10)) != null, "completion with multi-megabyte answer recognized using bounded metadata header");
            File.WriteAllText(file, Record(turn, "done").Replace("\"ordinal\":56,", ""));
            Check(monitor.Check(session, turn, now.AddSeconds(10)) != null, "record without ordinal is also supported");
            File.WriteAllText(file, "broken record\n");
            Check(monitor.Check(session, turn, now.AddSeconds(11)) == null, "truncated or malformed rollout does not complete a turn");
            File.WriteAllText(file, Record(turn, "done"));
            Check(monitor.Check(session, other, now.AddSeconds(12)) == null, "new turn resets file cache and ignores previous completion");
            File.AppendAllText(file, Record(other, "done again"));
            Check(monitor.Check(session, other, now.AddSeconds(13)) != null, "second turn completion detected");

            Environment.SetEnvironmentVariable("CODEX_HOME", Path.GetDirectoryName(root));
            string bridge = Path.Combine(root, "bridge");
            var link = new ClaudeLink(bridge);
            link.Accept(new ClaudeEvent { Source = CodexHooks.Source, Session = session, Turn = other, Event = "UserPromptSubmit", Ticks = DateTime.UtcNow.Ticks });
            for (int i = 0; i < 40; i++)
            {
                link.Poll();
                if (link.Current != null && link.Current.Kind == "done") break;
                Thread.Sleep(100);
            }
            Check(link.Current != null && link.Current.Kind == "done", "background completion reaches normal desktop state machine");
            long completedAt = link.Current.Ticks;
            link.Accept(new ClaudeEvent { Source = CodexHooks.Source, Session = session, Turn = other, Event = "Stop", Ticks = completedAt + 1 });
            link.Select(new DateTime(completedAt + 1));
            Check(link.Current.Ticks == completedAt, "late real Stop cannot repeat completion animation");
            link.Select(new DateTime(completedAt).AddSeconds(9));
            Check(link.Current == null, "recovered completion returns to free activity after dance");
            Console.WriteLine("SUCCESS: " + checks + " completion checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
