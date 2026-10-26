using System;
using System.Drawing;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
using ClawdPet;

class DockAvoidanceTests
{
    static int checks;
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static void Set(object target, string name, object value)
    { target.GetType().GetField(name, Fields).SetValue(target, value); }
    static void State(PetForm pet, string name)
    {
        FieldInfo field = typeof(PetForm).GetField("st", Fields);
        field.SetValue(pet, Enum.Parse(field.FieldType, name));
    }
    static object Call(PetForm pet, string name, double dt)
    { return typeof(PetForm).GetMethod(name, Fields).Invoke(pet, new object[] { dt }); }

    // Exercise the real support / falling code without starting another pet or tray icon.
    static void PhysicsChecks(Rectangle monitor, Rectangle canvas, Rectangle dock)
    {
        PetContext context = (PetContext)FormatterServices.GetUninitializedObject(typeof(PetContext));
        WindowScanner scanner = new WindowScanner();
        List<WinInfo> windows = new List<WinInfo> { new WinInfo { Dock = true, R = canvas, DockContent = dock } };
        Set(scanner, "list", windows); Set(scanner, "lastScan", 0.0);
        Set(context, "Settings", new PetSettings { WalkOnWindows = false });
        Set(context, "Scanner", scanner); Set(context, "Rng", new Random(1));
        Set(context, "clock", Stopwatch.StartNew());
        using (PetForm pet = new PetForm(context, 1280, 1438, null))
        {
            Set(pet, "mon", monitor); Set(pet, "area", monitor); Set(pet, "now", 0.0);
            Set(pet, "ps", 3); Set(pet, "ui", 1.0); State(pet, "Idle");
            Call(pet, "UpdateSupport", 0.016);
            Expect(pet.FeetY, 1324, "standing pet rests just above visible icons with window walking off");
            Set(pet, "fy", 1280.0); Set(pet, "vy", 900.0); State(pet, "Fall");
            Call(pet, "UpdateAir", 0.05);
            Expect(pet.FeetY, 1324, "falling pet lands above icons instead of transparent headroom");
            foreach (double x in new double[] { 580, 1980 })
            {
                Set(pet, "fx", x); Set(pet, "fy", 1324.0); State(pet, "Idle");
                bool edgeSupport = (bool)Call(pet, "UpdateSupport", 0.016);
                Expect(edgeSupport ? 1 : 0, 0, "walking past Dock edge starts falling at x=" + x);
                Expect(pet.FeetY, 1324, "edge departure does not teleport downward");
                Set(pet, "fy", 1430.0); Set(pet, "vy", 900.0);
                Call(pet, "UpdateAir", 0.05);
                Expect(pet.FeetY, 1438, "pet reaches normal screen floor outside Dock at x=" + x);
            }
            Set(pet, "fx", 1280.0); Set(pet, "fy", 1324.0);
            windows.Clear(); State(pet, "Idle");
            bool supported = (bool)Call(pet, "UpdateSupport", 0.016);
            Expect(supported ? 1 : 0, 0, "hidden Dock releases support into a natural fall");
        }
    }
    static void ContentChecks()
    {
        using (Bitmap bitmap = new Bitmap(2560, 274))
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Black);
            Expect(DockContentScanner.FindContent(bitmap).IsEmpty ? 1 : 0, 1, "blank capture cannot reserve full width");
            g.FillRectangle(Brushes.White, 641, 162, 80, 80);
            g.FillRectangle(Brushes.Gray, 1837, 170, 80, 78);
            using (Brush shadow = new SolidBrush(Color.FromArgb(8, 8, 8)))
                g.FillRectangle(shadow, 600, 120, 10, 10);
            Rectangle content = DockContentScanner.FindContent(bitmap);
            Expect(content.Left, 641, "transparent left margin excluded");
            Expect(content.Right, 1917, "transparent right margin excluded");
            Expect(content.Top, 162, "magnification headroom and faint shadow excluded");
            Expect(content.Bottom, 248, "visible content bottom detected");
            g.Clear(Color.White);
            Expect(DockContentScanner.FindContent(bitmap).IsEmpty ? 1 : 0, 1, "solid failed capture rejected");
            g.Clear(Color.Transparent);
            Expect(DockContentScanner.FindContent(bitmap).IsEmpty ? 1 : 0, 1, "transparent capture ignored");
        }
    }
    static void Expect(double actual, double expected, string name)
    {
        if (Math.Abs(actual - expected) > 0.01) throw new Exception(name + ": " + actual + " != " + expected);
        checks++;
        Console.WriteLine("PASS " + name);
    }
    static int Main()
    {
        try
        {
            Rectangle monitor = new Rectangle(0, 0, 2560, 1440);
            Rectangle canvas = new Rectangle(0, 1166, 2560, 274);
            Rectangle dock = new Rectangle(641, 1328, 1276, 86);
            Expect(WindowScanner.BottomDockFloor(canvas, dock, monitor, 1280, 42, 1438, 4), 1324, "measured visible Dock raises floor only to icon tops");
            Expect(WindowScanner.BottomDockFloor(canvas, dock, monitor, 400, 42, 1438, 4), 1438, "left of Dock has normal screen floor");
            Expect(WindowScanner.BottomDockFloor(canvas, dock, monitor, 2200, 42, 1438, 4), 1438, "right of Dock has normal screen floor");
            Expect(WindowScanner.BottomDockFloor(canvas, dock, monitor, 620, 42, 1438, 4), 1324, "sprite edge overlap is protected");
            Expect(WindowScanner.BottomDockFloor(new Rectangle(0, 2286, 2560, 274), dock, monitor, 1280, 42, 1438, 4), 1438, "off-screen Dock ignores cached content");
            Expect(WindowScanner.BottomDockFloor(new Rectangle(0, 1439, 2560, 274), dock, monitor, 1280, 42, 1438, 4), 1438, "one-pixel reveal strip ignored");
            Expect(WindowScanner.BottomDockFloor(new Rectangle(0, 0, 2560, 54), dock, monitor, 1280, 42, 1438, 4), 1438, "top Finder bar is not a bottom Dock");
            Expect(WindowScanner.BottomDockFloor(new Rectangle(0, 0, 2560, 1440), dock, monitor, 1280, 42, 1438, 4), 1438, "full-screen overlay ignored");
            Expect(WindowScanner.BottomDockFloor(new Rectangle(0, 200, 140, 1000), dock, monitor, 1280, 42, 1438, 4), 1438, "side Dock does not reserve bottom");
            Expect(WindowScanner.BottomDockFloor(canvas, dock, monitor, 1280, 42, 1250, 4), 1250, "existing larger taskbar reservation preserved");
            Expect(WindowScanner.BottomDockFloor(canvas, dock, monitor, 1280, 56, 1438, 6), 1322, "DPI gap applied to icon tops");
            Expect(WindowScanner.BottomDockFloor(canvas, Rectangle.Empty, monitor, 1280, 42, 1438, 4), 1438, "missing content never falls back to full-width canvas");
            Rectangle leftMonitor = new Rectangle(-2560, -200, 2560, 1440);
            Rectangle leftDock = new Rectangle(-1919, 1128, 1276, 86);
            Rectangle leftCanvas = new Rectangle(-2560, 966, 2560, 274);
            Expect(WindowScanner.BottomDockFloor(leftCanvas, leftDock, leftMonitor, -1280, 42, 1238, 4), 1124, "negative multi-monitor coordinates");
            Expect(WindowScanner.BottomDockFloor(canvas, dock, leftMonitor, -1280, 42, 1238, 4), 1238, "other monitor Dock ignored");
            ContentChecks();
            PhysicsChecks(monitor, canvas, dock);
            Console.WriteLine("SUCCESS: " + checks + " checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
