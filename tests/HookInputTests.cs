using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using ClawdPet;

class HookInputTests
{
    static int checks;
    static void Check(bool pass, string name)
    {
        if (!pass) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
    static int Main()
    {
        try
        {
            Check(HookInput.Read(new StringReader("")) == "", "empty stdin");
            string text = new string('x', HookInput.MaxLength);
            Check(HookInput.Read(new StringReader(text)) == text, "maximum input accepted without truncation");
            Check(HookInput.Read(new StringReader(text + "x")) == null, "oversized input rejected");
            string payload = "{\"hook_event_name\":\"UserPromptSubmit\",\"session_id\":\"中文会话\",\"prompt\":\"不保存正文\"}";
            using (var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(payload)), Encoding.UTF8))
            {
                string input = HookInput.Read(reader);
                Check(ClaudeLink.ParseHook(input).Session == "中文会话", "Claude UTF-8 session preserved");
                Check(CodexHooks.Parse(input).Session == CodexHooks.Source + ":中文会话", "Codex UTF-8 session preserved");
            }
            var fields = new Dictionary<string, object> { { "text", "abcdef" }, { "number", 42 }, { "null", null } };
            Check(HookInput.Field(fields, "text", 3) == "abc", "metadata length bounded");
            Check(HookInput.Field(fields, "number", 10) == "", "non-string metadata ignored");
            Check(HookInput.Field(fields, "null", 10) == "" && HookInput.Field(fields, "missing", 10) == "", "null and missing metadata ignored");
            Console.WriteLine("SUCCESS: " + checks + " input checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
