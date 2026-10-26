// ============================================================================
//  Clawd 桌宠 —— Claude Code 吉祥物 Clawd 的 Windows 桌面宠物
//
//  C# 5 / WinForms / .NET Framework 4.x，用 Windows 自带的 csc.exe 即可编译
//  （见 build.bat）。渲染使用 UpdateLayeredWindow 逐像素透明窗口：像素风清晰锐利，
//  透明区域鼠标可穿透。
//
//  精灵以“子像素”为单位设计，原型来自 Claude Code 欢迎界面里的 Clawd：
//       ▐▛███▜▌
//      ▝▜█████▛▘
//        ▘▘ ▝▝
//  坐标原点 = 脚底中心，y 轴向下（向上为负）。默认身体 24×16、手臂 4×4、
//  眼睛 2×4、四条腿 2×4。
// ============================================================================
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Clawd 桌宠")]
[assembly: System.Reflection.AssemblyProduct("ClawdPet")]
[assembly: System.Reflection.AssemblyVersion(ClawdPet.Program.Version)]

namespace ClawdPet
{
    // ------------------------------------------------------------------ 入口
    static class Program
    {
        public const string Version = "1.4.0";
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--claude-hook") return ClaudeLink.RunHook();
            if (args.Length > 0 && args[0] == "--desktop-mcp") return DesktopMcp.Run();
            if (args.Length > 0 && args[0] == "--codex-mcp") return DesktopMcp.Run("codex");
            if (args.Length > 0 && args[0] == "--codex-hook") return CodexHooks.Run();
            // 每显示器 DPI 感知（清单里也声明了，这里是兜底）
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (Exception) { try { Native.SetProcessDPIAware(); } catch (Exception) { } }

            if (args.Length >= 2 && args[0] == "--make-icon") { IconMaker.WriteIco(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--sheet") { SpriteSheet.Render(args[1]); return 0; }

            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\ClawdPet.SingleInstance", out created))
            {
                if (!created)
                {
                    if (args.Length > 0 && args[0] == "--ensure-running") return 0;
                    // 已经在运行了：让正在运行的那份再召唤一只小伙伴
                    try { using (EventWaitHandle ev = EventWaitHandle.OpenExisting(PetContext.SummonEventName)) ev.Set(); }
                    catch (Exception) { }
                    return 0;
                }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.Error(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log.Error(e.ExceptionObject as Exception); };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PetContext());
            }
            return 0;
        }
    }

    // ------------------------------------------------------------------ Win32
    static class Native
    {
        public const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8,
            WS_EX_NOACTIVATE = 0x8000000, WS_EX_APPWINDOW = 0x40000, WS_EX_TRANSPARENT = 0x20;
        public const int WS_CAPTION = 0xC00000;
        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        public const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        public const int ULW_ALPHA = 2;
        public const int SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        public const int SW_HIDE = 0, SW_SHOWNA = 8;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int CX, CY; public SIZE(int w, int h) { CX = w; CY = h; } }
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage,
            out IntPtr bits, IntPtr hSection, uint offset);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, int flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT r, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int v, int size);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO gi);
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO lii);

        public const uint GA_ROOT = 2, GW_OWNER = 4, GW_HWNDPREV = 3;
        public const int GUI_MENUMODES = 0x4 | 0x8 | 0x10;   // INMENUMODE | SYSTEMMENUMODE | POPUPMENUMODE

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public int cbSize; public uint dwTime; }

        /// <summary>用户多久没碰键盘鼠标了（秒）。</summary>
        public static double IdleSeconds()
        {
            LASTINPUTINFO lii = new LASTINPUTINFO();
            lii.cbSize = Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref lii)) return 0;
            return unchecked((uint)Environment.TickCount - lii.dwTime) / 1000.0;
        }

        /// <summary>前台线程是否正在显示菜单（别的程序的右键菜单等）。</summary>
        public static bool ForeignMenuOpen()
        {
            GUITHREADINFO gi = new GUITHREADINFO();
            gi.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
            return GetGUIThreadInfo(0, ref gi) && (gi.flags & GUI_MENUMODES) != 0;
        }
    }

    static class Log
    {
        static int count;
        public static void Error(Exception ex)
        {
            if (ex == null || count++ > 50) return;
            try
            {
                Directory.CreateDirectory(PetSettings.Dir);
                File.AppendAllText(Path.Combine(PetSettings.Dir, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }

    static class Dpi
    {
        static bool broken;
        static double sys;
        public static double ForPoint(Point p)
        {
            if (!broken)
            {
                try
                {
                    IntPtr m = Native.MonitorFromPoint(new Native.POINT(p.X, p.Y), 2 /* NEAREST */);
                    uint dx, dy;
                    if (Native.GetDpiForMonitor(m, 0, out dx, out dy) == 0 && dx > 0) return dx;
                }
                catch (Exception) { broken = true; }
            }
            if (sys <= 0) using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sys = g.DpiX;
            return sys;
        }
    }

    // ------------------------------------------------------------------ 设置
    class PetSettings
    {
        public int Size = 3;              // 100% 缩放下每个子像素的像素数
        public bool Follow, Quiet;
        public bool WalkOnWindows = true, HideFullscreen = true, Reminders = true, ClaudeEnabled = true;

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClawdPet"); }
        }
        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static PetSettings Load()
        {
            PetSettings s = new PetSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                    bool b = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
                    int n;
                    int.TryParse(v, out n);
                    switch (k)
                    {
                        case "Size": if (n >= 1 && n <= 8) s.Size = n; break;
                        case "Follow": s.Follow = b; break;
                        case "Quiet": s.Quiet = b; break;
                        case "WalkOnWindows": s.WalkOnWindows = b; break;
                        case "HideFullscreen": s.HideFullscreen = b; break;
                        case "Reminders": s.Reminders = b; break;
                        case "ClaudeEnabled": s.ClaudeEnabled = b; break;
                    }
                }
            }
            catch (Exception ex) { Log.Error(ex); }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, string.Join(Environment.NewLine, new string[] {
                    "Size=" + Size, "Follow=" + B(Follow), "Quiet=" + B(Quiet), "WalkOnWindows=" + B(WalkOnWindows),
                    "HideFullscreen=" + B(HideFullscreen), "Reminders=" + B(Reminders),
                    "ClaudeEnabled=" + B(ClaudeEnabled) }), Encoding.UTF8);
            }
            catch (Exception ex) { Log.Error(ex); }
        }
        static string B(bool b) { return b ? "1" : "0"; }
    }

    static class AutoStart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "ClawdPet";
        // 任务管理器“启动”页里禁用时，Windows 会在这里写一个首字节为奇数的值
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        static string Cmd { get { return "\"" + Application.ExecutablePath + "\""; } }
        public static bool Enabled
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                        if (k == null || !string.Equals(k.GetValue(Name) as string, Cmd, StringComparison.OrdinalIgnoreCase)) return false;
                    using (RegistryKey a = Registry.CurrentUser.OpenSubKey(Approved))
                    {
                        byte[] v = a == null ? null : a.GetValue(Name) as byte[];
                        if (v != null && v.Length > 0 && (v[0] & 1) != 0) return false;
                    }
                    return true;
                }
                catch (Exception) { return false; }
            }
            set
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
                    {
                        if (value) k.SetValue(Name, Cmd); else k.DeleteValue(Name, false);
                    }
                    using (RegistryKey a = Registry.CurrentUser.OpenSubKey(Approved, true))
                        if (a != null) a.DeleteValue(Name, false);   // 清掉“已禁用”标记
                }
                catch (Exception ex) { Log.Error(ex); }
            }
        }
    }

    // ------------------------------------------------------------------ 台词
    static class Lines
    {
        public static readonly string[] Chatter = {
            "你好呀，我是 Clawd！", "今天也要好好写代码哦～", "需要我帮你 debug 吗？", "记得 Ctrl+S！",
            "git commit 了吗？", "该喝水啦～", "眨眨眼，让眼睛休息一下", "Bug 退散！", "rm -rf 之前请三思……",
            "编译通过！……大概吧", "要不要写个单元测试？", "我不是螃蟹，我是 Clawd！", "Tab 还是空格？这是个问题",
            "今天的代码也很优雅呢", "你已经很棒啦！", "（伸懒腰）", "嘿嘿～", "摸鱼时间到～", "先跑一下测试再说",
            "404：灵感未找到", "It works on my machine!", "让我 /compact 一下脑子……", "代码写累了就看看我吧",
            "我在这儿陪着你", "提交信息要写清楚哦", "今天也辛苦啦" };
        public static readonly string[] Click = { "嘿！", "痒痒的～", "嘻嘻", "再摸一下！", "干嘛呀～", "在呢在呢！", "(｡･ω･｡)", "嗯？" };
        public static readonly string[] Poke = { "别戳啦！", "再戳我要生气了！", "哼！", "呜……痛痛" };
        public static readonly string[] Pet = { "好舒服～", "呼噜呼噜……", "最喜欢你啦！", "再摸摸～", "嘿嘿嘿" };
        public static readonly string[] Drag = { "哇啊——", "放我下来！", "飞起来啦！", "好高！", "要去哪儿呀？" };
        public static readonly string[] Dizzy = { "天旋地转……", "@_@", "星星在转圈……", "晕乎乎的" };
        public static readonly string[] Wake = { "唔……我醒啦", "再睡五分钟……", "（揉眼睛）", "嗯？发生什么了？" };
        public static readonly string[] Sleep = { "困了……", "我先眯一会儿", "Zzz…", "晚安～" };
        public static readonly string[] FallOff = { "哎呀！", "啊——", "地面不见了！", "掉下去啦！" };
        public static readonly string[] OnWindow = { "这里视野不错！", "上来啦！", "站得高看得远", "嘿咻！" };
        public static readonly string[] Ride = { "坐稳了！", "出发咯～", "嗖——", "带我去兜风！" };
        public static readonly string[] Jump = { "跳！", "嘿！", "冲呀！" };
        public static readonly string[] Friend = { "嗨，伙伴！", "一起写代码吧！", "你也是 Clawd？", "击个掌！" };
        public static readonly string[] Summon = { "我来啦！", "叫我吗？", "Clawd 报到！" };
        public static readonly string[] Recall = { "我回来啦！", "在这儿呢！", "来了来了～" };
        public static readonly string[] Dance = { "跳舞时间！", "动次打次～", "一起摇摆！" };
        public static readonly string[] ThinkDone = { "想好了！", "有思路了！", "就这么办！", "搞定！", "原来如此！" };
        public static readonly string[] Remind = {
            "已经陪你 {0} 分钟啦，起来活动一下吧～", "工作 {0} 分钟了，喝口水、看看远方吧", "{0} 分钟过去啦，伸个懒腰？" };
        // Claude Code 风格的“思考中”动词
        public static readonly string[] Verbs = {
            "Clauding", "Pondering", "Noodling", "Percolating", "Combobulating", "Ruminating", "Cogitating",
            "Schlepping", "Finagling", "Moseying", "Simmering", "Spelunking", "Wrangling", "Vibing", "Brewing",
            "Conjuring", "Tinkering", "Puzzling", "Mulling", "Hatching" };

        public static string Greeting()
        {
            DateTime n = DateTime.Now;
            double t = n.Hour + n.Minute / 60.0;
            if (t >= 5 && t < 9) return "早上好！今天也元气满满～";
            if (t >= 9 && t < 11.5) return "上午好！一起写代码吧";
            if (t >= 11.5 && t < 13.5) return "中午啦，吃饭了吗？";
            if (t >= 13.5 && t < 18) return "下午好！来杯咖啡？";
            if (t >= 18 && t < 23) return "晚上好～今天辛苦啦";
            return "这么晚还不睡？早点休息哦";
        }
    }

    // ------------------------------------------------------------------ 像素画
    enum EyeStyle { Open, Closed, Happy, Squint, Dizzy, Wide, Sleepy }
    enum ArmStyle { Normal, Up, Out, Down, Hidden }

    class Pose
    {
        public int BodyW = 24, BodyH = 16, LegLen = 4, LegDX, OffX, EyeDX, EyeDY;
        public int[] Lift = new int[4];
        public ArmStyle ArmL = ArmStyle.Normal, ArmR = ArmStyle.Normal;
        public EyeStyle Eyes = EyeStyle.Open;
        public bool Blush;
        public int Frame = -1;    // >= 0：画敲键盘动画的这一帧（见 Typing），忽略上面的参数
        public bool Mirror;       // 敲键盘帧水平镜像（笔记本放在左边）

        public string Key()
        {
            if (Frame >= 0) return "T" + Frame + (Mirror ? "m" : "") + (Eyes == EyeStyle.Closed ? "c" : "");
            return BodyW + "," + BodyH + "," + LegLen + "," + LegDX + "," + OffX + "," + EyeDX + "," + EyeDY + ","
                + Lift[0] + Lift[1] + Lift[2] + Lift[3] + "," + (int)ArmL + (int)ArmR + (int)Eyes + (Blush ? "b" : "");
        }
    }

    static class Art
    {
        public static readonly Color BodyColor = Color.FromArgb(215, 119, 87);   // Claude 橙 #D77757
        public static readonly Color EyeColor = Color.FromArgb(28, 20, 18);
        public static readonly Color BlushColor = Color.FromArgb(245, 158, 150);
        // 精灵包围盒（子像素，相对脚底中心），留出手臂、拉伸、阴影的空间
        public const int BoxLeft = -21, BoxTop = -27, BoxW = 42, BoxH = 29;

        public static readonly string[] HeartPat = { ".XX.XX.", "XXXXXXX", "XXXXXXX", ".XXXXX.", "..XXX..", "...X..." };
        public static readonly string[] ZedPat = { "XXXX", "..X.", ".X..", "XXXX" };
        public static readonly string[] BangPat = { "XX", "XX", "XX", "..", "XX" };
        public static readonly string[] DropPat = { ".X.", ".X.", "XXX", "XXX", ".X." };
        public static readonly string[] StarPat = { "..X..", ".XXX.", "XXXXX", ".XXX.", "..X.." };
        public static readonly string[] NotePat = { "..XXX", "..X.X", "..X..", "XXX..", "XXX.." };
        static readonly string[] SquintPat = { "X..", ".X.", "..X", ".X.", "X.." };
        static readonly string[] DizzyPat = { "X.X", ".X.", "X.X" };
        static readonly string[] WidePat = { "XXX", "X.X", "X.X", "XXX" };
        static readonly string[] HappyPat = { ".XX.", "X..X", "X..X" };

        // 关于 0 对称的四舍五入，保证左右部件严格镜像
        static int Sym(double c) { return c < 0 ? -(int)Math.Floor(-c + 0.5) : (int)Math.Floor(c + 0.5); }

        static void R(Graphics g, Brush b, float ox, float oy, float ps, int x, int y, int w, int h)
        {
            if (w > 0 && h > 0) g.FillRectangle(b, ox + x * ps, oy + y * ps, w * ps, h * ps);
        }

        static void Pat(Graphics g, Brush b, float ox, float oy, float ps, string[] pat, int x, int y, bool mirror)
        {
            for (int r = 0; r < pat.Length; r++)
            {
                string row = pat[r];
                for (int c = 0; c < row.Length; c++)
                    if ((mirror ? row[row.Length - 1 - c] : row[c]) == 'X') R(g, b, ox, oy, ps, x + c, y + r, 1, 1);
            }
        }

        /// <summary>在 (ox, oy)=脚底中心 处画 Clawd；ps = 每个子像素的像素数。</summary>
        public static void DrawSprite(Graphics g, float ox, float oy, float ps, Pose p)
        {
            if (p.Frame >= 0) { Typing.Draw(g, ox, oy, ps, p.Frame, p.Mirror, p.Eyes == EyeStyle.Closed); return; }
            ox += p.OffX * ps;
            using (SolidBrush body = new SolidBrush(BodyColor))
            using (SolidBrush eye = new SolidBrush(EyeColor))
            {
                int bw = p.BodyW, bh = p.BodyH;
                int bottom = -p.LegLen, top = bottom - bh, left = -bw / 2, right = left + bw;

                // 四条小短腿
                if (p.LegLen > 0)
                {
                    double[] centers = { -9, -5, 5, 9 };
                    for (int i = 0; i < 4; i++)
                    {
                        int c = Sym(centers[i] * bw / 24.0);
                        int lift = Math.Max(0, Math.Min(p.Lift[i], p.LegLen - 1));
                        R(g, body, ox, oy, ps, c - 1 + p.LegDX, bottom, 2, p.LegLen - lift);
                    }
                }

                // 身体 + 手臂
                R(g, body, ox, oy, ps, left, top, bw, bh);
                int armY = top + bh / 2;
                Arm(g, body, ox, oy, ps, p.ArmL, left, armY, true);
                Arm(g, body, ox, oy, ps, p.ArmR, right, armY, false);

                // 眼睛
                int ex = Sym(7.0 * bw / 24.0);
                int eyBase = top + (bh >= 18 ? 5 : bh >= 14 ? 4 : 3);
                int ey = eyBase + p.EyeDY;
                Eye(g, eye, ox, oy, ps, p.Eyes, -ex + p.EyeDX, ey, true);
                Eye(g, eye, ox, oy, ps, p.Eyes, ex + p.EyeDX, ey, false);

                if (p.Blush)
                    using (SolidBrush bl = new SolidBrush(BlushColor))
                    {
                        R(g, bl, ox, oy, ps, -ex - 3, eyBase + 5, 3, 1);
                        R(g, bl, ox, oy, ps, ex, eyBase + 5, 3, 1);
                    }
            }
        }

        // edge = 身体左/右边缘的 x；left 表示左臂（向 -x 方向伸出）
        static void Arm(Graphics g, Brush b, float ox, float oy, float ps, ArmStyle s, int edge, int armY, bool left)
        {
            switch (s)
            {
                case ArmStyle.Normal: Limb(g, b, ox, oy, ps, edge, left, 0, armY, 4, 4); break;
                case ArmStyle.Up:
                    Limb(g, b, ox, oy, ps, edge, left, 0, armY - 1, 3, 3);
                    Limb(g, b, ox, oy, ps, edge, left, 2, armY - 4, 3, 3);
                    break;
                case ArmStyle.Out: Limb(g, b, ox, oy, ps, edge, left, 0, armY, 6, 3); break;
                case ArmStyle.Down: Limb(g, b, ox, oy, ps, edge, left, 0, armY + 1, 2, 5); break;
            }
        }

        static void Limb(Graphics g, Brush b, float ox, float oy, float ps, int edge, bool left, int d, int y, int w, int h)
        {
            R(g, b, ox, oy, ps, left ? edge - d - w : edge + d, y, w, h);
        }

        // cx = 眼睛中心（落在两个子像素之间），常规眼睛占 [cx-1, cx+1)
        static void Eye(Graphics g, Brush b, float ox, float oy, float ps, EyeStyle s, int cx, int y, bool left)
        {
            int x3 = left ? cx - 2 : cx - 1;   // 3 格宽图案的起点：左右严格镜像
            switch (s)
            {
                case EyeStyle.Open: R(g, b, ox, oy, ps, cx - 1, y, 2, 4); break;
                case EyeStyle.Sleepy: R(g, b, ox, oy, ps, cx - 1, y + 2, 2, 2); break;
                case EyeStyle.Closed: R(g, b, ox, oy, ps, cx - 2, y + 3, 4, 1); break;
                case EyeStyle.Happy: Pat(g, b, ox, oy, ps, HappyPat, cx - 2, y + 1, false); break;
                case EyeStyle.Squint: Pat(g, b, ox, oy, ps, SquintPat, x3, y, !left); break;
                case EyeStyle.Dizzy: Pat(g, b, ox, oy, ps, DizzyPat, x3, y + 1, false); break;
                case EyeStyle.Wide: Pat(g, b, ox, oy, ps, WidePat, x3, y, false); break;
            }
        }

        /// <summary>带描边的像素小图标（爱心、Zzz、感叹号……）。(x, y) 为左上角。</summary>
        public static void Glyph(Graphics g, string[] pat, float x, float y, int cell, Color fill, Color outline)
        {
            int o = Math.Max(1, cell / 2);
            using (GraphicsPath outer = new GraphicsPath(FillMode.Winding))
            using (GraphicsPath inner = new GraphicsPath(FillMode.Winding))
            {
                for (int r = 0; r < pat.Length; r++)
                    for (int c = 0; c < pat[r].Length; c++)
                        if (pat[r][c] == 'X')
                        {
                            outer.AddRectangle(new RectangleF(x + c * cell - o, y + r * cell - o, cell + 2 * o, cell + 2 * o));
                            inner.AddRectangle(new RectangleF(x + c * cell, y + r * cell, cell, cell));
                        }
                // 只填描边那一圈，半透明淡出时内部颜色才不会变脏
                using (Region ring = new Region(outer))
                using (SolidBrush ob = new SolidBrush(outline))
                {
                    ring.Exclude(inner);
                    g.FillRegion(ob, ring);
                }
                using (SolidBrush fb = new SolidBrush(fill)) g.FillPath(fb, inner);
            }
        }
    }

    // ------------------------------------------------------------------ 思考动画：Clawd 敲键盘
    /// <summary>
    /// 思考时的“敲键盘”逐帧动画：转身 → 掏出笔记本 → 打开放到地上 → 伸个懒腰 →
    /// 两只手轮流敲键盘（循环） → 合上收好 → 转回来。
    /// 动作和节奏参照 Claude 应用里的 Clawd-Laptop 动画（12 帧/秒），按本程序的像素比例重新绘制。
    /// 坐标单位为子像素，原点 = 脚底中心，y 向上为负；按“笔记本在右边”绘制，朝左时水平镜像。
    /// 每帧是若干矩形：O 身体，D 背光面（侧身时的暗面），E 眼睛，G 笔记本。
    /// </summary>
    static class Typing
    {
        public const double Fps = 12;
        public static readonly Color ShadeColor = Color.FromArgb(190, 104, 77);
        public static readonly Color LaptopColor = Color.FromArgb(139, 139, 139);

        // 常用部件
        const string FrontBody = "O -12 -20 24 16";
        const string FrontEyes = "E -8 -16 2 4, E 6 -16 2 4";
        const string FrontEyesLow = "E -8 -15 2 4, E 6 -15 2 4";
        const string Legs = "O -10 -4 2 4, O -6 -4 2 4, O 4 -4 2 4, O 8 -4 2 4";
        const string LegsShaded = "D -10 -4 2 4, O -6 -4 2 4, O 4 -4 2 4, O 8 -4 2 4";
        // 蹲着敲键盘：身体压低、左边一条暗面表示侧过身，右眼贴着脸的边缘
        const string Crouch = "O -12 -17 24 14, D -12 -17 6 14, E -3 -12 2 4, E 10 -12 2 4, "
            + "D -10 -3 2 3, O -6 -3 2 3, O 4 -3 2 3, O 8 -3 2 3";
        // 打开放在地上的笔记本：键盘底座 + 向后仰的屏幕
        const string Desk = "G 16 -2 9 2, G 24 -3 2 1, G 25 -4 2 1, G 26 -5 2 1, G 27 -6 2 1, G 28 -7 2 1, G 29 -8 2 1";

        static readonly string[] Defs = {
            // 0 转身：近处的眼睛照常，远处的眼睛被挤成一条线；后手抬起、前手放低
            "O -12 -19 24 15, E 6 -15 2 4, E -9 -12 3 1, O -16 -16 4 4, O 12 -10 3 4, " + Legs,
            // 1 从背后掏出合着的笔记本，举过肩膀
            FrontBody + ", " + FrontEyes + ", O -16 -11 4 4, O 12 -19 4 4, "
                + "G 14 -24 3 1, G 14 -23 5 1, G 17 -22 7 1, G 20 -21 4 1, G 14 -20 10 1, " + Legs,
            // 2 举在头顶掀开屏幕
            FrontBody + ", " + FrontEyes + ", O -16 -11 4 4, O 12 -21 4 5, G 14 -22 9 1, G 21 -28 2 6, " + Legs,
            // 3 斜着放下来
            FrontBody + ", " + FrontEyesLow + ", O -16 -12 4 4, O 12 -12 3 4, "
                + "G 26 -16 3 1, G 25 -15 5 2, G 24 -13 5 1, G 15 -12 13 1, G 15 -11 10 1, " + Legs,
            // 4 放到地上，手还按着
            FrontBody + ", " + FrontEyesLow + ", O -16 -11 4 4, O 12 -7 4 3, " + Desk + ", " + Legs,
            // 5 放好了
            FrontBody + ", " + FrontEyes + ", O -16 -12 4 4, O 12 -12 4 4, " + Desk + ", " + Legs,
            // 6 伸个懒腰：两只手举过头顶，身体拉长并侧过来
            "O -12 -21 24 17, D -12 -21 3 17, O -6 -25 4 4, O 7 -25 4 4, E -5 -17 2 4, E 9 -17 2 4, " + Desk + ", " + LegsShaded,
            // 7 俯下身准备打字
            Crouch + ", O 12 -14 5 5, D 12 -10 5 1, " + Desk,
            // 8-10 打字循环：前手（亮）和后手（暗）轮流落在键盘上
            Crouch + ", O 12 -8 4 4, D 16 -7 1 3, D 12 -4 4 1, " + Desk,
            Crouch + ", O 12 -11 3 4, D 12 -5 4 3, " + Desk,
            Crouch + ", D 12 -12 4 2, O 12 -8 4 5, D 16 -7 1 3, " + Desk,
            // 11 合上屏幕
            Crouch + ", O 12 -12 5 4, D 12 -9 5 1, G 16 -2 9 2, G 24 -8 2 6",
            // 12 夹着合好的笔记本收起来
            "O -12 -19 24 15, D -12 -19 3 15, E -5 -15 2 4, E 9 -15 2 4, O -18 -12 6 4, O 12 -12 4 4, G 16 -13 9 2, " + LegsShaded,
        };

        // 播放顺序（每个数字是一帧，1/12 秒）
        public static readonly int[] Intro = { 0, 0, 0, 0, 1, 2, 2, 3, 4, 5, 6, 6, 7 };
        public static readonly int[] Loop = { 8, 9, 10 };
        public static readonly int[] Outro = { 11, 12, 12, 0, 0, 0 };
        public static readonly int Count = Defs.Length;

        static readonly int[][] frames;
        static readonly Rectangle[] bounds;

        static Typing()
        {
            frames = new int[Defs.Length][];
            bounds = new Rectangle[Defs.Length];
            for (int i = 0; i < Defs.Length; i++)
            {
                string[] parts = Defs[i].Split(',');
                int[] f = new int[parts.Length * 5];
                Rectangle b = Rectangle.Empty;
                for (int k = 0; k < parts.Length; k++)
                {
                    string[] t = parts[k].Trim().Split(' ');
                    f[k * 5] = "ODEG".IndexOf(t[0][0]);
                    for (int j = 1; j <= 4; j++) f[k * 5 + j] = int.Parse(t[j]);
                    Rectangle r = new Rectangle(f[k * 5 + 1], f[k * 5 + 2], f[k * 5 + 3], f[k * 5 + 4]);
                    b = b.IsEmpty ? r : Rectangle.Union(b, r);
                }
                frames[i] = f;
                bounds[i] = b;
            }
        }

        /// <summary>第 t 秒处的思考帧（先播开场，再无限循环打字）。</summary>
        public static int ThinkFrame(double t)
        {
            int f = Math.Max(0, (int)(t * Fps));
            return f < Intro.Length ? Intro[f] : Loop[(f - Intro.Length) % Loop.Length];
        }

        /// <summary>第 t 秒处的收拾帧。</summary>
        public static int OutroFrame(double t)
        {
            return Outro[Math.Max(0, Math.Min(Outro.Length - 1, (int)(t * Fps)))];
        }

        public static double OutroSeconds { get { return Outro.Length / Fps; } }

        /// <summary>该帧的包围盒（子像素，已按朝向镜像）。</summary>
        public static Rectangle Bounds(int frame, bool mirror)
        {
            Rectangle b = bounds[frame];
            return mirror ? new Rectangle(-b.Right, b.Y, b.Width, b.Height) : b;
        }

        /// <summary>所有帧的包围盒：整段动画里窗口大小保持不变。</summary>
        public static Rectangle AllBounds(bool mirror)
        {
            Rectangle b = bounds[0];
            foreach (Rectangle r in bounds) b = Rectangle.Union(b, r);
            return mirror ? new Rectangle(-b.Right, b.Y, b.Width, b.Height) : b;
        }

        public static void Draw(Graphics g, float ox, float oy, float ps, int frame, bool mirror, bool blink)
        {
            int[] f = frames[frame];
            using (SolidBrush body = new SolidBrush(Art.BodyColor))
            using (SolidBrush shade = new SolidBrush(ShadeColor))
            using (SolidBrush eye = new SolidBrush(Art.EyeColor))
            using (SolidBrush laptop = new SolidBrush(LaptopColor))
            {
                Brush[] brushes = { body, shade, eye, laptop };
                for (int k = 0; k < f.Length; k += 5)
                {
                    int x = f[k + 1], y = f[k + 2], w = f[k + 3], h = f[k + 4];
                    if (f[k] == 2 && blink && h > 1) { y += h - 1; h = 1; }   // 眨眼：只剩最下面一条
                    if (mirror) x = -x - w;
                    g.FillRectangle(brushes[f[k]], ox + x * ps, oy + y * ps, w * ps, h * ps);
                }
            }
        }
    }

    // ------------------------------------------------------------------ 字体 & 对话气泡
    static class Fonts
    {
        static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();
        static readonly string[] TextFaces = { "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimHei", "Segoe UI" };
        static readonly string[] SymFaces = { "Segoe UI Symbol", "Segoe UI", "Arial Unicode MS" };

        public static Font Text(float px) { return Get(TextFaces, px); }
        public static Font Symbol(float px) { return Get(SymFaces, px); }

        static Font Get(string[] faces, float px)
        {
            int k = Math.Max(6, (int)Math.Round(px));
            string key = faces[0] + "|" + k;
            Font f;
            if (cache.TryGetValue(key, out f)) return f;
            f = null;
            foreach (string face in faces)
            {
                try
                {
                    Font t = new Font(face, k, FontStyle.Regular, GraphicsUnit.Pixel);
                    // 中文系统下 Font.Name 是本地化名（“微软雅黑”），所以比较英文名
                    if (string.Equals(t.FontFamily.GetName(0x409), face, StringComparison.OrdinalIgnoreCase)) { f = t; break; }
                    t.Dispose();
                }
                catch (Exception) { }
            }
            if (f == null) f = new Font(FontFamily.GenericSansSerif, k, FontStyle.Regular, GraphicsUnit.Pixel);
            cache[key] = f;
            return f;
        }
    }

    class Bubble
    {
        public string Text;
        public double Age, Life;
        public bool Thinking;
        public bool High;   // 头顶有“！”或晕眩星星时把气泡抬高，别挡住它们
        public float Alpha
        {
            get
            {
                double a = Math.Min(1, Age / 0.15) * Math.Min(1, (Life - Age) / 0.35);
                return (float)Math.Max(0, Math.Min(1, a));
            }
        }
    }

    class BubbleLayout
    {
        public RectangleF Box;
        public float TailX, Tail, Rad, Pad, GlyphW, TextW, TextH, Ui, Alpha;
        public bool TailDown;
        public string Text, Glyph;
        public Font Font, GlyphFont;
        public Rectangle Bounds;
    }

    static class BubbleArt
    {
        public static readonly StringFormat SF;
        static readonly Graphics measure;
        static readonly Dictionary<string, SizeF> sizes = new Dictionary<string, SizeF>();

        static BubbleArt()
        {
            SF = (StringFormat)StringFormat.GenericTypographic.Clone();
            SF.Trimming = StringTrimming.None;
            measure = Graphics.FromImage(new Bitmap(1, 1));
            measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        static SizeF Measure(string s, Font f, float maxW)
        {
            string key = f.Size + "|" + (int)maxW + "|" + s;
            SizeF sz;
            if (sizes.TryGetValue(key, out sz)) return sz;
            if (sizes.Count > 300) sizes.Clear();
            sz = measure.MeasureString(s, f, new SizeF(maxW, 4000), SF);
            sizes[key] = sz;
            return sz;
        }

        /// <summary>计算气泡位置：anchorX 为宠物中心，headY 为头顶，footY 为脚底，mon 为所在显示器。</summary>
        public static BubbleLayout Layout(string text, string glyph, float ui, float anchorX, float headY, float footY, Rectangle mon)
        {
            BubbleLayout L = new BubbleLayout();
            L.Ui = ui; L.Text = text; L.Glyph = glyph;
            L.Font = Fonts.Text(13.5f * ui);
            L.Pad = 8 * ui; L.Tail = 6 * ui; L.Rad = 8 * ui;
            SizeF ts = Measure(text, L.Font, 210 * ui);
            L.TextW = (float)Math.Ceiling(ts.Width);
            L.TextH = (float)Math.Ceiling(ts.Height);
            if (glyph != null)
            {
                L.GlyphFont = Fonts.Symbol(16f * ui);
                L.GlyphW = (float)Math.Ceiling(17 * ui);   // 固定宽度，避免转圈图标让气泡抖动
            }
            float w = L.TextW + L.GlyphW + 2 * L.Pad;
            float h = Math.Max(L.TextH + 2 * L.Pad, 2 * L.Rad + 2);
            float x = anchorX - w / 2;
            x = Math.Max(mon.Left + 4, Math.Min(mon.Right - 4 - w, x));
            float y = headY - 3 * ui - L.Tail - h;
            L.TailDown = true;
            if (y < mon.Top + 4) { y = footY + 4 * ui + L.Tail; L.TailDown = false; }
            L.Box = new RectangleF((float)Math.Round(x), (float)Math.Round(y), (float)Math.Ceiling(w), (float)Math.Ceiling(h));
            L.TailX = (float)Math.Round(Math.Max(L.Box.Left + L.Rad + L.Tail, Math.Min(L.Box.Right - L.Rad - L.Tail, anchorX)));
            float top = L.Box.Top - (L.TailDown ? 0 : L.Tail), bottom = L.Box.Bottom + (L.TailDown ? L.Tail : 0);
            float m = 3 * ui;
            L.Bounds = Rectangle.FromLTRB((int)Math.Floor(L.Box.Left - m), (int)Math.Floor(top - m),
                (int)Math.Ceiling(L.Box.Right + m), (int)Math.Ceiling(bottom + m + 2 * ui));
            return L;
        }

        static GraphicsPath Shape(RectangleF r, float rad, float tx, float tail, bool down)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            if (!down) { p.AddLine(tx - tail, r.Top, tx, r.Top - tail); p.AddLine(tx, r.Top - tail, tx + tail, r.Top); }
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            if (down) { p.AddLine(tx + tail, r.Bottom, tx, r.Bottom + tail); p.AddLine(tx, r.Bottom + tail, tx - tail, r.Bottom); }
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Draw(Graphics g, BubbleLayout L)
        {
            int a = (int)Math.Round(255 * Math.Max(0, Math.Min(1, L.Alpha)));
            if (a < 3) return;
            SmoothingMode oldSm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Shape(L.Box, L.Rad, L.TailX, L.Tail, L.TailDown))
            {
                using (GraphicsPath sh = (GraphicsPath)path.Clone())
                using (Matrix mx = new Matrix(1, 0, 0, 1, 0, 2 * L.Ui))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(a * 45 / 255, 0, 0, 0)))
                {
                    sh.Transform(mx);
                    g.FillPath(sb, sh);
                }
                using (SolidBrush fb = new SolidBrush(Color.FromArgb(a * 250 / 255, 255, 255, 255))) g.FillPath(fb, path);
                using (Pen pen = new Pen(Color.FromArgb(a, 188, 96, 68), Math.Max(1f, 1.5f * L.Ui)))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float tx = L.Box.X + L.Pad, ty = L.Box.Y + L.Pad;
            if (L.Glyph != null)
            {
                float lineH = L.Font.GetHeight(), gh = L.GlyphFont.GetHeight();
                using (SolidBrush gb = new SolidBrush(Color.FromArgb(a, Art.BodyColor)))
                    g.DrawString(L.Glyph, L.GlyphFont, gb, tx, ty + (lineH - gh) / 2, SF);
            }
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(a, 40, 36, 34)))
                g.DrawString(L.Text, L.Font, tb, new RectangleF(tx + L.GlyphW, ty, L.TextW + 2, L.TextH + 2), SF);
            g.SmoothingMode = oldSm;
        }
    }

    // ------------------------------------------------------------------ 粒子
    enum PK { Heart, Zed, Dust, Sweat, Bang, Star, Note }

    class Particle
    {
        public PK K;
        public double X, Y, VX, VY, Age, Life, Phase;
        public bool Rel;   // true：坐标相对脚底（跟着宠物走）
    }

    // ------------------------------------------------------------------ 逐像素透明的绘图表面
    class Surface : IDisposable
    {
        public IntPtr DC, HBmp, Bits;
        public Bitmap Bmp;
        public Graphics G;
        public int W, H;
        IntPtr oldBmp;

        public void Ensure(int w, int h)
        {
            if (G != null && w <= W && h <= H && W * H <= Math.Max(4 * w * h, 256 * 256)) return;
            Free();
            W = Math.Max(32, (w + 31) / 32 * 32);
            H = Math.Max(32, (h + 31) / 32 * 32);
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            DC = Native.CreateCompatibleDC(screen);
            Native.ReleaseDC(IntPtr.Zero, screen);
            Native.BITMAPINFOHEADER bi = new Native.BITMAPINFOHEADER();
            bi.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            bi.biWidth = W;
            bi.biHeight = -H;   // 自上而下
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            HBmp = Native.CreateDIBSection(DC, ref bi, 0, out Bits, IntPtr.Zero, 0);
            if (HBmp == IntPtr.Zero) { Free(); throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDIBSection 失败"); }
            oldBmp = Native.SelectObject(DC, HBmp);
            Bmp = new Bitmap(W, H, W * 4, PixelFormat.Format32bppPArgb, Bits);
            G = Graphics.FromImage(Bmp);
        }

        public void Free()
        {
            if (G != null) { G.Dispose(); G = null; }
            if (Bmp != null) { Bmp.Dispose(); Bmp = null; }
            if (DC != IntPtr.Zero)
            {
                if (oldBmp != IntPtr.Zero) Native.SelectObject(DC, oldBmp);
                Native.DeleteDC(DC);
                DC = IntPtr.Zero;
                oldBmp = IntPtr.Zero;
            }
            if (HBmp != IntPtr.Zero) { Native.DeleteObject(HBmp); HBmp = IntPtr.Zero; }
            W = H = 0;
        }

        public void Dispose() { Free(); }
    }

    // ------------------------------------------------------------------ 窗口扫描（让 Clawd 站在窗口顶上）
    class WinInfo
    {
        public IntPtr H;
        public Rectangle R;
        public Rectangle DockContent;
        public bool Platform, Occluder, Dock;
    }

    // MyDockAPP includes a screen-wide transparent canvas for magnified icons.
    // Capture only that window, in memory, to find the pixels it actually draws.
    // PrintWindow can wait on another process, so it must never run on the pet's UI thread.
    class DockContentScanner
    {
        class Sample
        {
            public bool Running;
            public double Next;
            public Size Size;
            public Rectangle Content;
        }
        readonly Dictionary<IntPtr, Sample> samples = new Dictionary<IntPtr, Sample>();

        public Rectangle Get(IntPtr window, Rectangle canvas, double now)
        {
            lock (samples)
            {
                Sample sample;
                if (!samples.TryGetValue(window, out sample))
                    samples.Add(window, sample = new Sample());
                Size size = canvas.Size;
                if (!sample.Running && now >= sample.Next && size.Width <= 16384 && size.Height <= 2048)
                {
                    sample.Running = true;
                    sample.Next = now + 0.5;
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        Rectangle content = Rectangle.Empty;
                        try
                        {
                            using (Bitmap bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb))
                            {
                                bool printed;
                                using (Graphics graphics = Graphics.FromImage(bitmap))
                                {
                                    IntPtr dc = graphics.GetHdc();
                                    try { printed = Native.PrintWindow(window, dc, 2); }
                                    finally { graphics.ReleaseHdc(dc); }
                                }
                                if (printed) content = FindContent(bitmap);
                            }
                        }
                        catch (Exception) { }
                        finally
                        {
                            lock (samples)
                            {
                                sample.Size = size;
                                sample.Content = content;
                                sample.Running = false;
                            }
                        }
                    });
                }
                if (sample.Size != canvas.Size || sample.Content.IsEmpty) return Rectangle.Empty;
                Rectangle result = sample.Content;
                result.Offset(canvas.Location);
                return result;
            }
        }

        internal static Rectangle FindContent(Bitmap bitmap)
        {
            int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1, count = 0;
            BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[bitmap.Width];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    int first = -1, last = -1;
                    for (int x = 0; x < row.Length; x++)
                    {
                        uint pixel = unchecked((uint)row[x]);
                        // PrintWindow represents MyDockFinder's transparent padding as black.
                        if ((pixel >> 24) < 16 || ((pixel & 255) <= 12 &&
                            ((pixel >> 8) & 255) <= 12 && ((pixel >> 16) & 255) <= 12)) continue;
                        if (first < 0) first = x;
                        last = x; count++;
                    }
                    if (first < 0) continue;
                    // Update the four bounds once per occupied row, not per icon pixel.
                    left = Math.Min(left, first); right = Math.Max(right, last);
                    top = Math.Min(top, y); bottom = y;
                }
            }
            finally { bitmap.UnlockBits(data); }
            if (count < 32 || right - left < 2 || bottom - top < 2) return Rectangle.Empty;
            // A failed capture filled with a solid background must not recreate a full-canvas floor.
            if (left == 0 && top == 0 && right == bitmap.Width - 1 && bottom == bitmap.Height - 1)
                return Rectangle.Empty;
            return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

    }

    class WindowScanner
    {
        List<WinInfo> list = new List<WinInfo>();
        List<WinInfo> building;
        double lastScan = -1;
        readonly uint myPid = (uint)Process.GetCurrentProcess().Id;
        readonly Native.EnumWindowsProc cb;
        readonly DockContentScanner dockContent = new DockContentScanner();
        static readonly StringBuilder sb = new StringBuilder(256);

        public WindowScanner() { cb = OnWindow; }

        public void Refresh(double now)
        {
            if (lastScan >= 0 && now - lastScan < 0.25) return;
            lastScan = now;
            building = new List<WinInfo>();
            Native.EnumWindows(cb, IntPtr.Zero);   // 按 Z 序从上到下
            list = building;
            building = null;
        }

        bool OnWindow(IntPtr h, IntPtr lParam)
        {
            try
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == myPid || IsCloaked(h)) return true;
                int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                string cls = ClassOf(h);
                // MyDockFinder 不预留 Windows 工作区，并使用工具/透明窗口绘制 Dock。
                // 只识别 Dock 本体；Finder 顶栏、菜单和 Launchpad 不能充当地面。
                bool dock = cls.Equals("MyDockAPP", StringComparison.OrdinalIgnoreCase);
                if (!dock && (ex & Native.WS_EX_TRANSPARENT) != 0) return true;   // 鼠标穿透的透明覆盖层
                Rectangle r;
                if (!FrameRect(h, out r) || r.Width < 2 || r.Height < 2) return true;
                if (cls == "Progman" || cls == "WorkerW") return true;      // 桌面
                bool tool = (ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0;
                bool shell = cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd";
                WinInfo w = new WinInfo();
                w.H = h;
                w.R = r;
                w.Dock = dock;
                if (dock && IsBottomDock(r, Screen.FromRectangle(r).Bounds, 4))
                    w.DockContent = dockContent.Get(h, r, lastScan);
                w.Occluder = !shell;
                w.Platform = !tool && !shell && !Native.IsZoomed(h) && Native.GetWindowTextLength(h) > 0
                    && r.Width >= 120 && r.Height >= 60;
                building.Add(w);
            }
            catch (Exception) { }
            return true;
        }

        public double DockFloor(Rectangle monitor, double x, double halfWidth, double floor, double gap)
        {
            foreach (WinInfo w in list)
                if (w.Dock) floor = BottomDockFloor(w.R, w.DockContent, monitor, x, halfWidth, floor, gap);
            return floor;
        }

        static bool IsBottomDock(Rectangle canvas, Rectangle monitor, double gap)
        {
            Rectangle visible = Rectangle.Intersect(canvas, monitor);
            return visible.Width >= 2 && visible.Height >= 2 && canvas.Width > canvas.Height &&
                canvas.Top >= monitor.Top + monitor.Height * 0.55 && canvas.Bottom >= monitor.Bottom - gap * 4;
        }

        // Only the visible icons/bar support the pet. The transparent headroom and
        // left/right margins remain free, so walking past either edge starts a fall.
        internal static double BottomDockFloor(Rectangle canvas, Rectangle content, Rectangle monitor, double x, double halfWidth,
            double floor, double gap)
        {
            if (!IsBottomDock(canvas, monitor, gap) || content.IsEmpty) return floor;
            Rectangle visible = Rectangle.Intersect(Rectangle.Intersect(content, canvas), monitor);
            if (visible.Width < 2 || visible.Height < 2) return floor;
            if (x + halfWidth <= visible.Left || x - halfWidth >= visible.Right) return floor;
            return Math.Min(floor, visible.Top - gap);
        }

        public static string ClassOf(IntPtr h)
        {
            sb.Length = 0;
            Native.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool IsCloaked(IntPtr h)
        {
            int v;
            try { return Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out v, 4) == 0 && v != 0; }
            catch (Exception) { return false; }
        }

        public static bool FrameRect(IntPtr h, out Rectangle r)
        {
            Native.RECT rc;
            bool ok = false;
            try { ok = Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out rc, 16) == 0; }
            catch (Exception) { rc = new Native.RECT(); }
            if (!ok) ok = Native.GetWindowRect(h, out rc);
            r = ok ? Rectangle.FromLTRB(rc.Left, rc.Top, rc.Right, rc.Bottom) : Rectangle.Empty;
            return ok;
        }

        /// <summary>当前仍可站立的窗口矩形（可见、未最小化/最大化、未隐藏）。</summary>
        public static bool PlatformRect(IntPtr h, out Rectangle r)
        {
            r = Rectangle.Empty;
            if (!Native.IsWindow(h) || !Native.IsWindowVisible(h) || Native.IsIconic(h) || Native.IsZoomed(h) || IsCloaked(h))
                return false;
            return FrameRect(h, out r) && r.Width > 0;
        }

        bool OccludedAt(int idx, int x, int y)
        {
            for (int i = 0; i < idx; i++)
                if (list[i].Occluder && list[i].R.Contains(x, y)) return true;
            return false;
        }

        /// <summary>窗口 h 的顶边在 (x, y) 处是否被它上面的窗口挡住。</summary>
        public bool IsOccluded(IntPtr h, int x, int y)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].H == h) return false;
                if (list[i].Occluder && list[i].R.Contains(x, y)) return true;
            }
            return false;
        }

        /// <summary>从 prevY 落到 y 的过程中，穿过了哪个窗口的顶边（取最高的那个）。</summary>
        public WinInfo FindLanding(double prevY, double y, double x, double margin, IntPtr ignore, Rectangle area, double headroom)
        {
            WinInfo best = null;
            for (int i = 0; i < list.Count; i++)
            {
                WinInfo w = list[i];
                if (!w.Platform || w.H == ignore) continue;
                int top = w.R.Top;
                if (prevY > top + 1 || y < top) continue;
                if (x < w.R.Left + margin || x > w.R.Right - margin) continue;
                if (top - headroom < area.Top || top >= area.Bottom - 4) continue;
                if (OccludedAt(i, (int)Math.Round(x), top + 2)) continue;
                if (best == null || top < best.R.Top) best = w;
            }
            return best;
        }

        /// <summary>找一个能跳上去的窗口顶边。tx 返回落点 x。</summary>
        public WinInfo FindJumpTarget(double x, double y, double minRise, double maxRise, double maxDX, double inset,
            double headroom, IntPtr current, Rectangle area, Random rng, out double tx)
        {
            WinInfo best = null;
            double bestScore = double.MaxValue;
            tx = 0;
            for (int i = 0; i < list.Count; i++)
            {
                WinInfo w = list[i];
                if (!w.Platform || w.H == current) continue;
                double rise = y - w.R.Top;
                if (rise < minRise || rise > maxRise) continue;
                if (w.R.Top - headroom < area.Top) continue;
                double l = w.R.Left + inset, r = w.R.Right - inset;
                if (r <= l) continue;
                double cx = Math.Max(l, Math.Min(r, x + (rng.NextDouble() - 0.5) * 2 * inset));
                if (Math.Abs(cx - x) > maxDX || cx < area.Left || cx > area.Right) continue;
                if (OccludedAt(i, (int)Math.Round(cx), w.R.Top + 2)) continue;
                double score = Math.Abs(cx - x) + rise * 0.3 + rng.NextDouble() * 80;
                if (score < bestScore) { bestScore = score; best = w; tx = cx; }
            }
            return best;
        }
    }

    // ------------------------------------------------------------------ 宠物本体
    class PetForm : Form
    {
        // Think = 掏出笔记本敲键盘；PackUp = 思考结束后合上笔记本收好
        enum St { Fall, Land, Idle, Walk, Jump, Sit, Sleep, Wave, Think, Happy, Drag, Dizzy, Look, Dance, Annoyed, PackUp }

        // Claude Code 的转圈图标
        static readonly string[] Spinner = { "·", "✢", "✳", "✶", "✻", "✽", "✻", "✶", "✳", "✢" };

        readonly PetContext ctx;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly Surface surf = new Surface();
        readonly List<Particle> parts = new List<Particle>();
        readonly List<double[]> dragHist = new List<double[]>();

        // 位置 = 脚底中心（屏幕物理像素），速度单位 px/s
        double fx, fy, vx, vy, jumpVX, jumpVY;
        int facing = 1;
        St st = St.Fall;
        double stT, stDur, now, seed;

        // 环境
        int ps = 3;            // 每个子像素的像素数
        double ui = 1;         // 显示器缩放（1 = 100%）
        Rectangle area, mon;   // 工作区 / 显示器
        IntPtr platform;       // 脚下站着的窗口（Zero = 地面/任务栏）
        Rectangle platRect;
        double occludedT;
        bool rideSaid;
        IntPtr ignoreHwnd;
        double ignoreUntil;

        // 动画
        double walkDist, targetX = double.NaN;
        bool running, dizzyOnLand, thrown;
        int edgeChoice, jumpPhase;
        double blinkAt, blinkEnd, nextFx;
        Bubble bubble;
        BubbleLayout bubbleLayout;
        string greetText;
        bool thinkForced, leaving, hidden, wallHit, settling;
        int laptopSide = 1;      // 敲键盘时笔记本放在哪边（1 右，-1 左）
        bool packThenDone;       // 收好笔记本后说“想好了！”（普通思考结束时）
        bool claudePose;
        long claudeReaction;
        double leaveAt;

        // 交互
        bool mouseDown, dragging, cursorOk = true, clickThrough;
        Point downPos, cursor, lastCursor;
        double grabDX, grabDY, dvx, dvy, lastShake;
        int shakeCount, shakeSign, clickCombo;
        double lastInteraction, lastClick, lastFriendHi = -999, nextChatter, petAccum;

        // 渲染缓存
        string lastSig;
        Point lastLoc;

        public bool AllowClose;

        public PetForm(PetContext ctx, double x, double y, string greet)
        {
            this.ctx = ctx;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            // 注意：不要设置 TopMost 属性——它会让窗口在 Show() 时被激活、抢走焦点。
            // 置顶靠 CreateParams 里的 WS_EX_TOPMOST。
            Text = "Clawd";
            Bounds = new Rectangle((int)x, (int)y, 1, 1);
            fx = x;
            fy = y;
            now = ctx.Now;
            seed = ctx.Rng.NextDouble() * 100;
            lastInteraction = now;
            nextChatter = now + 50 + ctx.Rng.NextDouble() * 80;
            blinkAt = now + 1.5 + ctx.Rng.NextDouble() * 3;
            greetText = greet;
            facing = ctx.Rng.Next(2) == 0 ? -1 : 1;
            menu.Font = PetContext.MenuFont;
            menu.Closed += OnMenuClosed;
            cursor = lastCursor = Cursor.Position;
            UpdateScreen();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;   // 屏蔽 Alt+F4
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { surf.Dispose(); menu.Dispose(); }
            base.Dispose(disposing);
        }

        public bool IsSleeping { get { return st == St.Sleep; } }
        public bool MenuOpen { get { return menu.Visible; } }
        public bool IsLeaving { get { return leaving || AllowClose; } }
        public bool CanRemind { get { return !hidden && !IsLeaving && !IsDisposed; } }
        public double FeetX { get { return fx; } }
        public double FeetY { get { return fy; } }
        bool Grounded { get { return st != St.Fall && st != St.Drag && !(st == St.Jump && jumpPhase == 1); } }
        double Gravity { get { return 2600 * ui; } }

        // 地面高度。任务栏自动隐藏时工作区 = 整个屏幕：离底边留 2px，
        // 让最底下几行保持透明可穿透，鼠标移到底边仍能唤出任务栏。
        double Floor
        {
            get
            {
                ctx.Scanner.Refresh(now);
                double floor = area.Bottom >= mon.Bottom ? area.Bottom - 2 : area.Bottom;
                return ctx.Scanner.DockFloor(mon, fx, 14 * ps, floor, Math.Max(4, 4 * ui));
            }
        }

        // ============================================================== 主循环
        public void Tick(double dt, double t)
        {
            now = t;
            if (leaving && now >= leaveAt) { leaving = false; AllowClose = true; ctx.Post(Close); return; }
            UpdateScreen();

            bool wantHide = !dragging && ctx.IsFullscreenAt(new Point((int)Math.Round(fx), (int)Math.Round(fy) - 2));
            if (wantHide != hidden)
            {
                hidden = wantHide;
                Native.ShowWindow(Handle, hidden ? Native.SW_HIDE : Native.SW_SHOWNA);
                if (!hidden) Raise();
                lastSig = null;
            }
            if (hidden) return;
            if (menu.Visible) { Render(); return; }   // 菜单打开时原地待命

            UpdateCursor(dt);
            Rectangle before = SpriteRect();
            UpdateBlink();
            switch (st)
            {
                case St.Drag: UpdateDrag(); break;
                case St.Fall: UpdateAir(dt); break;
                case St.Jump:
                    if (jumpPhase == 1) UpdateAir(dt);
                    else if (UpdateSupport(dt) && stT >= 0.14) Launch();
                    break;
                default:
                    if (UpdateSupport(dt)) UpdateGround(dt);
                    break;
            }
            stT += dt;
            UpdateParticles(dt);
            if (bubble != null)
            {
                bubble.Age += dt;
                if (bubble.Age >= bubble.Life) bubble = null;
            }
            UpdateChatter();
            // 在移动之后再决定是否穿透，并且覆盖移动前后两个位置：高速下落时也点得到
            UpdateClickThrough(Rectangle.Union(before, SpriteRect()));
            Render();
        }

        void SetState(St s, double dur)
        {
            // 落地缓冲 / 摔晕被打断（点击、菜单命令……）：把挂起的问候、晕眩结清，
            // 免得留到以后某次落地才冒出来。正常结束时 settling=true，不受影响。
            if ((st == St.Land || st == St.Dizzy) && s != St.Land && s != St.Fall && !settling)
            {
                if (st == St.Land) { vx = 0; dizzyOnLand = false; }
                greetText = null;
            }
            // 思考被打断：转圈气泡也收起来
            if (st == St.Think && s != St.Think && bubble != null && bubble.Thinking)
                bubble.Life = Math.Min(bubble.Life, bubble.Age + 0.3);
            if (s == St.Think && st != St.Think) laptopSide = ChooseLaptopSide();
            st = s;
            // Debug captures include brief transitions such as the confirmation hop.
            if (!string.IsNullOrEmpty(DumpDir)) lastDump = -1;
            stT = 0;
            stDur = dur;
            edgeChoice = 0;
            if (s != St.Walk) running = false;
        }

        void UpdateScreen()
        {
            Point p = new Point((int)Math.Round(fx), (int)Math.Round(fy) - 2);
            Screen best = null;
            foreach (Screen s in Screen.AllScreens)
                if (s.Bounds.Contains(p)) { best = s; break; }
            if (best == null) best = Screen.FromPoint(p);
            area = best.WorkingArea;
            mon = best.Bounds;
            ui = Dpi.ForPoint(p) / 96.0;
            int nps = Math.Max(1, (int)Math.Floor(ctx.Settings.Size * ui + 0.5));
            if (nps != ps) { ps = nps; lastSig = null; }
        }

        static bool ScreenAt(double x, double y)
        {
            Point p = new Point((int)Math.Round(x), (int)Math.Round(y));
            foreach (Screen s in Screen.AllScreens)
                if (s.WorkingArea.Contains(p)) return true;
            return false;
        }

        Rectangle SpriteRect()
        {
            return new Rectangle((int)(fx - 14 * ps), (int)(fy - 23 * ps), 28 * ps, 23 * ps);
        }

        string Pick(string[] a) { return a[ctx.Rng.Next(a.Length)]; }

        void Say(string text) { Say(text, false); }

        void Say(string text, bool force)
        {
            if (string.IsNullOrEmpty(text) || (ctx.Settings.Quiet && !force)) return;
            bubble = new Bubble();
            bubble.Text = text;
            bubble.Life = Math.Min(7, 2.4 + text.Length * 0.12);
            bubble.High = parts.Exists(delegate(Particle p) { return p.Rel && (p.K == PK.Bang || p.K == PK.Star); });
        }

        // ============================================================== 鼠标 / 眨眼
        void UpdateCursor(double dt)
        {
            // 锁屏 / UAC 时 GetCursorPos 会失败（Cursor.Position 会返回 0,0），此时保持上次的位置
            Native.POINT np;
            cursorOk = Native.GetCursorPos(out np);
            if (!cursorOk) return;
            Point c = new Point(np.X, np.Y);
            double ddx = c.X - lastCursor.X, ddy = c.Y - lastCursor.Y;
            double moved = Math.Sqrt(ddx * ddx + ddy * ddy);
            lastCursor = cursor = c;
            bool hovering = SpriteRect().Contains(c);
            petAccum = Math.Max(0, petAccum - 300 * ui * dt);
            if (ctx.Settings.Follow && moved > 0) lastInteraction = now;   // 跟随模式下鼠标在动就不睡觉
            if (!hovering || mouseDown) return;

            // 鼠标在 Clawd 身上来回蹭 = 摸摸
            petAccum += Math.Min(moved, 50 * ui);
            if (moved > 0) lastInteraction = now;
            if (petAccum > 450 * ui && CanReact())
            {
                petAccum = 0;
                SetState(St.Happy, 1.8);
                SpawnHearts(3);
                Say(Pick(Lines.Pet));
            }
            else if (st == St.Walk && !running && double.IsNaN(targetX))
                SetState(St.Idle, 1.5 + ctx.Rng.NextDouble());   // 鼠标放上来就停下，方便点击
        }

        /// <summary>
        /// 只有鼠标在 Clawd 身上时窗口才接收鼠标；其余时候（气泡、特效、阴影）设为
        /// WS_EX_TRANSPARENT 穿透，不挡住下面窗口的点击。
        /// </summary>
        void UpdateClickThrough(Rectangle r)
        {
            r.Inflate(2 * ps, 2 * ps);
            bool want = !mouseDown && !dragging && cursorOk && !r.Contains(cursor);
            if (want == clickThrough || !IsHandleCreated) return;
            clickThrough = want;
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            ex = want ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT;
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
        }

        bool CanReact()
        {
            return st == St.Idle || st == St.Walk || st == St.Sit || st == St.Look || st == St.Wave
                || st == St.Think || st == St.Dance;
        }

        void UpdateBlink()
        {
            if (now < blinkAt) return;
            blinkEnd = now + 0.12;
            blinkAt = ctx.Rng.NextDouble() < 0.2 ? now + 0.28 : now + 2 + ctx.Rng.NextDouble() * 4.5;
        }

        // ============================================================== 支撑面 / 物理
        /// <summary>检查脚下（地面或窗口）是否还在。返回 false 表示开始下落。</summary>
        bool UpdateSupport(double dt)
        {
            if (platform != IntPtr.Zero)
            {
                Rectangle r;
                if (!ctx.Settings.WalkOnWindows || !WindowScanner.PlatformRect(platform, out r)) { DropOff(true); return false; }
                if (r != platRect)
                {
                    // 窗口被拖动：跟着一起走。只拖一条边改大小时不水平跟随；
                    // 两边同时变（比如拖到另一个 DPI 的显示器上被缩放）时按相对位置跟随。
                    int dxl = r.Left - platRect.Left, dxr = r.Right - platRect.Right, dy = r.Top - platRect.Top;
                    int dx = dxl == dxr ? dxl : 0;
                    if (dxl != dxr && dxl != 0 && dxr != 0 && platRect.Width > 0)
                    {
                        double rel = (fx - platRect.Left) / platRect.Width;
                        double nfx = Math.Max(r.Left + 2, Math.Min(r.Right - 2, r.Left + rel * r.Width));
                        dx = (int)Math.Round(nfx - fx);
                    }
                    if (dx != 0 || dy != 0)
                    {
                        fx += dx;
                        if (!rideSaid && Math.Abs(dx) + Math.Abs(dy) > 6)
                        {
                            rideSaid = true;
                            if (ctx.Rng.NextDouble() < 0.5) Say(Pick(Lines.Ride));
                        }
                    }
                    platRect = r;
                }
                fy = r.Top;
                ctx.Scanner.Refresh(now);
                if (ctx.Scanner.IsOccluded(platform, (int)Math.Round(fx), r.Top + 2)) occludedT += dt; else occludedT = 0;
                // 被挡住、走出窗口、窗口顶边跑到屏幕外/任务栏里/太靠上：掉下来
                if (occludedT > 0.6 || fx < r.Left - 2 || fx > r.Right + 2 || r.Top - 22 * ps < area.Top
                    || r.Top >= area.Bottom - 4 || r.Top > Floor || !ScreenAt(fx, r.Top - 2))
                {
                    DropOff(true);
                    return false;
                }
                return true;
            }
            if (fy < Floor - 1) { StartFall(); return false; }
            fy = Floor;
            double half = 12 * ps;
            if (fx < area.Left + half && !ScreenAt(fx - half - 2, fy - 2)) fx = area.Left + half;
            if (fx > area.Right - half && !ScreenAt(fx + half + 2, fy - 2)) fx = area.Right - half;
            return true;
        }

        void StartFall()
        {
            platform = IntPtr.Zero;
            jumpPhase = 0;
            SetState(St.Fall, 0);
        }

        void DropOff(bool surprised)
        {
            ignoreHwnd = platform;
            ignoreUntil = now + 0.5;
            vx = 0;
            vy = 0;
            StartFall();
            if (surprised)
            {
                SpawnMark(PK.Bang);
                if (ctx.Rng.NextDouble() < 0.5) Say(Pick(Lines.FallOff));
            }
        }

        void UpdateAir(double dt)
        {
            double prevY = fy, half = 12 * ps;
            // 匀加速的精确积分（梯形），这样跳跃轨迹和 TryJumpToWindow 算出来的一致
            double nvy = Math.Min(vy + Gravity * dt, 4000 * ui);
            fx += vx * dt;
            fy += (vy + nvy) / 2 * dt;
            vy = nvy;
            if (fx - half < area.Left && !ScreenAt(fx - half - 2, fy - 2)) { fx = area.Left + half; vx = Math.Abs(vx) * 0.45; }
            if (fx + half > area.Right && !ScreenAt(fx + half + 2, fy - 2)) { fx = area.Right - half; vx = -Math.Abs(vx) * 0.45; }
            if (fy - 22 * ps < area.Top && vy < 0 && !ScreenAt(fx, fy - 22 * ps - 2))
            {
                if (prevY - 22 * ps >= area.Top) fy = area.Top + 22 * ps;   // 刚撞到天花板
                vy = -vy * 0.2;                                              // 本来就在上面（在那儿松的手）：只反弹，不瞬移
            }
            if (vy <= 0) return;
            if (ctx.Settings.WalkOnWindows)
            {
                ctx.Scanner.Refresh(now);
                // 刚从窗口边走下来时放宽落点要求，能直接踩到紧挨着的另一个窗口上
                double margin = now < ignoreUntil ? -2 : 4 * ps;
                WinInfo w = ctx.Scanner.FindLanding(prevY, fy, fx, margin, now < ignoreUntil ? ignoreHwnd : IntPtr.Zero, area, 22 * ps);
                Rectangle fr;
                // 扫描结果最多旧 0.25 秒：落脚前再确认一次窗口还在原处
                if (w != null && WindowScanner.PlatformRect(w.H, out fr) && fr.Top <= Floor && prevY <= fr.Top + 1 && fy >= fr.Top
                    && fx >= fr.Left + margin && fx <= fr.Right - margin)
                {
                    fy = fr.Top;
                    Touchdown(w.H, fr);
                    return;
                }
            }
            if (fy >= Floor) { fy = Floor; Touchdown(IntPtr.Zero, Rectangle.Empty); }
        }

        void Touchdown(IntPtr plat, Rectangle r)
        {
            double impact = vy;
            bool wasJump = st == St.Jump;
            if (impact > 2400 * ui && thrown) dizzyOnLand = true;   // 被用力扔下来才会摔晕
            thrown = false;
            if (impact > 1500 * ui && !wasJump)
            {
                vy = -impact * 0.3;   // 摔得太狠，弹一下
                vx *= 0.7;
                SpawnDust(4);
                return;
            }
            platform = plat;
            platRect = r;
            occludedT = 0;
            rideSaid = false;
            vy = 0;
            if (plat != IntPtr.Zero) fx = Math.Max(r.Left + 3 * ps, Math.Min(r.Right - 3 * ps, fx));   // 站稳在窗口上
            SpawnDust(impact > 800 * ui ? 6 : 3);
            SetState(St.Land, impact > 800 * ui ? 0.24 : 0.16);
            if (plat != IntPtr.Zero && greetText == null && ctx.Rng.NextDouble() < (wasJump ? 0.4 : 0.25)) Say(Pick(Lines.OnWindow));
        }

        /// <summary>
        /// 水平移动。返回 0 正常；±1 碰到左/右的墙（屏幕边，已夹紧，wallHit=true）或接近平台左/右边缘；
        /// 2 走出平台边缘。
        /// </summary>
        int MoveAlong(double nx)
        {
            double half = 12 * ps;
            wallHit = false;
            if (platform != IntPtr.Zero)
            {
                // 窗口伸出屏幕外的那部分顶边不能走：屏幕边当作墙
                // 正好停在墙上；如果本来就已经在墙内侧（被拖动的窗口带过去的），原地不动、不往回跳
                if (nx - half < area.Left && !ScreenAt(area.Left - 2, platRect.Top - 2) && nx < fx) { fx = Math.Min(fx, Math.Max(nx, area.Left + half)); wallHit = true; return -1; }
                if (nx + half > area.Right && !ScreenAt(area.Right + 2, platRect.Top - 2) && nx > fx) { fx = Math.Max(fx, Math.Min(nx, area.Right - half)); wallHit = true; return 1; }
                fx = nx;
                if (nx < platRect.Left + 2 || nx > platRect.Right - 2) return 2;
                double m = Math.Min(10 * ps, platRect.Width / 3.0);
                if (nx < platRect.Left + m) return -1;
                if (nx > platRect.Right - m) return 1;
                return 0;
            }
            if (nx - half < area.Left && !ScreenAt(nx - half - 2, fy - 2)) { fx = area.Left + half; wallHit = true; return -1; }
            if (nx + half > area.Right && !ScreenAt(nx + half + 2, fy - 2)) { fx = area.Right - half; wallHit = true; return 1; }
            fx = nx;
            return 0;
        }

        void WalkOff(double speedX)
        {
            ignoreHwnd = platform;
            ignoreUntil = now + 0.3;
            vx = Math.Sign(speedX) * Math.Max(Math.Abs(speedX), 90 * ui);   // 往外迈一步，好踩到旁边紧挨着的窗口
            vy = -150 * ui;   // 轻轻一跳再掉下去
            StartFall();
        }

        // ============================================================== 地面上的各种状态
        void UpdateGround(double dt)
        {
            ClaudeActivity activity = ctx.ClaudeFor(this);
            if (activity != null && st != St.Land && st != St.Dizzy)
            {
                lastInteraction = now;
                bool chat = DesktopMcp.IsChatSource(activity.Source);
                bool wantThink = chat ? activity.Kind == "working" : activity.Active && activity.Thinking;
                // 不再需要敲键盘了：先合上笔记本收好，收完再切到新状态
                if (st == St.Think && !wantThink) { StartPackUp(false); return; }
                if (st == St.PackUp && stT < stDur) return;
                if (chat)
                {
                    St pose = activity.Kind == "working" ? St.Think : activity.Kind == "dance" ? St.Dance :
                        activity.Kind == "sleep" ? St.Sleep : activity.Kind == "happy" ? St.Happy :
                        activity.Kind == "wave" ? St.Wave : St.Look;
                    if (claudeReaction != activity.Ticks || !claudePose || st != pose)
                    {
                        claudeReaction = activity.Ticks;
                        // 已经在敲键盘就接着敲，不要重新掏一遍电脑
                        if (pose == St.Think && st == St.Think) stDur = double.MaxValue;
                        else SetState(pose, double.MaxValue);
                        if (pose == St.Happy) SpawnHearts(3);
                        if (activity.Kind == "error" || activity.Kind == "waiting") SpawnMark(PK.Bang);
                        nextFx = now;
                        claudePose = true;
                    }
                    if (st == St.Dance && now >= nextFx) { SpawnNote(); nextFx = now + 0.5; }
                    if (st == St.Sleep && now >= nextFx) { SpawnZ(); nextFx = now + 1.2; }
                    return;
                }
                if (activity.Source == CodexHooks.Source && activity.Kind == "waiting" && claudeReaction != activity.Ticks)
                {
                    claudeReaction = activity.Ticks;
                    claudePose = false;
                    StartHop(); // One hop per request; then remain attentive after landing.
                    return;
                }
                if (activity.Source == CodexHooks.Source && activity.Kind == "done")
                {
                    if (claudeReaction != activity.Ticks || !claudePose || st != St.Dance)
                    {
                        claudeReaction = activity.Ticks;
                        SetState(St.Dance, double.MaxValue);
                        nextFx = now;
                        claudePose = true;
                    }
                    if (now >= nextFx) { SpawnNote(); nextFx = now + 0.5; }
                    return;
                }
                if (activity.Active)
                {
                    St desired = activity.Thinking ? St.Think : St.Look;
                    if (st != desired) SetState(desired, double.MaxValue);
                    else if (!claudePose) stDur = double.MaxValue;   // 正在敲键盘/张望：接着做，交给 AI 状态控制何时结束
                    claudePose = true;
                    return;
                }
                if (claudeReaction != activity.Ticks)
                {
                    claudeReaction = activity.Ticks;
                    if (activity.Kind == "done") { SetState(St.Happy, 1.8); SpawnHearts(3); }
                    else if (activity.Kind == "error") { SetState(St.Look, 2); SpawnMark(PK.Bang); }
                    else SetState(St.Wave, 1.8);
                    claudePose = false;
                }
            }
            if (claudePose)
            {
                claudePose = false;
                // AI 状态结束：敲着键盘就先收电脑；正在收的让它收完（PackUp 结束后自然回到 Idle）
                if (st != St.PackUp && !StartPackUp(false)) SetState(St.Idle, 1);
            }
            if (ctx.Settings.Follow && cursorOk) FollowCursor();
            switch (st)
            {
                case St.Land:
                    if (Math.Abs(vx) > 5 * ui)
                    {
                        int r = MoveAlong(fx + vx * dt);
                        vx *= Math.Exp(-8 * dt);
                        if (r == 2) { WalkOff(vx); return; }
                        if (wallHit) vx = 0;
                    }
                    if (stT >= stDur)
                    {
                        vx = 0;
                        settling = true;
                        if (dizzyOnLand) { dizzyOnLand = false; EnterDizzy(); }
                        else if (greetText != null) { SetState(St.Wave, 1.8); Say(greetText); greetText = null; }
                        else SetState(St.Idle, 0.6 + ctx.Rng.NextDouble());
                        settling = false;
                    }
                    break;
                case St.Walk:
                    Walk(dt);
                    break;
                case St.Sleep:
                    if (now >= nextFx) { SpawnZ(); nextFx = now + 1.2; }
                    if (stT >= stDur) Wake(false);
                    break;
                case St.Dance:
                    if (now >= nextFx) { SpawnNote(); nextFx = now + 0.5; }
                    if (stT >= stDur) SetState(St.Idle, 1.5);
                    break;
                case St.Think:
                    if (stT >= stDur) StartPackUp(true);
                    break;
                case St.PackUp:
                    if (stT >= stDur)
                    {
                        if (packThenDone)
                        {
                            packThenDone = false;
                            bubble = null;
                            Say(Pick(Lines.ThinkDone), thinkForced);
                            SetState(St.Happy, 0.9);
                        }
                        else SetState(St.Idle, 0.8 + ctx.Rng.NextDouble());
                    }
                    break;
                case St.Dizzy:
                    if (stT >= stDur)
                    {
                        settling = true;
                        if (greetText != null) { SetState(St.Wave, 1.8); Say(greetText); greetText = null; }
                        else SetState(St.Idle, 1.2);
                        settling = false;
                    }
                    break;
                case St.Wave:
                case St.Happy:
                case St.Annoyed:
                    if (stT >= stDur) SetState(St.Idle, 0.8 + ctx.Rng.NextDouble() * 1.5);
                    break;
                default:   // Idle / Look / Sit
                    if (stT >= stDur) ChooseNext();
                    break;
            }
        }

        void Walk(double dt)
        {
            double speed = (running ? 36 : 13) * ps;
            if (!double.IsNaN(targetX)) facing = targetX >= fx ? 1 : -1;
            int r = MoveAlong(fx + facing * speed * dt);
            walkDist += speed * dt;
            if (r == 2)
            {
                if (ctx.Rng.NextDouble() < 0.3) Say(Pick(Lines.Jump));
                WalkOff(facing * speed);
                return;
            }
            if (r == facing)
            {
                if (platform != IntPtr.Zero && !wallHit)
                {
                    // 走到窗口边缘：掉头，或者干脆跳下去
                    if (edgeChoice == 0)
                        edgeChoice = (ctx.Settings.Follow && !double.IsNaN(targetX)) || ctx.Rng.NextDouble() < 0.3 ? 1 : -1;
                    if (edgeChoice < 0) { facing = -facing; edgeChoice = 0; }
                }
                else if (!double.IsNaN(targetX)) { targetX = double.NaN; SetState(St.Idle, 1 + ctx.Rng.NextDouble()); return; }
                else facing = -facing;
            }
            else edgeChoice = 0;

            if (!double.IsNaN(targetX) && Math.Abs(targetX - fx) < 4 * ps)
            {
                targetX = double.NaN;
                SetState(St.Idle, 1 + ctx.Rng.NextDouble() * 2);
                return;
            }
            if (stT >= stDur) ChooseNext();
        }

        void FollowCursor()
        {
            if (!(st == St.Idle || st == St.Look || st == St.Walk || (st == St.Sit && stT > 1.5))) return;
            double dx = cursor.X - fx, far = 80 * ui;
            if (st == St.Walk)
            {
                if (double.IsNaN(targetX) && Math.Abs(dx) <= far) return;
                targetX = cursor.X;
                running = Math.Abs(dx) > 300 * ui;
                return;
            }
            if (Math.Abs(dx) <= far || stT < 0.35) return;
            // 目标在屏幕边界外且已经贴边（地面或窗口上都一样）：原地望着就好
            double half = 12 * ps;
            bool stuckL = dx < 0 && fx <= area.Left + half + 1 && !ScreenAt(area.Left - half - 2, fy - 2);
            bool stuckR = dx > 0 && fx >= area.Right - half - 1 && !ScreenAt(area.Right + half + 2, fy - 2);
            if (stuckL || stuckR) { facing = dx > 0 ? 1 : -1; return; }
            SetState(St.Walk, 30);
            targetX = cursor.X;
            running = Math.Abs(dx) > 300 * ui;
            facing = dx > 0 ? 1 : -1;
        }

        void ChooseNext()
        {
            Random rng = ctx.Rng;
            targetX = double.NaN;
            PetForm friend = ctx.NearestPet(this, 260 * ui);
            if (friend != null && now - lastFriendHi > 30 && rng.NextDouble() < 0.35)
            {
                lastFriendHi = now;
                facing = friend.FeetX >= fx ? 1 : -1;
                SetState(St.Wave, 1.6);
                Say(Pick(Lines.Friend));
                return;
            }
            if (now - lastInteraction > 150 && rng.NextDouble() < 0.22) { EnterSleep(false); return; }
            if (ctx.Settings.WalkOnWindows && !ctx.Settings.Follow && rng.NextDouble() < 0.12 && TryJumpToWindow()) return;

            double r = rng.NextDouble() * 100;
            if (ctx.Settings.Follow)
            {
                if (r < 55) SetState(St.Idle, 2 + rng.NextDouble() * 3);
                else if (r < 72) SetState(St.Look, 2.2);
                else if (r < 84) SetState(St.Sit, 4 + rng.NextDouble() * 6);
                else if (r < 92) StartHop();
                else SetState(St.Wave, 1.4);
                return;
            }
            if (r < 24) SetState(St.Idle, 1.5 + rng.NextDouble() * 3.5);
            else if (r < 60) StartWalk(2 + rng.NextDouble() * 6);
            else if (r < 70) SetState(St.Look, 2.2);
            else if (r < 80) SetState(St.Sit, 5 + rng.NextDouble() * 10);
            else if (r < 87) StartHop();
            else if (r < 91) SetState(St.Wave, 1.5);
            else if (r < 97 && !ctx.Settings.Quiet) EnterThink(false);
            else EnterDance(false);
        }

        void StartWalk(double dur)
        {
            if (ctx.Rng.NextDouble() < 0.45) facing = -facing;
            SetState(St.Walk, dur);
            running = ctx.Rng.NextDouble() < 0.12;
            targetX = double.NaN;
        }

        void StartHop()
        {
            jumpVY = -Math.Sqrt(2 * Gravity * 13 * ps);
            jumpVX = ctx.Rng.NextDouble() < 0.5 ? 0 : facing * 70 * ui;
            SetState(St.Jump, 0);
            jumpPhase = 0;
        }

        void Launch()
        {
            jumpPhase = 1;
            vx = jumpVX;
            vy = jumpVY;
            platform = IntPtr.Zero;
        }

        bool TryJumpToWindow()
        {
            ctx.Scanner.Refresh(now);
            double tx;
            WinInfo w = ctx.Scanner.FindJumpTarget(fx, fy, 24 * ps, 34 * ps + 220 * ui, 420 * ui, 16 * ps, 38 * ps,
                platform, area, ctx.Rng, out tx);
            if (w == null) return false;
            double g = Gravity, rise = fy - w.R.Top, apex = rise + 16 * ps;
            double v0 = Math.Sqrt(2 * g * apex);
            double tUp = v0 / g, tDown = Math.Sqrt(2 * (apex - rise) / g);
            jumpVY = -v0;
            jumpVX = (tx - fx) / (tUp + tDown);
            facing = tx >= fx ? 1 : -1;
            SetState(St.Jump, 0);
            jumpPhase = 0;
            return true;
        }

        void EnterSleep(bool forced)
        {
            SetState(St.Sleep, 45 + ctx.Rng.NextDouble() * 90);
            nextFx = now + 0.8;
            if (forced || ctx.Rng.NextDouble() < 0.5) Say(Pick(Lines.Sleep), forced);
        }

        void Wake(bool forced)
        {
            if (st != St.Sleep) return;
            lastInteraction = now;
            SetState(St.Idle, 1.4);
            SpawnMark(PK.Bang);
            Say(Pick(Lines.Wake), forced);
        }

        /// <summary>笔记本放在哪边：优先朝着面对的方向，那边放不下（屏幕边缘/窗口边缘）就换另一边。</summary>
        int ChooseLaptopSide()
        {
            double left, right, need;
            if (platform != IntPtr.Zero) { left = fx - platRect.Left; right = platRect.Right - fx; need = 26 * ps; }
            else { left = fx - area.Left; right = area.Right - fx; need = 32 * ps; }
            int pref = facing >= 0 ? 1 : -1;
            if ((pref > 0 ? right : left) >= need) return pref;
            if ((pref > 0 ? left : right) >= need) return -pref;
            return right >= left ? 1 : -1;
        }

        /// <summary>结束敲键盘：先合上笔记本收好，再做后面的事。不在敲键盘时返回 false。</summary>
        bool StartPackUp(bool thenDone)
        {
            if (st != St.Think) return false;
            packThenDone = thenDone;
            SetState(St.PackUp, Typing.OutroSeconds);
            return true;
        }

        void EnterThink(bool forced)
        {
            thinkForced = forced;
            // 开场掏电脑约 1.1 秒，之后至少敲几秒键盘
            SetState(St.Think, 4.5 + ctx.Rng.NextDouble() * 3.5);
            if (ctx.Settings.Quiet && !forced) return;
            bubble = new Bubble();
            bubble.Text = Pick(Lines.Verbs) + "…";
            bubble.Life = stDur;
            bubble.Thinking = true;
        }

        void EnterDance(bool forced)
        {
            SetState(St.Dance, 4.5);
            nextFx = now;
            if (forced || ctx.Rng.NextDouble() < 0.5) Say(Pick(Lines.Dance), forced);
        }

        void EnterDizzy()
        {
            SetState(St.Dizzy, 2.4);
            for (int i = 0; i < 3; i++)
            {
                Particle q = new Particle();
                q.K = PK.Star; q.Rel = true; q.Phase = i * Math.PI * 2 / 3; q.Life = 2.4;
                parts.Add(q);
            }
            Say(Pick(Lines.Dizzy));
        }

        void UpdateChatter()
        {
            if (ctx.ClaudeFor(this) != null) return;
            if (now < nextChatter) return;
            nextChatter = now + 70 + ctx.Rng.NextDouble() * 150;
            if (ctx.Settings.Quiet || bubble != null) return;
            if (!(st == St.Idle || st == St.Walk || st == St.Sit || st == St.Look)) return;
            int h = DateTime.Now.Hour;
            if ((h >= 23 || h < 5) && ctx.Rng.NextDouble() < 0.3) { Say("夜深了，早点休息吧～"); return; }
            if (ctx.Rng.NextDouble() < 0.18) { EnterThink(false); return; }
            Say(Pick(Lines.Chatter));
            if (st != St.Walk && ctx.Rng.NextDouble() < 0.4) SetState(St.Wave, 1.2);
        }

        // ============================================================== 拖拽 & 点击
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Point c = Cursor.Position;
            lastInteraction = now;
            if (e.Button == MouseButtons.Left)
            {
                mouseDown = true;
                downPos = c;
                grabDX = fx - c.X;
                grabDY = fy - c.Y;
                Raise();   // 被别的置顶窗口挡着时，一碰就回到最上面
            }
            else if (e.Button == MouseButtons.Right) { mouseDown = false; ShowMenu(c); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!mouseDown || dragging) return;
            if ((Control.MouseButtons & MouseButtons.Left) == 0) { mouseDown = false; return; }
            Point c = Cursor.Position;
            Size ds = SystemInformation.DragSize;
            if (Math.Abs(c.X - downPos.X) > ds.Width || Math.Abs(c.Y - downPos.Y) > ds.Height) StartDrag();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (dragging) EndDrag();
            else if (mouseDown) { mouseDown = false; OnClicked(); }
            mouseDown = false;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            // WinForms 在 OnMouseUp 之后才释放捕获，所以这里清 mouseDown 不会吞掉正常的单击
            if (!Capture)
            {
                if (dragging) EndDrag(); else mouseDown = false;
            }
        }

        void OnClicked()
        {
            lastInteraction = now;
            if (st == St.Sleep) { Wake(false); return; }
            if (!Grounded) return;
            clickCombo = now - lastClick < 0.7 ? clickCombo + 1 : 1;
            lastClick = now;
            if (clickCombo >= 4)
            {
                clickCombo = 0;
                SetState(St.Annoyed, 1.4);
                SpawnSweat();
                Say(Pick(Lines.Poke));
                return;
            }
            SetState(St.Happy, 1.1);
            SpawnHearts(clickCombo == 1 ? 2 : 1);
            if (clickCombo == 1 && ctx.Rng.NextDouble() < 0.6) Say(Pick(Lines.Click));
        }

        void StartDrag()
        {
            bool wasSleeping = st == St.Sleep;
            dragging = true;
            platform = IntPtr.Zero;
            dizzyOnLand = false;
            SetState(St.Drag, 0);
            dragHist.Clear();
            dvx = dvy = 0;
            shakeCount = shakeSign = 0;
            lastInteraction = now;
            if (wasSleeping) { SpawnMark(PK.Bang); Say(Pick(Lines.Wake)); }
            else if (ctx.Rng.NextDouble() < 0.45) Say(Pick(Lines.Drag));
        }

        void UpdateDrag()
        {
            if ((Control.MouseButtons & MouseButtons.Left) == 0) { EndDrag(); return; }
            Point c = Cursor.Position;
            double nx = c.X + grabDX, ny = c.Y + grabDY;
            dragHist.Add(new double[] { now, nx, ny });
            while (dragHist.Count > 2 && now - dragHist[0][0] > 0.1) dragHist.RemoveAt(0);
            double[] o = dragHist[0];
            double span = now - o[0];
            if (span > 0.01) { dvx = (nx - o[1]) / span; dvy = (ny - o[2]) / span; }
            // 来回甩 → 甩晕
            int sign = dvx > 1100 * ui ? 1 : dvx < -1100 * ui ? -1 : 0;
            if (sign != 0 && sign != shakeSign)
            {
                if (shakeSign != 0) { shakeCount++; lastShake = now; }
                shakeSign = sign;
            }
            if (now - lastShake > 0.9) shakeCount = 0;
            fx = nx;
            fy = ny;
        }

        void EndDrag()
        {
            if (!dragging) return;
            dragging = false;
            mouseDown = false;
            double lim = 4200 * ui;
            vx = Math.Max(-lim, Math.Min(lim, dvx));
            vy = Math.Max(-lim, Math.Min(lim, dvy));
            if (shakeCount >= 4) dizzyOnLand = true;
            shakeCount = 0;
            thrown = Math.Sqrt(vx * vx + vy * vy) > 800 * ui;
            UpdateScreen();
            if (fy > Floor) fy = Floor;
            StartFall();
        }

        void ShowMenu(Point c)
        {
            if (dragging) return;
            ctx.BuildMenu(menu, this);
            PetContext.FitMenuFont(menu, c);
            ctx.NoteMenuOpening();               // 记下用户原来在用的窗口，菜单关掉后把焦点还回去
            Native.SetForegroundWindow(Handle);   // 否则点别处时菜单不会自动关闭
            menu.Show(c);
        }

        void OnMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            ctx.NoteMenuClosed(e.CloseReason == ToolStripDropDownCloseReason.AppFocusChange);
        }

        // ============================================================== 菜单命令（由 PetContext 调用）
        public void CmdPet()
        {
            if (st == St.Sleep) { Wake(true); return; }
            if (!Grounded) return;
            lastInteraction = now;
            SetState(St.Happy, 1.6);
            SpawnHearts(3);
            Say(Pick(Lines.Pet), true);
        }

        public void CmdSay()
        {
            lastInteraction = now;
            if (st == St.Sleep) Wake(true); else Say(Pick(Lines.Chatter), true);
        }

        public void CmdJump()
        {
            if (!Grounded) return;
            lastInteraction = now;
            if (st == St.Sleep) Wake(true);
            StartHop();
        }

        public void CmdDance() { if (Grounded) { lastInteraction = now; EnterDance(true); } }
        public void CmdThink() { if (Grounded) { lastInteraction = now; EnterThink(true); } }

        public void CmdSleep()
        {
            if (st == St.Sleep) Wake(true);
            else if (Grounded) EnterSleep(true);
        }

        public void ClearBubble() { if (!leaving) bubble = null; }

        public void SayGoodbye()
        {
            if (leaving) return;
            leaving = true;
            leaveAt = now + 1.3;
            if (Grounded) SetState(St.Wave, 1.3);
            Say("拜拜～", true);
        }

        public void Recall(Rectangle wa)
        {
            if (dragging) return;
            fx = wa.Left + wa.Width * (0.25 + 0.5 * ctx.Rng.NextDouble());
            fy = wa.Top + Math.Max(30 * ps, wa.Height * 0.15);
            vx = vy = 0;
            StartFall();
            UpdateScreen();
            greetText = Pick(Lines.Recall);
        }

        /// <summary>脚不在任何屏幕上就叫回来。periodic=true 时只管已经站稳的（空中/刚扔出去时可能短暂出界）。</summary>
        public void EnsureOnScreen(bool periodic)
        {
            if (periodic && (!Grounded || dragging || hidden || leaving)) return;
            Point p = new Point((int)Math.Round(fx), (int)Math.Round(fy) - 2);
            foreach (Screen s in Screen.AllScreens)
                if (s.Bounds.Contains(p)) return;
            Recall(Screen.PrimaryScreen.WorkingArea);
        }

        public void Remind(int minutes)
        {
            if (Grounded)
            {
                if (st == St.Sleep) SetState(St.Idle, 1);
                SetState(St.Wave, 2.2);
            }
            Say(string.Format(Pick(Lines.Remind), minutes), true);
            if (bubble != null) bubble.Life = 6;
        }

        void Raise()
        {
            if (hidden || !IsHandleCreated) return;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// <summary>
        /// 定期调用：只有被“普通的”置顶窗口盖住时才重新置顶。上面若是菜单、提示框、
        /// 输入法候选框、任务栏这类临时/工具窗口，就让着它们，不去抢。
        /// </summary>
        public void KeepOnTop()
        {
            if (hidden || !IsHandleCreated) return;
            Native.RECT me;
            if (!Native.GetWindowRect(Handle, out me)) return;
            Rectangle mine = Rectangle.FromLTRB(me.Left, me.Top, me.Right, me.Bottom);
            IntPtr h = Native.GetWindow(Handle, Native.GW_HWNDPREV);
            for (int guard = 0; h != IntPtr.Zero && guard < 256; guard++, h = Native.GetWindow(h, Native.GW_HWNDPREV))
            {
                if (!Native.IsWindowVisible(h)) continue;
                Rectangle r;
                if (!WindowScanner.FrameRect(h, out r) || !r.IntersectsWith(mine)) continue;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == ctx.Pid) continue;
                int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_TRANSPARENT) != 0 || WindowScanner.IsCloaked(h)) continue;
                string cls = WindowScanner.ClassOf(h);
                bool transient = cls == "#32768" || cls.StartsWith("tooltips_class", StringComparison.OrdinalIgnoreCase)
                    || (ex & Native.WS_EX_TOOLWINDOW) != 0 || Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero;
                if (!transient) Raise();
                return;
            }
        }

        // ============================================================== 粒子特效
        void SpawnHearts(int n)
        {
            Random rng = ctx.Rng;
            for (int i = 0; i < n; i++)
            {
                Particle q = new Particle();
                q.K = PK.Heart;
                q.X = fx + (rng.NextDouble() - 0.5) * 20 * ps;
                q.Y = fy - (21 + rng.NextDouble() * 4) * ps;
                q.VX = (rng.NextDouble() - 0.5) * 40 * ui;
                q.VY = -(50 + rng.NextDouble() * 30) * ui;
                q.Life = 1.2 + rng.NextDouble() * 0.4;
                q.Phase = rng.NextDouble() * 6;
                parts.Add(q);
            }
        }

        void SpawnZ()
        {
            Particle q = new Particle();
            q.K = PK.Zed;
            q.X = fx + 9 * ps;
            q.Y = fy - 17 * ps;
            q.VX = 12 * ui;
            q.VY = -24 * ui;
            q.Life = 2.4;
            q.Phase = ctx.Rng.NextDouble() * 6;
            parts.Add(q);
        }

        void SpawnNote()
        {
            int side = ctx.Rng.Next(2) == 0 ? -1 : 1;
            Particle q = new Particle();
            q.K = PK.Note;
            q.X = fx + side * 15 * ps - 2 * ps;
            q.Y = fy - 21 * ps;
            q.VX = side * 18 * ui;
            q.VY = -40 * ui;
            q.Life = 1.3;
            q.Phase = ctx.Rng.NextDouble() * 6;
            parts.Add(q);
        }

        void SpawnDust(int n)
        {
            Random rng = ctx.Rng;
            for (int i = 0; i < n; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                Particle q = new Particle();
                q.K = PK.Dust;
                q.X = fx + side * (6 + rng.NextDouble() * 6) * ps;
                q.Y = fy - ps;
                q.VX = side * (60 + rng.NextDouble() * 90) * ui;
                q.VY = -(10 + rng.NextDouble() * 30) * ui;
                q.Life = 0.35 + rng.NextDouble() * 0.15;
                parts.Add(q);
            }
        }

        void SpawnMark(PK k)
        {
            parts.RemoveAll(delegate(Particle p) { return p.K == k; });
            Particle q = new Particle();
            q.K = k;
            q.Rel = true;
            q.X = 9 * ps;
            q.Y = -28 * ps;
            q.Life = 0.9;
            parts.Add(q);
        }

        void SpawnSweat()
        {
            Particle q = new Particle();
            q.K = PK.Sweat;
            q.Rel = true;
            q.X = 13 * ps;
            q.Y = -22 * ps;
            q.VY = 14 * ui;
            q.Life = 1.0;
            parts.Add(q);
        }

        void UpdateParticles(double dt)
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                Particle q = parts[i];
                q.Age += dt;
                if (q.Age >= q.Life || (q.K == PK.Star && st != St.Dizzy)) { parts.RemoveAt(i); continue; }
                if (q.K == PK.Star)
                {
                    double a = q.Phase + q.Age * 5;
                    q.X = Math.Cos(a) * 13 * ps - 2 * ps;
                    q.Y = -26 * ps + Math.Sin(a) * 2.5 * ps;
                    continue;
                }
                q.X += q.VX * dt;
                q.Y += q.VY * dt;
                if (q.K == PK.Heart || q.K == PK.Note || q.K == PK.Zed) q.X += Math.Sin(q.Age * 5 + q.Phase) * 12 * ui * dt;
                if (q.K == PK.Dust) q.VX *= Math.Exp(-6 * dt);
            }
        }

        static Color A(int a, int r, int g, int b) { return Color.FromArgb(a, r, g, b); }

        void DrawParticle(Graphics g, Particle q, int ix, int iy)
        {
            float x = (float)Math.Round(q.Rel ? ix + q.X : q.X), y = (float)Math.Round(q.Rel ? iy + q.Y : q.Y);
            double fade = q.K == PK.Dust ? 1 - q.Age / q.Life : Math.Min(1, (q.Life - q.Age) / (q.Life * 0.35));
            int a = (int)(255 * Math.Max(0, Math.Min(1, fade)));
            if (a <= 0) return;
            int big = Math.Max(1, ps * 2 / 3), small = Math.Max(1, (ps + 1) / 2);
            switch (q.K)
            {
                case PK.Heart: Art.Glyph(g, Art.HeartPat, x, y, big, A(a, 255, 92, 110), A(a, 92, 26, 38)); break;
                case PK.Zed:
                    Art.Glyph(g, Art.ZedPat, x, y, Math.Max(1, (int)Math.Round(ps * (0.45 + 0.45 * q.Age / q.Life))),
                        A(a, 245, 245, 245), A(a, 60, 60, 66));
                    break;
                case PK.Note: Art.Glyph(g, Art.NotePat, x, y, big, A(a, 190, 168, 255), A(a, 52, 38, 110)); break;
                case PK.Star: Art.Glyph(g, Art.StarPat, x, y, small, A(a, 255, 222, 92), A(a, 110, 82, 0)); break;
                case PK.Sweat: Art.Glyph(g, Art.DropPat, x, y, small, A(a, 140, 210, 255), A(a, 30, 80, 120)); break;
                case PK.Bang: Art.Glyph(g, Art.BangPat, x, y, big, A(a, 255, 210, 63), A(a, 100, 60, 0)); break;
                case PK.Dust:
                    using (SolidBrush b = new SolidBrush(A(a * 3 / 4, 205, 196, 186))) g.FillRectangle(b, x, y, ps, ps);
                    break;
            }
        }

        Rectangle ParticleBounds(Particle q, int ix, int iy)
        {
            int x = (int)Math.Round(q.Rel ? ix + q.X : q.X), y = (int)Math.Round(q.Rel ? iy + q.Y : q.Y);
            return new Rectangle(x - 2 * ps, y - 2 * ps, 12 * ps, 12 * ps);
        }

        // ============================================================== 姿势
        Pose BuildPose()
        {
            Pose p = new Pose();
            double t = stT;
            bool breathe = false;
            switch (st)
            {
                case St.Idle:
                    breathe = true;
                    break;
                case St.Look:
                    {
                        int k = (int)(t / 0.55) % 4;
                        p.EyeDX = k == 0 ? -1 : k == 2 ? 1 : 0;
                        if (k == 3) p.EyeDY = -1;
                        breathe = true;
                        break;
                    }
                case St.Walk:
                    {
                        int ph = (int)(walkDist / ((running ? 4 : 3) * ps)) % 4;
                        if (ph == 1) { p.Lift[0] = 2; p.Lift[2] = 2; }
                        else if (ph == 3) { p.Lift[1] = 2; p.Lift[3] = 2; }
                        p.EyeDX = facing;
                        if (running) p.ArmL = p.ArmR = ArmStyle.Out;
                        break;
                    }
                case St.Sit:
                    p.LegLen = 1;
                    p.ArmL = p.ArmR = ArmStyle.Down;
                    breathe = true;
                    break;
                case St.Sleep:
                    p.LegLen = 0;
                    p.BodyW = 26;
                    p.BodyH = Math.Sin(now * 1.7 + seed) > 0 ? 14 : 13;
                    p.ArmL = p.ArmR = ArmStyle.Hidden;
                    p.Eyes = EyeStyle.Closed;
                    break;
                case St.Land:
                    p.BodyW = 28;
                    p.BodyH = 12;
                    p.LegLen = 2;
                    p.ArmL = p.ArmR = ArmStyle.Out;
                    p.Eyes = EyeStyle.Squint;
                    break;
                case St.Jump:
                    if (jumpPhase == 0) { p.BodyW = 26; p.BodyH = 14; p.LegLen = 2; }
                    else if (vy < 0) { p.BodyW = 22; p.BodyH = 18; p.ArmL = p.ArmR = ArmStyle.Up; p.Lift[0] = p.Lift[3] = 1; }
                    else p.ArmL = p.ArmR = ArmStyle.Up;
                    break;
                case St.Fall:
                    {
                        bool flap = ((int)(now * 9)) % 2 == 0;
                        p.ArmL = flap ? ArmStyle.Up : ArmStyle.Out;
                        p.ArmR = flap ? ArmStyle.Out : ArmStyle.Up;
                        p.LegLen = 5;
                        p.Eyes = vy > 900 * ui ? EyeStyle.Wide : EyeStyle.Open;
                        break;
                    }
                case St.Drag:
                    {
                        p.ArmL = p.ArmR = ArmStyle.Up;
                        p.LegLen = 6;
                        p.LegDX = (int)Math.Max(-2, Math.Min(2, Math.Round(-dvx / (700 * ui))));
                        double sp = Math.Sqrt(dvx * dvx + dvy * dvy);
                        if (shakeCount >= 3) p.Eyes = EyeStyle.Dizzy;
                        else if (sp > 1500 * ui) p.Eyes = EyeStyle.Squint;
                        else p.EyeDY = 1;   // 往下看，有点紧张
                        break;
                    }
                case St.Wave:
                    {
                        bool up = ((int)(t / 0.2)) % 2 == 0;
                        if (facing > 0) p.ArmR = up ? ArmStyle.Up : ArmStyle.Normal;
                        else p.ArmL = up ? ArmStyle.Up : ArmStyle.Normal;
                        p.Eyes = EyeStyle.Happy;
                        break;
                    }
                case St.Happy:
                    {
                        p.Eyes = EyeStyle.Happy;
                        p.Blush = true;
                        if (((int)(t / 0.16)) % 2 == 1) { p.LegLen = 3; p.BodyW = 26; p.BodyH = 15; }
                        else p.ArmL = p.ArmR = ArmStyle.Up;
                        break;
                    }
                case St.Dizzy:
                    p.Eyes = EyeStyle.Dizzy;
                    p.ArmL = p.ArmR = ArmStyle.Down;
                    p.OffX = (int)Math.Round(Math.Sin(t * 6) * 1.4);
                    break;
                case St.Think:
                    p.Frame = Typing.ThinkFrame(t);   // 掏出笔记本 → 敲键盘循环
                    p.Mirror = laptopSide < 0;
                    break;
                case St.PackUp:
                    p.Frame = Typing.OutroFrame(t);   // 合上笔记本收好
                    p.Mirror = laptopSide < 0;
                    break;
                case St.Dance:
                    {
                        int beat = (int)(t / 0.28);
                        bool b = beat % 2 == 0;
                        p.ArmL = b ? ArmStyle.Up : ArmStyle.Normal;
                        p.ArmR = b ? ArmStyle.Normal : ArmStyle.Up;
                        if (!b) { p.LegLen = 3; p.BodyW = 26; p.BodyH = 15; }
                        p.OffX = beat % 4 < 2 ? -1 : 1;
                        p.Eyes = (beat / 4) % 2 == 0 ? EyeStyle.Happy : EyeStyle.Open;
                        break;
                    }
                case St.Annoyed:
                    p.Eyes = EyeStyle.Squint;
                    p.ArmL = p.ArmR = ArmStyle.Down;
                    break;
            }
            if (breathe && Math.Sin(now * 2.3 + seed) > 0.55) p.BodyH -= 1;

            // 眼睛跟着鼠标转
            if ((st == St.Idle || st == St.Sit || st == St.Wave) && p.Eyes == EyeStyle.Open)
            {
                double dx = cursor.X - fx, dy = cursor.Y - (fy - 14 * ps), lim = 600 * ui;
                if (dx * dx + dy * dy < lim * lim)
                {
                    p.EyeDX = dx > 24 * ui ? 1 : dx < -24 * ui ? -1 : 0;
                    p.EyeDY = dy < -50 * ui ? -1 : dy > 60 * ui ? 1 : 0;
                }
            }
            if ((st == St.Idle || st == St.Sit) && now - lastInteraction > 100 && p.Eyes == EyeStyle.Open) p.Eyes = EyeStyle.Sleepy;
            if (now < blinkEnd && (p.Eyes == EyeStyle.Open || p.Eyes == EyeStyle.Wide || p.Eyes == EyeStyle.Sleepy)) p.Eyes = EyeStyle.Closed;
            return p;
        }

        // ============================================================== 渲染
        void Render()
        {
            Pose pose = BuildPose();
            int ix = (int)Math.Round(fx), iy = (int)Math.Round(fy);
            Rectangle box = new Rectangle(ix + Art.BoxLeft * ps, iy + Art.BoxTop * ps, Art.BoxW * ps, Art.BoxH * ps);
            if (pose.Frame >= 0)
            {
                // 笔记本会伸到身体外面：窗口要包住整帧
                Rectangle tb = Typing.AllBounds(pose.Mirror);
                box = Rectangle.Union(box, new Rectangle(ix + tb.X * ps, iy + tb.Y * ps, tb.Width * ps, tb.Height * ps));
            }
            foreach (Particle q in parts) box = Rectangle.Union(box, ParticleBounds(q, ix, iy));

            bubbleLayout = null;
            string bubbleSig = "";
            Bubble visibleBubble = bubble;
            ClaudeActivity activity = ctx.ClaudeFor(this);
            if (activity != null && !ctx.Settings.Quiet)
                visibleBubble = new Bubble { Text = activity.Text, Thinking = activity.Thinking, Age = 1, Life = 100 };
            if (visibleBubble != null)
            {
                string glyph = visibleBubble.Thinking ? Spinner[(int)(now / 0.12) % Spinner.Length] : null;
                // 气泡锚点 = 头顶；按实际姿势取高度（不低于 21，免得呼吸时气泡上下抖），
                // 头顶有“！”/星星时再抬高一截
                int headRows = Math.Max(visibleBubble.High ? 30 : 21, pose.LegLen + pose.BodyH + 1);
                bubbleLayout = BubbleArt.Layout(visibleBubble.Text, glyph, (float)ui, ix, iy - headRows * ps, iy, mon);
                bubbleLayout.Alpha = visibleBubble.Alpha;
                box = Rectangle.Union(box, bubbleLayout.Bounds);
                Rectangle bb = bubbleLayout.Bounds;
                bubbleSig = visibleBubble.Text + "|" + glyph + "|" + (int)(bubbleLayout.Alpha * 40) + "|" + (bb.X - ix) + ","
                    + (bb.Y - iy) + "," + bb.Width + "," + bb.Height + "," + ((int)bubbleLayout.TailX - ix);
            }
            box.Inflate(2, 2);
            bool shadow = Grounded;
            string sig = pose.Key() + "|" + ps + "|" + (box.X - ix) + "," + (box.Y - iy) + "," + box.Width + "," + box.Height
                + "|" + (shadow ? 1 : 0) + "|" + bubbleSig;

            if (parts.Count == 0 && sig == lastSig)
            {
                // 画面没变，只是位置变了：移动窗口即可
                if (box.Location != lastLoc)
                {
                    Native.SetWindowPos(Handle, IntPtr.Zero, box.X, box.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    lastLoc = box.Location;
                }
                return;
            }

            surf.Ensure(box.Width, box.Height);
            Graphics g = surf.G;
            g.ResetTransform();
            g.Clear(Color.Transparent);
            g.TranslateTransform(-box.X, -box.Y);
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;

            if (shadow)
            {
                // 只画在脚底线以上：脚下的任务栏/标题栏像素保持透明，不挡点击
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float sw = (pose.BodyW + 4) * ps, sx = ix + pose.OffX * ps - sw / 2;
                g.SetClip(RectangleF.FromLTRB(sx - 1, iy - 3 * ps, sx + sw + 1, iy));
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                    g.FillEllipse(sb, sx, iy - 2f * ps, sw, 3.5f * ps);
                g.ResetClip();
                g.SmoothingMode = SmoothingMode.None;
            }
            Art.DrawSprite(g, ix, iy, ps, pose);
            if (bubbleLayout != null) BubbleArt.Draw(g, bubbleLayout);
            foreach (Particle q in parts) DrawParticle(g, q, ix, iy);   // 爱心等飘在气泡前面
            g.Flush(FlushIntention.Sync);

            Present(box);
            lastSig = sig;
            lastLoc = box.Location;
        }

        void Present(Rectangle box)
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            try
            {
                Native.POINT dst = new Native.POINT(box.X, box.Y), src = new Native.POINT(0, 0);
                Native.SIZE size = new Native.SIZE(box.Width, box.Height);
                Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
                bf.BlendOp = 0;            // AC_SRC_OVER
                bf.SourceConstantAlpha = 255;
                bf.AlphaFormat = 1;        // AC_SRC_ALPHA（预乘 alpha）
                bool ok = Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, surf.DC, ref src, 0, ref bf, Native.ULW_ALPHA);
                if (DumpDir != null) Dump(box, ok ? 0 : Marshal.GetLastWin32Error());
            }
            finally { Native.ReleaseDC(IntPtr.Zero, screen); }
        }

        // 调试：设置环境变量 CLAWD_DUMP=目录 后，每 0.5 秒把送给 UpdateLayeredWindow 的画面存成 PNG
        static readonly string DumpDir = Environment.GetEnvironmentVariable("CLAWD_DUMP");
        double lastDump = -1;
        int dumpCount;

        void Dump(Rectangle box, int err)
        {
            if (dumpCount >= 40 || (lastDump >= 0 && now - lastDump < 0.5)) return;
            lastDump = now;
            try
            {
                using (Bitmap crop = surf.Bmp.Clone(new Rectangle(0, 0, box.Width, box.Height), PixelFormat.Format32bppArgb))
                    crop.Save(Path.Combine(DumpDir, "dump_" + dumpCount.ToString("00") + ".png"), ImageFormat.Png);
                File.AppendAllText(Path.Combine(DumpDir, "dump.log"), string.Format("{0:00} t={1:0.00} st={2} box={3} ulwErr={4}{5}",
                    dumpCount, now, st, box, err, Environment.NewLine));
            }
            catch (Exception ex) { Log.Error(ex); }
            dumpCount++;
        }
    }

    // ------------------------------------------------------------------ 程序上下文：托盘、计时器、多只宠物
    class PetContext : ApplicationContext
    {
        public const string SummonEventName = "Local\\ClawdPet.Summon";
        public const int MaxPets = 8;
        public static readonly Font MenuFont = MakeMenuFont();

        public readonly PetSettings Settings = PetSettings.Load();
        public readonly List<PetForm> Pets = new List<PetForm>();
        public readonly WindowScanner Scanner = new WindowScanner();
        public readonly Random Rng = new Random();
        public readonly ClaudeLink Claude = new ClaudeLink();
        public readonly uint Pid = (uint)Process.GetCurrentProcess().Id;
        const double RemindEvery = 45 * 60;      // 连续使用多久提醒一次（秒）
        const double BreakIdle = 5 * 60;         // 离开键盘鼠标这么久算休息过了

        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        readonly List<Action> posted = new List<Action>();
        readonly List<Rectangle> fullscreen = new List<Rectangle>();
        readonly SessionSwitchEventHandler onSession;
        EventWaitHandle summon;
        IntPtr trayHicon;
        double last, lastSlow, activeSec, nextRemind = RemindEvery;
        double lastClaude = -1;
        DateTime lastWall = DateTime.MinValue;
        int slowCount;
        bool exiting, locked;

        public double Now { get { return clock.Elapsed.TotalSeconds; } }

        public ClaudeActivity ClaudeFor(PetForm pet)
        {
            if (!Settings.ClaudeEnabled || Claude.Current == null) return null;
            return Pets.Find(delegate(PetForm p) { return !p.IsDisposed && !p.IsLeaving; }) == pet ? Claude.Current : null;
        }

        public PetContext()
        {
            try { summon = new EventWaitHandle(false, EventResetMode.AutoReset, SummonEventName); }
            catch (Exception ex) { Log.Error(ex); }

            int s = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (Bitmap b = IconMaker.Render(s)) trayHicon = b.GetHicon();
            tray.Icon = Icon.FromHandle(trayHicon);
            tray.Text = "Clawd 桌宠";
            trayMenu.Font = MenuFont;
            trayMenu.Opening += delegate(object o, CancelEventArgs e)
            {
                BuildMenu(trayMenu, ActiveCount() > 0 ? Pets.Find(delegate(PetForm p) { return !p.IsLeaving; }) : null);
                FitMenuFont(trayMenu, Cursor.Position);
                e.Cancel = false;
            };
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += delegate { Post(RecallAll); };
            tray.Visible = true;

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            onSession = delegate(object o, SessionSwitchEventArgs e)
            {
                // 锁屏时不需要 60 帧地画给没人看
                if (e.Reason == SessionSwitchReason.SessionLock) { locked = true; timer.Interval = 250; }
                else if (e.Reason == SessionSwitchReason.SessionUnlock) { locked = false; timer.Interval = 15; }
                if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionUnlock)
                {
                    activeSec = 0;   // 锁屏 = 离开了一会儿，休息提醒重新计时
                    nextRemind = RemindEvery;
                }
            };
            SystemEvents.SessionSwitch += onSession;
            Spawn(true);
            timer.Interval = 15;
            timer.Tick += OnTick;
            timer.Start();
        }

        static Font MakeMenuFont()
        {
            try
            {
                Font f = new Font("Microsoft YaHei UI", 9f);
                if (string.Equals(f.FontFamily.GetName(0x409), "Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
            }
            catch (Exception) { }
            return SystemFonts.MenuFont;
        }

        static float sysDpi;
        static readonly Dictionary<int, Font> menuFonts = new Dictionary<int, Font>();

        /// <summary>WinForms 按系统 DPI 换算菜单字号；在缩放比例不同的显示器上按该显示器 DPI 重新设置。</summary>
        public static void FitMenuFont(ContextMenuStrip m, Point at)
        {
            try
            {
                if (sysDpi <= 0) using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sysDpi = g.DpiY;
                int dpi = (int)Math.Round(Dpi.ForPoint(at));
                Font f;
                if (!menuFonts.TryGetValue(dpi, out f))
                {
                    f = Math.Abs(dpi - sysDpi) < 1 ? MenuFont : new Font(MenuFont.FontFamily, MenuFont.SizeInPoints * dpi / sysDpi);
                    menuFonts[dpi] = f;
                }
                if (m.Font != f) m.Font = f;
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        // ---- 右键菜单的焦点归还
        IntPtr returnFocusTo;   // 打开菜单前用户在用的（别的程序的）窗口
        int menuSeq;

        bool AnyMenuOpen { get { return trayMenu.Visible || Pets.Exists(delegate(PetForm p) { return p.MenuOpen; }); } }

        bool IsOurs(IntPtr h)
        {
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            return pid == Pid;
        }

        public void NoteMenuOpening()
        {
            menuSeq++;   // 作废之前还没执行的归还
            IntPtr fg = Native.GetForegroundWindow();
            // 前台已经是我们自己（比如在一个菜单开着时又右键）：沿用之前记下的窗口
            if (fg != IntPtr.Zero && !IsOurs(fg)) returnFocusTo = fg;
        }

        public void NoteMenuClosed(bool userSwitchedApp)
        {
            if (userSwitchedApp) { returnFocusTo = IntPtr.Zero; return; }   // 用户点了别的程序：焦点已经走了
            int seq = menuSeq;
            Post(delegate
            {
                if (seq != menuSeq || AnyMenuOpen) return;   // 期间又打开了菜单
                IntPtr prev = returnFocusTo;
                returnFocusTo = IntPtr.Zero;
                IntPtr fg = Native.GetForegroundWindow();
                if (prev != IntPtr.Zero && Native.IsWindow(prev) && (fg == IntPtr.Zero || IsOurs(fg))) Native.SetForegroundWindow(prev);
            });
        }

        public int ActiveCount()
        {
            int n = 0;
            foreach (PetForm p in Pets) if (!p.IsDisposed && !p.IsLeaving) n++;
            return n;
        }

        public bool IsFullscreenAt(Point p)
        {
            foreach (Rectangle r in fullscreen) if (r.Contains(p)) return true;
            return false;
        }

        /// <summary>推迟到下一帧执行（避免在菜单点击回调里销毁菜单本身）。</summary>
        public void Post(Action a) { posted.Add(a); }

        void OnTick(object sender, EventArgs e)
        {
            if (exiting) return;
            if (posted.Count > 0)
            {
                Action[] acts = posted.ToArray();
                posted.Clear();
                foreach (Action a in acts)
                {
                    try { a(); } catch (Exception ex) { Log.Error(ex); }
                    if (exiting) return;
                }
            }
            double now = Now, dt = Math.Max(0, Math.Min(0.05, now - last));
            last = now;
            if (now - lastClaude >= 0.3)
            {
                lastClaude = now;
                Claude.Poll();
                Claude.Publish(Settings.ClaudeEnabled);
                tray.Text = Settings.ClaudeEnabled ? Claude.StatusText : "Clawd 桌宠 · AI 联动已暂停";
            }
            if (now - lastSlow >= 1) { double sdt = Math.Min(5, now - lastSlow); lastSlow = now; SlowTick(now, sdt); }
            if (locked) return;
            foreach (PetForm p in Pets.ToArray())
            {
                if (p.IsDisposed) continue;
                try { p.Tick(dt, now); } catch (Exception ex) { Log.Error(ex); }
            }
        }

        void SlowTick(double now, double dt)
        {
            UpdateFullscreen();
            if (summon != null && summon.WaitOne(0)) Spawn(false);
            bool anyMenu = trayMenu.Visible || Pets.Exists(delegate(PetForm p) { return p.MenuOpen; });
            if (++slowCount % 3 == 0 && !anyMenu && !locked && !Native.ForeignMenuOpen())
                foreach (PetForm p in Pets) p.KeepOnTop();
            if (slowCount % 5 == 0 && !locked) foreach (PetForm p in Pets) p.EnsureOnScreen(true);
            UpdateReminder(dt);
        }

        /// <summary>
        /// 休息提醒：累计“连续使用”时间（键盘鼠标有动静才算），离开超过 5 分钟（包括锁屏、
        /// 睡眠）就清零重新计。提醒交给一只正在显示的 Clawd，都隐藏着就等它出现。
        /// </summary>
        void UpdateReminder(double dt)
        {
            // 用墙上时钟检查两次之间是否隔了很久（电脑睡眠过）；锁屏期间不累计
            DateTime wall = DateTime.UtcNow;
            bool slept = lastWall != DateTime.MinValue && (wall - lastWall).TotalSeconds >= BreakIdle;
            lastWall = wall;
            if (locked) return;
            if (slept || Native.IdleSeconds() >= BreakIdle) { activeSec = 0; nextRemind = RemindEvery; return; }
            activeSec += dt;
            if (!Settings.Reminders) { nextRemind = activeSec + RemindEvery; return; }
            if (activeSec < nextRemind) return;
            PetForm t = Pets.Find(delegate(PetForm p) { return p.CanRemind; });
            if (t == null) return;
            nextRemind = activeSec + RemindEvery;
            t.Remind((int)Math.Round(activeSec / 60));
        }

        /// <summary>
        /// 逐个显示器判断是否被全屏程序（游戏、视频、演示）占满：看屏幕中心最上层的是哪个窗口。
        /// 不依赖前台窗口，所以副屏上的全屏视频、或者点了托盘菜单时都能正确隐藏。
        /// </summary>
        void UpdateFullscreen()
        {
            List<Rectangle> res = new List<Rectangle>();
            if (Settings.HideFullscreen)
                foreach (Screen s in Screen.AllScreens)
                {
                    Rectangle b = s.Bounds;
                    IntPtr h = Native.GetAncestor(Native.WindowFromPoint(new Native.POINT(b.Left + b.Width / 2, b.Top + b.Height / 2)), Native.GA_ROOT);
                    uint pid = 0;
                    if (h != IntPtr.Zero) Native.GetWindowThreadProcessId(h, out pid);
                    if (h == IntPtr.Zero || pid == Pid) { if (fullscreen.Contains(b)) res.Add(b); continue; }   // 我们自己的窗口在上面：保持原状态
                    string cls = WindowScanner.ClassOf(h);
                    if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") continue;
                    if ((Native.GetWindowLong(h, Native.GWL_STYLE) & Native.WS_CAPTION) == Native.WS_CAPTION) continue;
                    // 任务栏自动隐藏时，普通最大化窗口也会铺满整屏：这种情况不算全屏
                    if (Native.IsZoomed(h) && s.WorkingArea == b) continue;
                    Rectangle r;
                    if (WindowScanner.FrameRect(h, out r) && r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom)
                        res.Add(b);
                }
            fullscreen.Clear();
            fullscreen.AddRange(res);
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            Post(delegate { foreach (PetForm p in Pets) p.EnsureOnScreen(false); });
        }

        public void Spawn(bool first)
        {
            if (exiting || ActiveCount() >= MaxPets) return;
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            double x = wa.Left + wa.Width * (0.2 + 0.6 * Rng.NextDouble());
            double y = wa.Top + wa.Height * 0.15;
            PetForm p = new PetForm(this, x, y, first ? Lines.Greeting() : Lines.Summon[Rng.Next(Lines.Summon.Length)]);
            p.FormClosed += delegate { OnPetClosed(p); };
            Pets.Add(p);
            p.Show();
        }

        public void RecallAll()
        {
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            foreach (PetForm p in Pets) p.Recall(wa);
        }

        public PetForm NearestPet(PetForm me, double maxDist)
        {
            PetForm best = null;
            double bd = maxDist;
            foreach (PetForm p in Pets)
            {
                if (p == me || p.IsDisposed) continue;
                double d = Math.Abs(p.FeetX - me.FeetX) + Math.Abs(p.FeetY - me.FeetY) * 2;
                if (d < bd) { bd = d; best = p; }
            }
            return best;
        }

        void OnPetClosed(PetForm p)
        {
            Pets.Remove(p);
            if (Pets.Count == 0 && !exiting) ExitAll();
        }

        public void ExitAll()
        {
            if (exiting) return;
            exiting = true;
            timer.Stop();
            Settings.Save();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.SessionSwitch -= onSession;
            tray.Visible = false;
            foreach (PetForm p in Pets.ToArray())
            {
                try { p.AllowClose = true; p.Close(); } catch (Exception) { }
            }
            tray.Dispose();
            trayMenu.Dispose();
            if (trayHicon != IntPtr.Zero) { Native.DestroyIcon(trayHicon); trayHicon = IntPtr.Zero; }
            if (summon != null) summon.Dispose();
            ExitThread();
        }

        // ---- 右键菜单（宠物和托盘共用）
        public void BuildMenu(ContextMenuStrip m, PetForm pet)
        {
            while (m.Items.Count > 0) m.Items[0].Dispose();
            PetSettings S = Settings;
            if (pet != null)
            {
                Item(m.Items, "摸摸头", pet.CmdPet);
                Item(m.Items, "说句话", pet.CmdSay);
                Item(m.Items, "跳一下", pet.CmdJump);
                Item(m.Items, "跳个舞", pet.CmdDance);
                Item(m.Items, "想一想", pet.CmdThink);
                Item(m.Items, pet.IsSleeping ? "叫醒它" : "睡一觉", pet.CmdSleep);
                m.Items.Add(new ToolStripSeparator());
            }
            Check(m.Items, "跟随鼠标", S.Follow, delegate(bool v) { S.Follow = v; });
            Check(m.Items, "在窗口上行走", S.WalkOnWindows, delegate(bool v) { S.WalkOnWindows = v; });
            Check(m.Items, "安静模式（不说话）", S.Quiet, delegate(bool v)
            {
                S.Quiet = v;
                if (v) foreach (PetForm p in Pets) p.ClearBubble();
            });
            Check(m.Items, "休息提醒（连续用 45 分钟）", S.Reminders, delegate(bool v) { S.Reminders = v; nextRemind = activeSec + RemindEvery; });
            Check(m.Items, "全屏时自动隐藏", S.HideFullscreen, delegate(bool v) { S.HideFullscreen = v; });
            Check(m.Items, "AI 联动（Claude / ChatGPT）", S.ClaudeEnabled, delegate(bool v) { S.ClaudeEnabled = v; });
            ToolStripMenuItem status = new ToolStripMenuItem(S.ClaudeEnabled ? Claude.StatusText : "AI 联动已暂停");
            status.Enabled = false;
            m.Items.Add(status);

            ToolStripMenuItem size = new ToolStripMenuItem("大小");
            int[] sizes = { 2, 3, 4, 6 };
            string[] names = { "小", "中", "大", "特大" };
            for (int i = 0; i < sizes.Length; i++)
            {
                int sz = sizes[i];
                ToolStripMenuItem it = new ToolStripMenuItem(names[i]);
                it.Checked = S.Size == sz;
                it.Click += delegate { S.Size = sz; S.Save(); };
                size.DropDownItems.Add(it);
            }
            m.Items.Add(size);
            m.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem spawn = Item(m.Items, "召唤小伙伴", delegate { Spawn(false); });
            spawn.Enabled = ActiveCount() < MaxPets;
            // 正在说再见的不算数，保证至少留下一只（否则最后一只走了程序就退出了）
            if (pet != null && !pet.IsLeaving && ActiveCount() > 1)
                Item(m.Items, "送走这只", delegate { if (!pet.IsLeaving && ActiveCount() > 1) pet.SayGoodbye(); });
            Item(m.Items, "全部叫回来", RecallAll);
            Check(m.Items, "开机自动启动", AutoStart.Enabled, delegate(bool v) { AutoStart.Enabled = v; });
            m.Items.Add(new ToolStripSeparator());
            Item(m.Items, "退出", ExitAll);
        }

        ToolStripMenuItem Item(ToolStripItemCollection items, string text, Action a)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Click += delegate { Post(a); };
            items.Add(it);
            return it;
        }

        void Check(ToolStripItemCollection items, string text, bool on, Action<bool> set)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Checked = on;
            it.Click += delegate { set(!on); Settings.Save(); };
            items.Add(it);
        }
    }

    // ------------------------------------------------------------------ 图标
    static class IconMaker
    {
        /// <summary>生成 size×size 的 Clawd 图标（小尺寸自动去掉手臂，保持像素对齐）。</summary>
        public static Bitmap Render(int size)
        {
            Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.None;
                Pose p = new Pose();
                // 默认姿势的所有坐标都是偶数，所以 0.5 的倍数都能落在整像素上
                double withArms = Math.Floor(size * 0.94 / 36 * 2) / 2, noArms = Math.Floor(size * 0.94 / 24 * 2) / 2;
                float ps;
                if (withArms >= 0.5 && withArms * 36 >= noArms * 24 * 0.85) ps = (float)withArms;
                else { ps = (float)Math.Max(0.5, noArms); p.ArmL = p.ArmR = ArmStyle.Hidden; }
                Art.DrawSprite(g, size / 2f, (float)Math.Floor((size + 20 * ps) / 2), ps, p);
            }
            return b;
        }

        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            List<byte[]> imgs = new List<byte[]>();
            foreach (int s in sizes)
                using (Bitmap b = Render(s))
                {
                    if (s >= 256)
                        using (MemoryStream ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); imgs.Add(ms.ToArray()); }
                    else imgs.Add(Dib(b));
                }
            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(imgs[i].Length);
                    w.Write(offset);
                    offset += imgs[i].Length;
                }
                foreach (byte[] img in imgs) w.Write(img);
            }
        }

        // ICO 内的 32 位 DIB：BITMAPINFOHEADER（高度×2）+ 自下而上的 BGRA + 1 位 AND 掩码
        static byte[] Dib(Bitmap b)
        {
            int s = b.Width, maskStride = (s + 31) / 32 * 4;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(40); w.Write(s); w.Write(s * 2);
                w.Write((short)1); w.Write((short)32);
                w.Write(0); w.Write(s * s * 4 + maskStride * s);
                w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                for (int y = s - 1; y >= 0; y--)
                    for (int x = 0; x < s; x++)
                    {
                        Color c = b.GetPixel(x, y);
                        w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A);
                    }
                for (int y = s - 1; y >= 0; y--)
                {
                    byte[] row = new byte[maskStride];
                    for (int x = 0; x < s; x++)
                        if (b.GetPixel(x, y).A == 0) row[x >> 3] |= (byte)(0x80 >> (x & 7));
                    w.Write(row);
                }
                w.Flush();
                return ms.ToArray();
            }
        }
    }

    // ------------------------------------------------------------------ 调试：导出所有姿势的预览图
    static class SpriteSheet
    {
        static KeyValuePair<string, Pose> P(string k, Pose p) { return new KeyValuePair<string, Pose>(k, p); }

        public static void Render(string path)
        {
            List<KeyValuePair<string, Pose>> L = new List<KeyValuePair<string, Pose>>();
            Pose p;
            L.Add(P("默认", new Pose()));
            p = new Pose(); p.BodyH = 15; L.Add(P("呼吸", p));
            p = new Pose(); p.Lift[0] = 2; p.Lift[2] = 2; p.EyeDX = 1; L.Add(P("走路 A", p));
            p = new Pose(); p.Lift[1] = 2; p.Lift[3] = 2; p.EyeDX = 1; L.Add(P("走路 B", p));
            p = new Pose(); p.ArmL = p.ArmR = ArmStyle.Out; p.Lift[0] = 2; p.Lift[2] = 2; p.EyeDX = 1; L.Add(P("跑", p));
            p = new Pose(); p.LegLen = 1; p.ArmL = p.ArmR = ArmStyle.Down; L.Add(P("坐", p));
            p = new Pose(); p.LegLen = 0; p.BodyW = 26; p.BodyH = 14; p.ArmL = p.ArmR = ArmStyle.Hidden; p.Eyes = EyeStyle.Closed; L.Add(P("睡觉", p));
            p = new Pose(); p.BodyW = 28; p.BodyH = 12; p.LegLen = 2; p.ArmL = p.ArmR = ArmStyle.Out; p.Eyes = EyeStyle.Squint; L.Add(P("落地", p));
            p = new Pose(); p.BodyW = 26; p.BodyH = 14; p.LegLen = 2; L.Add(P("蓄力", p));
            p = new Pose(); p.BodyW = 22; p.BodyH = 18; p.ArmL = p.ArmR = ArmStyle.Up; p.Lift[0] = p.Lift[3] = 1; L.Add(P("起跳", p));
            p = new Pose(); p.ArmL = ArmStyle.Up; p.ArmR = ArmStyle.Out; p.LegLen = 5; p.Eyes = EyeStyle.Wide; L.Add(P("下落", p));
            p = new Pose(); p.ArmL = p.ArmR = ArmStyle.Up; p.LegLen = 6; p.LegDX = -2; p.Eyes = EyeStyle.Squint; L.Add(P("被拎起", p));
            p = new Pose(); p.ArmR = ArmStyle.Up; p.Eyes = EyeStyle.Happy; L.Add(P("挥手", p));
            p = new Pose(); p.ArmL = p.ArmR = ArmStyle.Up; p.Eyes = EyeStyle.Happy; p.Blush = true; L.Add(P("开心", p));
            p = new Pose(); p.Eyes = EyeStyle.Dizzy; p.ArmL = p.ArmR = ArmStyle.Down; p.OffX = 1; L.Add(P("晕", p));
            string[] typingNames = { "转身", "掏电脑", "打开", "放下", "放好", "放好了", "伸懒腰", "俯身", "敲键盘 1", "敲键盘 2", "敲键盘 3", "合上", "收起来" };
            for (int i = 0; i < Typing.Count; i++) { p = new Pose(); p.Frame = i; L.Add(P(typingNames[i], p)); }
            p = new Pose(); p.Eyes = EyeStyle.Squint; p.ArmL = p.ArmR = ArmStyle.Down; L.Add(P("生气", p));
            p = new Pose(); p.Eyes = EyeStyle.Sleepy; L.Add(P("犯困", p));
            p = new Pose(); p.Eyes = EyeStyle.Closed; L.Add(P("眨眼", p));
            p = new Pose(); p.ArmL = ArmStyle.Up; p.LegLen = 3; p.BodyW = 26; p.BodyH = 15; p.Eyes = EyeStyle.Happy; p.OffX = -1; L.Add(P("跳舞", p));

            int ps = 4, cw = 46 * ps, ch = 34 * ps, cols = 5, rows = (L.Count + cols - 1) / cols, extra = 200;
            using (Bitmap bmp = new Bitmap(cols * cw, rows * ch + extra, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(250, 248, 245));
                Font lf = Fonts.Text(15);
                for (int i = 0; i < L.Count; i++)
                {
                    int cx = (i % cols) * cw, cy = (i / cols) * ch;
                    bool dark = ((i % cols) + (i / cols)) % 2 == 1;
                    using (SolidBrush bg = new SolidBrush(dark ? Color.FromArgb(38, 38, 44) : Color.FromArgb(236, 233, 228)))
                        g.FillRectangle(bg, cx, cy, cw, ch);
                    g.SmoothingMode = SmoothingMode.None;
                    // 敲键盘的帧右边带着笔记本，往左挪一点免得出格
                    Art.DrawSprite(g, cx + cw / 2 - (L[i].Value.Frame >= 0 ? 8 * ps : 0), cy + ch - 4 * ps, ps, L[i].Value);
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (SolidBrush tb = new SolidBrush(dark ? Color.White : Color.Black))
                        g.DrawString(L[i].Key, lf, tb, cx + 6, cy + 4);
                }
                int gy = rows * ch + 16;
                string[][] pats = { Art.HeartPat, Art.ZedPat, Art.BangPat, Art.DropPat, Art.StarPat, Art.NotePat };
                Color[] fills = { Color.FromArgb(255, 92, 110), Color.FromArgb(245, 245, 245), Color.FromArgb(255, 210, 63),
                    Color.FromArgb(140, 210, 255), Color.FromArgb(255, 222, 92), Color.FromArgb(190, 168, 255) };
                for (int k = 0; k < pats.Length; k++)
                    Art.Glyph(g, pats[k], 20 + k * 50, gy, 4, fills[k], Color.FromArgb(50, 40, 40));
                int ix = 20;
                foreach (int s in new int[] { 16, 24, 32, 48, 64 })
                {
                    using (Bitmap ic = IconMaker.Render(s)) g.DrawImageUnscaled(ic, ix, gy + 50);
                    ix += s + 14;
                }
                Rectangle mon = new Rectangle(0, 0, bmp.Width, bmp.Height);
                BubbleLayout b1 = BubbleArt.Layout("你好呀，我是 Clawd！今天也要好好写代码哦～", null, 1.25f, 520, gy + 170, gy + 180, mon);
                b1.Alpha = 1;
                BubbleArt.Draw(g, b1);
                BubbleLayout b2 = BubbleArt.Layout("Pondering…", "✻", 1.25f, 800, gy + 170, gy + 180, mon);
                b2.Alpha = 1;
                BubbleArt.Draw(g, b2);
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}
