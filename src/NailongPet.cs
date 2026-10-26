using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("NailongPet")]
[assembly: AssemblyDescription("独立奶龙桌宠")]
[assembly: AssemblyProduct("NailongPet")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace NailongDesktop
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            bool test = Array.IndexOf(args, "--self-test") >= 0;
            if (test)
            {
                File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.txt"));
                File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-failed.txt"));
                Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "src"));
            }
            bool created;
            using (Mutex mutex = new Mutex(true, test ? "Local\\NailongPet.Test" : "Local\\NailongPet.Standalone", out created))
            {
                if (!created) return;
                try
                {
                    using (PetWindow window = new PetWindow(test, Array.IndexOf(args, "--inspect") >= 0))
                        Application.Run(window);
                }
                catch (Exception ex)
                {
                    if (test)
                    {
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-failed.txt"), ex.ToString());
                        Environment.ExitCode = 1;
                    }
                    else MessageBox.Show("奶龙启动失败：\n" + ex.Message, "NailongPet", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    internal sealed class Preferences
    {
        internal int X = int.MinValue, Y = int.MinValue, Scale = 125;
        internal bool Sound = true, OnTop = true;
        private static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NailongPet", "settings.txt"); } }
        internal static Preferences Load()
        {
            Preferences p = new Preferences();
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] pair = line.Split('=');
                    int n;
                    if (pair.Length != 2 || !int.TryParse(pair[1], out n)) continue;
                    switch (pair[0])
                    {
                        case "x": p.X = n; break;
                        case "y": p.Y = n; break;
                        case "scale": if (n == 75 || n == 100 || n == 125 || n == 150 || n == 200) p.Scale = n; break;
                        case "sound": p.Sound = n != 0; break;
                        case "top": p.OnTop = n != 0; break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return p;
        }
        internal void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new string[] { "x=" + X, "y=" + Y, "scale=" + Scale, "sound=" + (Sound ? 1 : 0), "top=" + (OnTop ? 1 : 0) });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal sealed class PetWindow : Form
    {
        private const int CellW = 192, CellH = 208, Pad = 14;
        private readonly int[] frameTimes = { 140, 95, 95, 105, 105, 150, 160, 100, 145 };
        private readonly Preferences prefs;
        private readonly Bitmap sheet;
        private readonly Bitmap[,] frames = new Bitmap[9, 8];
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly Dictionary<int, ToolStripMenuItem> actions = new Dictionary<int, ToolStripMenuItem>();
        private readonly Dictionary<int, ToolStripMenuItem> sizes = new Dictionary<int, ToolStripMenuItem>();
        private readonly SoundPlayer sound;
        private readonly Stream audioStream;
        private readonly NotifyIcon tray;
        private readonly Icon appIcon;
        private ToolStripMenuItem soundItem, topItem;
        private int row, frame, direction = 1;
        private int lastHop = -1;
        private double animationMs, remainingMs, lastMs, walkFractionX, walkFractionY;
        private double velocityX = 1, velocityY;
        private readonly Random random = new Random();
        private bool walking, dragging, moved, menuOpen, isClosing, soundPlaying;
        private Point mouseStart, mousePrevious, windowStart;
        private readonly bool testing;
        private readonly bool inspectable;
        private int testStage = -1;
        private double nextTest;
        private readonly List<string> checks = new List<string>();

        internal PetWindow(bool test, bool inspect)
        {
            testing = test;
            inspectable = inspect;
            prefs = test ? new Preferences() : Preferences.Load();
            Text = "奶龙桌宠 · NailongPet";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = inspect;
            StartPosition = FormStartPosition.Manual;
            TopMost = prefs.OnTop;
            AutoScaleMode = AutoScaleMode.None;
            using (Stream stream = Resource("sprites.png"))
            using (Bitmap image = new Bitmap(stream)) sheet = new Bitmap(image);
            using (Stream stream = Resource("pet.ico")) appIcon = new Icon(stream);
            Icon = appIcon;
            audioStream = Resource("laugh.wav");
            sound = new SoundPlayer(audioStream);
            sound.Load();
            BuildMenu();
            tray = new NotifyIcon { Icon = appIcon, Text = "奶龙桌宠 · 右键打开菜单", ContextMenuStrip = menu, Visible = !test };
            tray.DoubleClick += delegate { BringBack(); };
            BuildFrames();
            PlaceInitially();
            timer.Interval = 16;
            timer.Tick += Tick;
            MouseDown += OnPetMouseDown;
            MouseMove += OnPetMouseMove;
            MouseUp += OnPetMouseUp;
            MouseCaptureChanged += delegate {
                if (!Capture && dragging) { dragging = false; Play(0, 0); ClampToScreen(); Save(); }
            };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch += SessionChanged;
        }

        private static Stream Resource(string name)
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (s == null) throw new InvalidDataException("缺少内置资源：" + name);
            return s;
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= 0x00080000; if (!inspectable) p.ExStyle |= 0x00000080 | 0x08000000; return p; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Render();
            lastMs = clock.Elapsed.TotalMilliseconds;
            timer.Start();
            if (testing) nextTest = 100;
        }

        private void BuildMenu()
        {
            menu.Font = new Font("Microsoft YaHei UI", 10);
            menu.ShowImageMargin = false;
            ToolStripMenuItem demos = new ToolStripMenuItem("演示动作");
            AddAction(demos, "开心爆笑", 3);
            AddAction(demos, "完成欢呼", 4);
            AddAction(demos, "托腮思考", 6);
            AddAction(demos, "认真敲代码", 7);
            AddAction(demos, "检查代码", 8);
            AddAction(demos, "垂头丧气", 5);
            menu.Items.Add(demos);
            menu.Items.Add("走两步", null, delegate { Walk(); });
            menu.Items.Add("回到待机", null, delegate { Play(0, 0); });
            soundItem = new ToolStripMenuItem("爆笑音效") { Checked = prefs.Sound };
            soundItem.Click += delegate { prefs.Sound = !prefs.Sound; soundItem.Checked = prefs.Sound; UpdateAudio(); Save(); };
            menu.Items.Add(soundItem);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem sizeMenu = new ToolStripMenuItem("桌宠大小");
            foreach (int value in new int[] { 75, 100, 125, 150, 200 })
            {
                int scale = value;
                ToolStripMenuItem item = new ToolStripMenuItem(scale + "%") { Checked = scale == prefs.Scale };
                item.Click += delegate { ChangeScale(scale); };
                sizeMenu.DropDownItems.Add(item);
                sizes.Add(scale, item);
            }
            menu.Items.Add(sizeMenu);
            topItem = new ToolStripMenuItem("始终置顶") { Checked = prefs.OnTop };
            topItem.Click += delegate { prefs.OnTop = !prefs.OnTop; topItem.Checked = prefs.OnTop; TopMost = prefs.OnTop; Save(); };
            menu.Items.Add(topItem);
            menu.Items.Add("找回奶龙", null, delegate { BringBack(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Close(); });
            menu.Opening += delegate { menuOpen = true; dragging = false; Capture = false; UpdateAudio(); };
            menu.Closed += delegate { menuOpen = false; lastMs = clock.Elapsed.TotalMilliseconds; UpdateAudio(); };
        }
        private void AddAction(ToolStripMenuItem parent, string label, int state)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Click += delegate { Play(state, state == 4 ? 2600 : state == 5 ? 3600 : 0); };
            actions.Add(state, item);
            parent.DropDownItems.Add(item);
        }
        private void BuildFrames()
        {
            int w = CellW * prefs.Scale / 100, h = CellH * prefs.Scale / 100;
            Size = new Size(w + Pad * 2, h + Pad * 2);
            for (int r = 0; r < 9; r++)
                for (int f = 0; f < 8; f++)
                {
                    if (frames[r, f] != null) frames[r, f].Dispose();
                    Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(sheet, new Rectangle(Pad, Pad, w, h), new Rectangle(f * CellW, r * CellH, CellW, CellH), GraphicsUnit.Pixel);
                    }
                    frames[r, f] = bitmap;
                }
        }
        private void PlaceInitially()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = prefs.X == int.MinValue || prefs.Y == int.MinValue
                ? new Point(area.Right - Width - 70, area.Bottom - Height - 20)
                : new Point(prefs.X, prefs.Y);
            ClampToScreen();
        }
        private void ClampToScreen()
        {
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            Location = new Point(Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)), Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        }
        private static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); }
        private void ChangeScale(int value)
        {
            Point foot = new Point(Left + Width / 2, Bottom);
            prefs.Scale = value;
            BuildFrames();
            Location = new Point(foot.X - Width / 2, foot.Y - Height);
            ClampToScreen();
            foreach (KeyValuePair<int, ToolStripMenuItem> item in sizes) item.Value.Checked = item.Key == value;
            Render();
            Save();
        }
        private void BringBack()
        {
            Play(0, 0);
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Right - Width - 40, area.Bottom - Height - 20);
            ClampToScreen();
            Show();
            Render();
            Save();
        }
        private void DisplayChanged(object sender, EventArgs e)
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke((Action)delegate { ClampToScreen(); Render(); Save(); });
        }
        private void SessionChanged(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (!IsDisposed && IsHandleCreated)
                BeginInvoke((Action)delegate {
                    if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) { sound.Stop(); soundPlaying = false; }
                    else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) { Play(0, 0); ClampToScreen(); }
                });
        }
        private void Play(int state, double duration)
        {
            walking = false;
            row = state;
            frame = 0;
            animationMs = 0;
            remainingMs = duration;
            lastHop = -1;
            foreach (KeyValuePair<int, ToolStripMenuItem> item in actions) item.Value.Checked = item.Key == state;
            UpdateAudio();
            if (IsHandleCreated) Render();
        }
        private void Walk()
        {
            double angle = random.NextDouble() * Math.PI * 2;
            velocityX = Math.Cos(angle);
            velocityY = Math.Sin(angle);
            Play(direction > 0 ? 1 : 2, 6000);
            FaceWalking(velocityX);
            walking = true;
            walkFractionX = walkFractionY = 0;
            Render();
        }
        private void FaceWalking(double horizontal)
        {
            // The existing artwork has left/right walk cycles; retain facing on a vertical path.
            if (horizontal > 0.01) direction = 1;
            else if (horizontal < -0.01) direction = -1;
            row = direction > 0 ? 1 : 2;
        }
        private void UpdateAudio()
        {
            bool should = !testing && !isClosing && prefs.Sound && !menuOpen && !dragging && (row == 3 || row == 4);
            if (should == soundPlaying) return;
            if (should) sound.PlayLooping(); else sound.Stop();
            soundPlaying = should;
        }
        private void Tick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            double elapsed = Math.Min(100, Math.Max(0, now - lastMs));
            lastMs = now;
            if (testing && now >= nextTest)
            {
                RunNextCheck(now);
                nextTest = clock.Elapsed.TotalMilliseconds + 260;
            }
            if (isClosing || menuOpen) return;
            animationMs += elapsed;
            int previous = frame, previousRow = row;
            while (animationMs >= frameTimes[row]) { animationMs -= frameTimes[row]; frame = (frame + 1) % 8; }
            if (walking && !dragging) AdvanceWalk(elapsed);
            if (remainingMs > 0)
            {
                remainingMs -= elapsed;
                if (remainingMs <= 0) { Play(0, 0); Save(); return; }
            }
            int hop = Hop();
            if (previous != frame || previousRow != row || hop != lastHop) Render();
        }
        private void AdvanceWalk(double elapsed)
        {
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            double distance = elapsed * 0.085 * prefs.Scale / 100.0;
            walkFractionX += velocityX * distance;
            walkFractionY += velocityY * distance;
            int dx = (int)walkFractionX, dy = (int)walkFractionY;
            walkFractionX -= dx;
            walkFractionY -= dy;
            int nextX = Left + dx, nextY = Top + dy;
            int right = Math.Max(area.Left, area.Right - Width);
            int bottom = Math.Max(area.Top, area.Bottom - Height);
            if (nextX <= area.Left && velocityX < 0) { nextX = area.Left + (area.Left - nextX); velocityX = -velocityX; walkFractionX = 0; }
            else if (nextX >= right && velocityX > 0) { nextX = right - (nextX - right); velocityX = -velocityX; walkFractionX = 0; }
            if (nextY <= area.Top && velocityY < 0) { nextY = area.Top + (area.Top - nextY); velocityY = -velocityY; walkFractionY = 0; }
            else if (nextY >= bottom && velocityY > 0) { nextY = bottom - (nextY - bottom); velocityY = -velocityY; walkFractionY = 0; }
            FaceWalking(velocityX);
            Location = new Point(Clamp(nextX, area.Left, right), Clamp(nextY, area.Top, bottom));
        }
        private int Hop()
        {
            return row == 4 ? (int)(Math.Abs(Math.Sin((frame + animationMs / frameTimes[row]) / 8.0 * Math.PI * 2)) * 10) : 0;
        }
        private Bitmap CurrentImage()
        {
            if (row != 4) return frames[row, frame];
            Bitmap image = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(image)) g.DrawImageUnscaled(frames[row, frame], 0, -Hop());
            return image;
        }
        private void Render()
        {
            if (isClosing || !IsHandleCreated) return;
            Bitmap image = CurrentImage();
            try { Native.SetBitmap(Handle, image, Left, Top); lastHop = Hop(); }
            finally { if (row == 4) image.Dispose(); }
        }
        private void OnPetMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            BeginDrag(Cursor.Position);
        }
        private void BeginDrag(Point cursor)
        {
            Play(0, 0);
            dragging = true;
            moved = false;
            mouseStart = mousePrevious = cursor;
            windowStart = Location;
            Capture = true;
            UpdateAudio();
        }
        private void OnPetMouseMove(object sender, MouseEventArgs e)
        {
            if (testing) return;
            ContinueDrag(Cursor.Position);
        }
        private void ContinueDrag(Point cursor)
        {
            if (!dragging) return;
            Point delta = new Point(cursor.X - mouseStart.X, cursor.Y - mouseStart.Y);
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 5) moved = true;
            if (!moved) return;
            FaceWalking(cursor.X - mousePrevious.X);
            mousePrevious = cursor;
            Location = new Point(windowStart.X + delta.X, windowStart.Y + delta.Y);
            Render();
        }
        private void OnPetMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { menu.Show(Cursor.Position); return; }
            if (e.Button != MouseButtons.Left || !dragging) return;
            FinishDrag();
        }
        private void FinishDrag()
        {
            dragging = false;
            Capture = false;
            ClampToScreen();
            Save();
            if (!moved) Play(3, 2520); else Play(0, 0);
        }
        private void Save()
        {
            if (testing) return;
            prefs.X = Left; prefs.Y = Top; prefs.Save();
        }

        private void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("自检失败：" + label);
            checks.Add("PASS " + label);
        }
        private void RunNextCheck(double now)
        {
            nextTest = now + 260;
            testStage++;
            if (testStage == 0)
            {
                Require(sheet.Width == 1536 && sheet.Height >= 1872, "embedded sprite dimensions");
                Require(audioStream.Length > 44, "embedded WAV loaded by SoundPlayer");
                Require(appIcon.Width > 0, "embedded application icon");
                Require(actions.Count == 6, "all six demo menu actions");
                Require((Native.GetWindowLong(Handle, -20) & 0x80000) != 0, "native layered window");
                for (int r = 0; r < 9; r++)
                    for (int f = 0; f < 8; f++)
                        Require(frames[r, f].GetPixel(0, 0).A == 0, "transparent corner " + r + "/" + f);
            }
            else if (testStage <= 9)
            {
                Play(testStage - 1, 0);
                Require(row == testStage - 1 && !walking, "play animation row " + row);
                Bitmap image = CurrentImage();
                try { image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "src", "preview-" + row + ".png"), ImageFormat.Png); }
                finally { if (row == 4) image.Dispose(); }
            }
            else if (testStage == 10)
            {
                Walk();
                Rectangle area = Screen.FromControl(this).WorkingArea;
                Point center = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
                foreach (int degrees in new int[] { 0, 17, 45, 73, 90, 135, 180, 225, 270, 315 })
                {
                    Location = center;
                    double angle = degrees * Math.PI / 180;
                    velocityX = Math.Cos(angle); velocityY = Math.Sin(angle);
                    walkFractionX = walkFractionY = 0;
                    AdvanceWalk(200);
                    double travel = 200 * 0.085 * prefs.Scale / 100.0;
                    Require(Math.Abs(Left - center.X - velocityX * travel) < 1.1 && Math.Abs(Top - center.Y - velocityY * travel) < 1.1, "free angle movement " + degrees + " degrees");
                }
                Location = new Point(area.Left, area.Top + 20);
                velocityX = -0.8; velocityY = 0.6;
                walkFractionX = walkFractionY = 0;
                AdvanceWalk(100);
                Require(velocityX > 0 && velocityY > 0 && row == 1 && Left >= area.Left, "walking reflects X at left edge");
                Location = new Point(area.Right - Width, Top);
                velocityX = 0.8;
                AdvanceWalk(100);
                Require(velocityX < 0 && row == 2 && Right <= area.Right, "walking reflects X at right edge");
                Location = new Point(center.X, area.Top);
                velocityX = 0.6; velocityY = -0.8;
                walkFractionX = walkFractionY = 0;
                AdvanceWalk(100);
                Require(velocityY > 0 && velocityX > 0 && Top >= area.Top, "walking reflects Y at top edge");
                Location = new Point(center.X, area.Bottom - Height);
                velocityY = 0.8;
                AdvanceWalk(100);
                Require(velocityY < 0 && Bottom <= area.Bottom, "walking reflects Y at bottom edge");
                Location = new Point(area.Right - Width, area.Bottom - Height);
                velocityX = 0.6; velocityY = 0.8;
                AdvanceWalk(100);
                Require(velocityX < 0 && velocityY < 0 && area.Contains(Bounds), "corner collision reflects both axes");
                remainingMs = 1;
            }
            else if (testStage == 11)
            {
                Require(row == 0 && !walking, "timed walk returns to idle");
                ChangeScale(75); Require(Width == 172, "75 percent scale");
                ChangeScale(200); Require(Width == 412, "200 percent scale");
                ChangeScale(125);
                Location = new Point(-100000, -100000); ClampToScreen();
                Require(Screen.FromControl(this).WorkingArea.Contains(Bounds), "offscreen position recovered");
                Play(3, 20);
            }
            else if (testStage == 12)
            {
                Require(row == 0, "timed laugh returns to idle");
                Play(7, 0);
                menu.Show(Location);
                Require(menuOpen, "right click menu opens and pauses playback");
                menu.Close();
                Require(!menuOpen, "menu closes and resumes playback");
                Play(0, 0);
            }
            else if (testStage == 13)
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
                BeginDrag(new Point(500, 500));
                ContinueDrag(new Point(550, 530));
                Require(dragging && moved && row == 1, "drag right selects right walk cycle");
                Require(Location == new Point(windowStart.X + 50, windowStart.Y + 30), "drag supports diagonal movement");
            }
            else if (testStage == 14)
            {
                Require(dragging && frame > 0 && row == 1, "drag walk keeps animating while mouse is held still");
                ContinueDrag(new Point(520, 510));
                Require(row == 2, "drag reversal changes to left walk cycle");
                ContinueDrag(new Point(520, 550));
                Require(row == 2, "vertical drag retains current facing");
            }
            else if (testStage == 15)
            {
                Require(dragging && row == 2, "left walk remains active throughout drag");
                FinishDrag();
                Require(!dragging && !Capture && row == 0 && remainingMs == 0, "release after dragging returns to idle");
                BeginDrag(new Point(500, 500));
                FinishDrag();
                Require(row == 3 && remainingMs > 0, "click without dragging still laughs");
                Play(0, 0);
            }
            else if (testStage == 16)
            {
                File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.txt"), checks.ToArray());
                Close();
            }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            isClosing = true;
            timer.Stop();
            Save();
            sound.Stop();
            tray.Visible = false;
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
                Microsoft.Win32.SystemEvents.SessionSwitch -= SessionChanged;
                timer.Dispose();
                sound.Dispose(); audioStream.Dispose(); tray.Dispose(); menu.Dispose();
                for (int r = 0; r < 9; r++) for (int f = 0; f < 8; f++) if (frames[r, f] != null) frames[r, f].Dispose();
                sheet.Dispose(); appIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; internal Point(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct Size { internal int W, H; internal Size(int w, int h) { W = w; H = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { internal byte Op, Flags, Alpha, Format; }
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref Point pos, ref Size size, IntPtr src, ref Point zero, uint key, ref Blend blend, uint flags);
        internal static void SetBitmap(IntPtr handle, Bitmap bitmap, int x, int y)
        {
            IntPtr screen = GetDC(IntPtr.Zero), dc = IntPtr.Zero, image = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                dc = CreateCompatibleDC(screen);
                image = bitmap.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(dc, image);
                Point pos = new Point(x, y), zero = new Point(0, 0);
                Size size = new Size(bitmap.Width, bitmap.Height);
                Blend blend = new Blend { Op = 0, Alpha = 255, Format = 1 };
                if (!UpdateLayeredWindow(handle, screen, ref pos, ref size, dc, ref zero, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(dc, old);
                if (image != IntPtr.Zero) DeleteObject(image);
                if (dc != IntPtr.Zero) DeleteDC(dc);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
