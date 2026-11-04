using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FrameSetup
{
    static class App
    {
        public static readonly string Dir = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameSetup");
        public static readonly string LogFile = Path.Combine(DataDir, "frame-setup.log");
        public static readonly string Apk = Path.Combine(Dir, "FrameHome.apk");
        public const string Package = "com.framehome.app";
        public const string ExampleServer = "http://192.168.1.100:8100";
        // build.cmd copies local-default-server.txt (never committed) next to the exe as default-server.txt.
        public static readonly string DefaultServer = ReadDefaultServer();
        public static readonly Dictionary<string, string> Settings = new Dictionary<string, string>();
        static readonly string SettingsFile = Path.Combine(DataDir, "settings.ini");
        static readonly object logLock = new object();

        // Frame Home version bundled with this exe; build.cmd writes FrameHome.version from the Gradle file.
        public static string ApkVersion
        {
            get
            {
                try
                {
                    string t = File.ReadAllText(Path.Combine(Dir, "FrameHome.version"));
                    int a = t.IndexOf('"'), b = t.IndexOf('"', a + 1);
                    if (a >= 0 && b > a) return t.Substring(a + 1, b - a - 1);
                }
                catch { }
                return "?";
            }
        }

        public static void Log(string text)
        {
            try
            {
                lock (logLock)
                {
                    Directory.CreateDirectory(DataDir);
                    File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text.Replace("\n", "\r\n                     ") + "\r\n");
                }
            }
            catch { }
        }

        public static void LoadSettings()
        {
            try
            {
                foreach (var line in File.ReadAllLines(SettingsFile))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) Settings[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }

        static string ReadDefaultServer()
        {
            try
            {
                string t = File.ReadAllText(Path.Combine(Dir, "default-server.txt")).Trim();
                if (t.Length > 0) return t;
            }
            catch { }
            return ExampleServer;
        }

        public static string Get(string key, string def) { string v; return Settings.TryGetValue(key, out v) && v.Length > 0 ? v : def; }

        public static void Set(string key, string value)
        {
            Settings[key] = value;
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllLines(SettingsFile, Settings.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            }
            catch { }
        }
    }

    class WizardForm : Form
    {
        static readonly string[] Names = { "Beta program", "ADB access", "Connect", "Frame detected", "Install Frame Home", "Dashboard", "Stays open", "Finished" };

        readonly Dictionary<string, string> opts;
        readonly bool demo;
        readonly St[] st = new St[8];
        readonly bool[] visited = new bool[8];
        readonly StepPage[] pages = new StepPage[8];
        int cur;

        readonly StepNav nav = new StepNav();
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg };
        readonly Label footStatus = new Label { AutoSize = false, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = Ui.Small };
        Button backBtn, nextBtn;
        readonly Label headRight = new Label { AutoSize = true, ForeColor = Ui.HeadSub, Font = Ui.Small, BackColor = Ui.Head };
        readonly Timer tick = new Timer { Interval = 1000 };
        int ticks;
        bool busy;

        // shared frame state
        string serial, deviceIp;
        string matchedId, matchedName;
        DateTime installedAt = DateTime.MinValue, dashWaitStart = DateTime.MinValue;
        Dictionary<string, bool> snapshot; // dashboard frames (id → online) just before install

        public WizardForm(Dictionary<string, string> opts)
        {
            this.opts = opts;
            demo = opts.ContainsKey("demo");
            Text = "Frame Setup";
            AutoScaleMode = AutoScaleMode.None;
            Font = Ui.Base;
            BackColor = Ui.Bg;
            ClientSize = new Size(Ui.S(960), Ui.S(700));
            MinimumSize = new Size(Ui.S(780), Ui.S(560));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            for (int i = 0; i < 8; i++) st[i] = St.Waiting;
            BuildPages();

            var right = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg };
            var foot = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(56), BackColor = Ui.Bg, Padding = new Padding(Ui.S(26), Ui.S(10), Ui.S(22), Ui.S(12)) };
            foot.Paint += (s, e) => { using (var p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, Ui.S(26), 0, foot.Width - Ui.S(22), 0); };
            backBtn = Ui.Btn("Back", false, (s, e) => Go(Math.Max(0, cur - 1)));
            nextBtn = Ui.Btn("Next", true, (s, e) => NextClicked());
            foot.Controls.Add(footStatus);
            foot.Controls.Add(backBtn);
            foot.Controls.Add(nextBtn);
            foot.Layout += (s, e) =>
            {
                int bh = Math.Max(backBtn.Height, nextBtn.Height), y = (foot.Height - bh) / 2 + 1;
                nextBtn.Location = new Point(foot.Width - Ui.S(22) - nextBtn.Width, y);
                backBtn.Location = new Point(nextBtn.Left - Ui.S(8) - backBtn.Width, y);
                footStatus.SetBounds(Ui.S(26), 1, Math.Max(10, backBtn.Left - Ui.S(36)), foot.Height - 1);
            };
            nextBtn.TextChanged += (s, e) => foot.PerformLayout();
            right.Controls.Add(content);
            right.Controls.Add(foot);
            content.BringToFront();

            var navPanel = new Panel { Dock = DockStyle.Left, Width = Ui.S(232), BackColor = Ui.NavBg };
            navPanel.Paint += (s, e) => { using (var p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, navPanel.Width - 1, 0, navPanel.Width - 1, navPanel.Height); };
            nav.Dock = DockStyle.Fill;
            nav.Names = Names; nav.States = st; nav.Visited = visited;
            nav.Picked += Go;
            var links = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Padding = new Padding(Ui.S(16), 0, 0, Ui.S(14)), BackColor = Ui.NavBg };
            links.Controls.Add(Link("Good to know…", (s, e) => ShowGotchas()));
            links.Controls.Add(Link("Open log", (s, e) => { App.Log("log opened"); try { Process.Start("notepad.exe", "\"" + App.LogFile + "\""); } catch { } }));
            navPanel.Controls.Add(nav);
            navPanel.Controls.Add(links);
            nav.BringToFront();
            navPanel.Width -= 1;

            Controls.Add(right);
            Controls.Add(navPanel);
            Controls.Add(BuildHeader());
            right.BringToFront();

            tick.Tick += async (s, e) => await OnTick();
            Shown += async (s, e) =>
            {
                int step = 1;
                if (opts.ContainsKey("step")) int.TryParse(opts["step"], out step);
                Go(Math.Max(0, Math.Min(7, step - 1)));
                if (demo) ApplyDemo();
                else { tick.Start(); await Startup(); }
                if (opts.ContainsKey("gotchas")) BeginInvoke(new Action(ShowGotchas));
                if (opts.ContainsKey("shot")) await AutoShot();
            };
            FormClosed += (s, e) => App.Log("closed");
        }

        static LinkLabel Link(string text, LinkLabelLinkClickedEventHandler click)
        {
            var l = new LinkLabel { Text = text, AutoSize = true, LinkColor = Ui.Accent, ActiveLinkColor = Ui.Accent, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, Ui.S(4), 0, 0), Font = Ui.Base };
            l.LinkClicked += click;
            return l;
        }

        protected override bool ShowWithoutActivation { get { return opts.ContainsKey("shot"); } }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Paint the Windows title bar the same colour as the header so they read as one bar (Windows 11).
        public static void ColorTitleBar(IntPtr handle, Color bg)
        {
            int dark = bg.GetBrightness() < 0.5f ? 1 : 0, caption = ColorRef(bg), text = ColorRef(dark == 1 ? Color.White : Ui.Text);
            DwmSetWindowAttribute(handle, 20, ref dark, 4);     // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(handle, 34, ref caption, 4);  // DWMWA_BORDER_COLOR
            DwmSetWindowAttribute(handle, 35, ref caption, 4);  // DWMWA_CAPTION_COLOR
            DwmSetWindowAttribute(handle, 36, ref text, 4);     // DWMWA_TEXT_COLOR
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ColorTitleBar(Handle, Ui.Head); }

        static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = Ui.S(68), BackColor = Ui.Head };
            var logo = new PictureBox { Image = Logo.Draw(Ui.S(42)), Size = new Size(Ui.S(42), Ui.S(42)), Location = new Point(Ui.S(18), Ui.S(10)), BackColor = Color.Transparent };
            var title = new Label { Text = "Frame Setup", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 15f), AutoSize = true, Location = new Point(Ui.S(70), Ui.S(7)) };
            var sub = new Label { Text = "Put Frame Home on a Lifelong Frameo photo frame, one step at a time", ForeColor = Ui.HeadSub, AutoSize = true, Location = new Point(Ui.S(72), Ui.S(38)) };
            headRight.Text = "Bundled: Frame Home " + App.ApkVersion;
            headRight.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.AddRange(new Control[] { logo, title, sub, headRight });
            header.Layout += (s, e) => headRight.Location = new Point(header.Width - headRight.Width - Ui.S(20), Ui.S(26));
            return header;
        }

        async Task Startup()
        {
            App.Log("Frame Setup started. adb: " + Adb.Exe + ", apk: " + App.Apk + " (" + App.ApkVersion + ")");
            var v = await Adb.Run("version", 8000);
            var line = v.Out.Split('\n').FirstOrDefault(l => l.StartsWith("Version "));
            if (line != null) headRight.Text = "Bundled: Frame Home " + App.ApkVersion + " · adb " + line.Substring(8).Split('-')[0];
            else headRight.Text = "Bundled: Frame Home " + App.ApkVersion + " · adb not found";
            headRight.Parent.PerformLayout();
            await Adb.Run("start-server", 15000);
        }

        // ---------------------------------------------------------------- navigation

        void Go(int i)
        {
            cur = i;
            visited[i] = true;
            content.SuspendLayout();
            content.Controls.Clear();
            content.Controls.Add(pages[i]);
            content.ResumeLayout();
            pages[i].Fit();
            OnEnter(i);
            Refresh2();
        }

        void Refresh2()
        {
            if (nextBtn == null) return;
            nav.Current = cur;
            nav.Invalidate();
            var s = st[cur];
            footStatus.Text = "Step " + (cur + 1) + " of 8 · " + (s == St.Passed ? "Done" : s == St.Failed ? "Needs a fix, see above" : "Waiting for this step");
            backBtn.Enabled = cur > 0;
            if (cur == 7) { nextBtn.Text = "Close"; nextBtn.Enabled = true; }
            else if (cur == 6 && s != St.Passed) { nextBtn.Text = "Skip"; nextBtn.Enabled = true; }
            else { nextBtn.Text = "Next"; nextBtn.Enabled = s == St.Passed; }
        }

        void SetSt(int i, St s) { st[i] = s; Refresh2(); }

        void NextClicked()
        {
            if (cur == 7) { Close(); return; }
            if (cur == 6 && st[6] != St.Passed) { SkipRestart(); }
            Go(cur + 1);
        }

        void OnEnter(int i)
        {
            if (demo) return;
            if (i == 3) { var _ = Detect(); }
            if (i == 4) UpdateInstallIdle();
            if (i == 5) { if (dashWaitStart == DateTime.MinValue) dashWaitStart = DateTime.Now; var _ = PollDash(); }
            if (i == 7) FillSummary();
        }

        async Task OnTick()
        {
            ticks++;
            if (restartPhase == 1 || restartPhase == 2) UpdateRestartProgress();
            if (busy) return;
            busy = true;
            try
            {
                if (cur == 2 && ticks % 2 == 0) await PollDevices();
                else if (cur == 5 && ticks % 5 == 0) await PollDash();
                else if ((restartPhase == 1 || restartPhase == 2) && ticks % 10 == 0) await PollRestart();
            }
            catch (Exception ex) { App.Log("tick: " + ex.Message); }
            finally { busy = false; }
        }

        // ---------------------------------------------------------------- pages

        CheckBox tick0, tick1;
        CheckView check0, check1, check2, check3, check4, check5, check6;
        bool help0, help1;

        void BuildPages()
        {
            // 1. Beta program
            var p = pages[0] = new StepPage("Turn on the Frameo beta program", "This unlocks the ADB access switch you need in the next step. Do this on the frame itself.");
            p.Body.Add(Stack.Numbered(
                "On the frame, tap the photo and open Settings (the gear).",
                "Scroll to the bottom and open About.",
                "Turn on Beta program.",
                "If the frame offers an update, let it install, then come back here."));
            var c = p.Body.Add(Stack.Card("Check"));
            check0 = c.Add(new CheckView());
            tick0 = c.Add(new CheckBox { Text = "Beta program is on", AutoSize = true, Font = Ui.Bold, Cursor = Cursors.Hand });
            tick0.CheckedChanged += (s, e) => Update0();
            c.Add(Link("Can't find Beta program?", (s, e) => { help0 = !help0; Update0(); }));
            Update0();

            // 2. ADB access
            p = pages[1] = new StepPage("Turn on ADB access", "ADB access lets this PC install Frame Home. It appears in Frameo's settings once the beta program is on.");
            p.Body.Add(Stack.Numbered(
                "On the frame open Settings → About.",
                "Turn on ADB access.",
                "Already on? Turn it off and on again. Frameo locks this PC out every time the frame restarts, so a fresh toggle is needed."));
            c = p.Body.Add(Stack.Card("Check"));
            check1 = c.Add(new CheckView());
            tick1 = c.Add(new CheckBox { Text = "ADB access is on", AutoSize = true, Font = Ui.Bold, Cursor = Cursors.Hand });
            tick1.CheckedChanged += (s, e) => Update1();
            c.Add(Link("No ADB access switch?", (s, e) => { help1 = !help1; Update1(); }));
            Update1();

            BuildConnect();
            BuildDetect();
            BuildInstall();
            BuildDashboard();
            BuildRestart();
            BuildFinished();
        }

        void Update0()
        {
            if (tick0.Checked) check0.Set(St.Passed, "Beta program is on", "You ticked this step.");
            else if (help0) check0.Set(St.Failed, "Can't find Beta program?", "", "Try this",
                "Make sure the frame is on Wi-Fi: Settings → Wi-Fi.",
                "Update Frameo first: Settings → About → Check for update.",
                "Beta program sits just under the version number in About.");
            else check0.Set(St.Waiting, "Waiting for you", "This step can't be checked from the PC. Tick the box when it's on.");
            SetSt(0, check0.State);
        }

        void Update1()
        {
            if (tick1.Checked) check1.Set(St.Passed, "ADB access is on", "You ticked this step. The next step checks it for real.");
            else if (help1) check1.Set(St.Failed, "No ADB access switch?", "", "Try this",
                "Go back one step and make sure Beta program is on.",
                "Let the frame install the beta update, restart it, then look again.",
                "The switch is in Settings → About, below Beta program.");
            else check1.Set(St.Waiting, "Waiting for you", "Tick the box once ADB access is on, or you've turned it off and on again.");
            SetSt(1, check1.State);
        }

        // ---------------------------------------------------------------- 3. connect

        RadioButton usbRadio, wifiRadio;
        Stack usbHelp, wifiCard;
        TextBox ipBox, portBox, devText;
        ComboBox scanRange;
        Button connectBtn, scanBtn, restartAdbBtn;
        ListView scanList;
        Label scanNote;
        bool connectRequested;
        string lastConnectMsg;

        void BuildConnect()
        {
            var p = pages[2] = new StepPage("Connect to the frame", "Use a USB cable, or connect over Wi-Fi with the frame's IP address. Both work the same.");
            usbRadio = new RadioButton { Text = "USB cable", AutoSize = true, Cursor = Cursors.Hand };
            wifiRadio = new RadioButton { Text = "Wi-Fi", AutoSize = true, Cursor = Cursors.Hand };
            bool wifi = App.Get("mode", "wifi") == "wifi";
            wifiRadio.Checked = wifi; usbRadio.Checked = !wifi;
            usbRadio.Margin = new Padding(0, 0, Ui.S(20), 0);
            p.Body.Add(Ui.Row(usbRadio, wifiRadio));

            usbHelp = p.Body.Add(Stack.Numbered(
                "Plug a USB cable into the frame's micro-USB port and into this PC.",
                "The wizard finds it by itself within a few seconds."));

            wifiCard = p.Body.Add(Stack.Card("Frame address"));
            ipBox = new TextBox { Width = Ui.S(150), Text = App.Get("ip", "") };
            portBox = new TextBox { Width = Ui.S(60), Text = "5555" };
            connectBtn = Ui.Btn("Connect", true, async (s, e) => await Connect());
            wifiCard.Add(Ui.Row(ipBox, Ui.Inline(":", Ui.Muted, Ui.Base), portBox, connectBtn));
            wifiCard.Add(new WrapLabel("Find the frame's IP on the frame: Settings → Wi-Fi, tap the connected network. Or scan for it:", Ui.Small, Ui.Muted));
            scanRange = new ComboBox { Width = Ui.S(170) };
            foreach (var n in NetScan.LocalSubnets()) scanRange.Items.Add(n);
            foreach (var n in new[] { "192.168.0.0/24", "192.168.1.0/24" }) if (!scanRange.Items.Contains(n)) scanRange.Items.Add(n);
            scanRange.Text = scanRange.Items.Count > 0 ? scanRange.Items[0].ToString() : "192.168.1.0/24";
            scanBtn = Ui.Btn("Scan network", false, async (s, e) => await Scan());
            wifiCard.Add(Ui.Row(scanRange, scanBtn));
            scanList = new ListView { View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, Height = Ui.S(96), BorderStyle = BorderStyle.FixedSingle, Font = Ui.Base };
            scanList.Columns.Add("IP", Ui.S(140));
            scanList.Columns.Add("Port 5555", Ui.S(90));
            scanList.Columns.Add("Note", Ui.S(330));
            scanList.ItemSelectionChanged += (s, e) => { if (e.IsSelected) ipBox.Text = e.Item.Text; };
            scanList.DoubleClick += async (s, e) => await Connect();
            wifiCard.Add(scanList);
            scanNote = wifiCard.Add(new WrapLabel("", Ui.Small, Ui.Faint));
            Stack.Show(scanList, false); Stack.Show(scanNote, false);

            var dc = p.Body.Add(Stack.Card("Live: adb devices"));
            devText = new TextBox { Multiline = true, AutoSize = false, WordWrap = false, ReadOnly = true, Height = Ui.S(52), BackColor = Ui.Head, ForeColor = Color.FromArgb(203, 213, 225), Font = Ui.Mono, BorderStyle = BorderStyle.None, Text = "List of devices attached\r\n(nothing found)", TabStop = false };
            var devWrap = new Stack(0) { BackColor = Ui.Head, Radius = Ui.S(6), Padding = new Padding(Ui.S(10), Ui.S(8), Ui.S(10), Ui.S(6)) };
            devWrap.Add(devText);
            dc.Add(devWrap);
            check2 = dc.Add(new CheckView());
            restartAdbBtn = Ui.Btn("Restart ADB", true, async (s, e) => await RestartAdb());
            check2.Extra.Add(Ui.Row(restartAdbBtn, Ui.Inline("Runs adb kill-server, then adb start-server, then connects again.", Ui.Muted)));

            EventHandler mode = (s, e) =>
            {
                bool w = wifiRadio.Checked;
                Stack.Show(usbHelp, !w); Stack.Show(wifiCard, w);
                App.Set("mode", w ? "wifi" : "usb");
                ShowNothing();
                CheckView.Relayout(wifiCard);
            };
            usbRadio.CheckedChanged += mode;
            Stack.Show(usbHelp, !wifi); Stack.Show(wifiCard, wifi);
            ShowNothing();
        }

        string Target { get { return ipBox.Text.Trim() + ":" + (portBox.Text.Trim().Length > 0 ? portBox.Text.Trim() : "5555"); } }

        void ShowNothing()
        {
            check2.Set(St.Waiting, "Nothing found yet", wifiRadio.Checked
                ? "Enter the frame's IP and press Connect, or press Scan network."
                : "Plug in the cable. Checking every 2 seconds.");
            check2.ShowExtra(false);
            SetSt(2, St.Waiting);
        }

        async Task Connect()
        {
            if (demo) return;
            if (ipBox.Text.Trim().Length == 0) { check2.Set(St.Waiting, "Enter the frame's IP address first", "Or press Scan network to find it."); return; }
            App.Set("ip", ipBox.Text.Trim());
            connectRequested = true;
            connectBtn.Enabled = false;
            check2.Set(St.Waiting, "Connecting to " + Target + "…", "");
            var r = await Adb.Run("connect " + Target, 12000);
            lastConnectMsg = r.Out;
            connectBtn.Enabled = true;
            await PollDevices();
        }

        async Task RestartAdb()
        {
            if (demo) return;
            restartAdbBtn.Enabled = false;
            check2.Set(St.Waiting, "Restarting ADB…", "adb kill-server, then adb start-server");
            check2.ShowExtra(false);
            await Adb.Run("kill-server", 10000);
            await Adb.Run("start-server", 15000);
            if (wifiRadio.Checked && ipBox.Text.Trim().Length > 0)
            {
                var r = await Adb.Run("connect " + Target, 12000);
                lastConnectMsg = r.Out;
            }
            restartAdbBtn.Enabled = true;
            await PollDevices();
        }

        async Task Scan()
        {
            if (demo) return;
            scanBtn.Enabled = false; scanBtn.Text = "Scanning…";
            var t0 = DateTime.Now;
            string range = scanRange.Text.Trim();
            List<string> found;
            try { found = await NetScan.Scan(range, 5555); }
            catch (Exception ex) { found = new List<string>(); App.Log("scan: " + ex.Message); }
            List<FrameInfo> frames = null;
            try { frames = await Dashboard.Frames(App.Get("server", App.DefaultServer)); } catch { }
            scanList.Items.Clear();
            foreach (var ip in found)
            {
                string note = "New device";
                var f = frames == null ? null : frames.FirstOrDefault(x => x.Ip == ip);
                if (f != null) note = f.Name + (f.Version != null ? " (already has Frame Home " + f.Version + ")" : "");
                scanList.Items.Add(new ListViewItem(new[] { ip, "open", note }));
            }
            scanNote.Text = "Scanned " + range + " in " + (DateTime.Now - t0).TotalSeconds.ToString("0.0") + " s · " + found.Count + (found.Count == 1 ? " device" : " devices") + " with port 5555 open" + (found.Count > 0 ? ". Click one to use it." : ". Check ADB access is on, or try another range.");
            Stack.Show(scanList, found.Count > 0); Stack.Show(scanNote, true);
            CheckView.Relayout(scanNote);
            scanBtn.Enabled = true; scanBtn.Text = "Scan network";
            App.Log("scan " + range + ": " + string.Join(", ", found.ToArray()));
        }

        async Task PollDevices()
        {
            var r = await Adb.Run("devices", 8000);
            var devs = Adb.ParseDevices(r.Out);
            devText.Text = "List of devices attached\r\n" + (devs.Count == 0 ? "(nothing found)" : string.Join("\r\n", devs.Select(d => d.Serial.PadRight(24) + d.State).ToArray()));
            AdbDevice dev;
            if (wifiRadio.Checked)
            {
                string t = Target;
                dev = devs.FirstOrDefault(d => d.Serial == t);
                if (dev == null && connectRequested && ticks % 6 == 0)
                {
                    var c = await Adb.Run("connect " + t, 12000);
                    lastConnectMsg = c.Out;
                }
            }
            else dev = devs.FirstOrDefault(d => !d.Wifi);

            if (dev == null)
            {
                string m = (lastConnectMsg ?? "").ToLowerInvariant();
                if (wifiRadio.Checked && connectRequested && (m.Contains("failed to connect") || m.Contains("cannot connect") || m.Contains("refused") || m.Contains("timed out")))
                {
                    check2.Set(St.Failed, "Can't reach " + Target, lastConnectMsg, "Fix it",
                        "Check the IP on the frame: Settings → Wi-Fi, tap the connected network.",
                        "Make sure ADB access is on (step 2). Turn it off and on again.",
                        "The frame and this PC must be on the same network, or networks that can reach each other.",
                        "Still nothing? Press Restart ADB.");
                    check2.ShowExtra(true);
                    SetSt(2, St.Failed);
                }
                else ShowNothing();
                serial = null;
            }
            else if (dev.State == "unauthorized")
            {
                check2.Set(St.Failed, "The frame is refusing this PC (unauthorized)", "", "Fix it",
                    "On the frame turn ADB access off and on again (Settings → About).",
                    "Look at the frame for \"Allow USB debugging?\", tick \"Always allow from this computer\", then tap OK.",
                    "No prompt? Press Restart ADB below. This often fixes it.");
                check2.ShowExtra(true);
                SetSt(2, St.Failed);
                serial = null;
            }
            else if (dev.State == "offline")
            {
                check2.Set(St.Failed, "The frame shows as offline", "", "Fix it",
                    "On the frame turn ADB access off and on again (Settings → About).",
                    "Press Restart ADB below.");
                check2.ShowExtra(true);
                SetSt(2, St.Failed);
                serial = null;
            }
            else if (dev.State == "device")
            {
                if (serial != dev.Serial) { App.Log("connected: " + dev.Serial); st[3] = St.Waiting; }
                serial = dev.Serial;
                check2.Set(St.Passed, "Connected", dev.Serial + ". The frame allowed this PC. Continue to the next step.");
                check2.ShowExtra(false);
                SetSt(2, St.Passed);
            }
            else
            {
                check2.Set(St.Waiting, "The frame is " + dev.State, "Waiting for it to finish connecting…");
                SetSt(2, St.Waiting);
            }
        }

        // When a later step finds the frame has locked ADB again.
        void LostConnection(CheckView cv, int step)
        {
            cv.Set(St.Failed, "Lost the connection to the frame", "Frameo probably came to the front and locked ADB again.", "Fix it",
                "On the frame turn ADB access off and on again (Settings → About).",
                "Go back to Connect and wait for Connected.",
                "Then come back to this step.");
            cv.ShowExtra(true);
            SetSt(step, St.Failed);
            SetSt(2, St.Waiting);
            serial = null;
        }

        // ---------------------------------------------------------------- 4. frame detected

        readonly Dictionary<string, Label> info = new Dictionary<string, Label>();
        static readonly string[] InfoKeys = { "Model", "Android", "Screen", "Memory", "Connected as", "Wi-Fi address", "Root (info only)", "Frame Home" };

        void BuildDetect()
        {
            var p = pages[3] = new StepPage("Frame detected", "Information read from the frame. Nothing here needs changing.");
            var c = p.Body.Add(Stack.Card("This frame"));
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, BackColor = Color.White, Margin = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(170)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach (var k in InfoKeys)
            {
                t.Controls.Add(new Label { Text = k, AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(0, Ui.S(3), 0, Ui.S(3)) });
                var v = new Label { Text = "–", AutoSize = true, ForeColor = Ui.Text, Margin = new Padding(0, Ui.S(3), 0, Ui.S(3)) };
                info[k] = v;
                t.Controls.Add(v);
            }
            c.Add(t);
            c = p.Body.Add(Stack.Card("Check"));
            check3 = c.Add(new CheckView());
            check3.Extra.Add(Ui.Row(Ui.Btn("Check again", false, async (s, e) => await Detect()), Ui.Btn("Back to Connect", false, (s, e) => Go(2))));
            check3.Set(St.Waiting, "Connect first", "Finish the Connect step, then this step reads the frame.");
        }

        async Task Detect()
        {
            if (serial == null)
            {
                check3.Set(St.Waiting, "Connect first", "Finish the Connect step, then this step reads the frame.");
                check3.ShowExtra(true);
                SetSt(3, St.Waiting);
                return;
            }
            check3.Set(St.Waiting, "Reading frame details…", "getprop, wm size, su -c id");
            check3.ShowExtra(false);
            var model = await Adb.Shell(serial, "getprop ro.product.model");
            if (!model.Ok || Adb.LostDevice(model)) { LostConnection(check3, 3); return; }
            string maker = (await Adb.Shell(serial, "getprop ro.product.manufacturer")).Out;
            string board = (await Adb.Shell(serial, "getprop ro.board.platform")).Out;
            string rel = (await Adb.Shell(serial, "getprop ro.build.version.release")).Out;
            string sdk = (await Adb.Shell(serial, "getprop ro.build.version.sdk")).Out;
            string size = (await Adb.Shell(serial, "wm size")).Out;
            string mem = (await Adb.Shell(serial, "cat /proc/meminfo")).Out;
            string ipOut = (await Adb.Shell(serial, "ip -f inet addr show wlan0")).Out;
            var root = await Adb.Shell(serial, "su -c id", 6000);
            string fh = (await Adb.Shell(serial, "dumpsys package " + App.Package + " | grep versionName")).Out;

            info["Model"].Text = model.Out + (board.Length > 0 && board != model.Out ? " (" + board + ")" : "") + (maker.Length > 0 ? " · " + maker : "");
            info["Android"].Text = rel + (sdk.Length > 0 ? " (API " + sdk + ")" : "");
            int i = size.LastIndexOf(':');
            info["Screen"].Text = i >= 0 ? size.Substring(i + 1).Trim().Replace("x", " × ") : size;
            info["Memory"].Text = MemText(mem);
            info["Connected as"].Text = serial;
            deviceIp = ParseInet(ipOut);
            if (deviceIp == null && serial.Contains(":")) deviceIp = serial.Split(':')[0];
            info["Wi-Fi address"].Text = deviceIp ?? "not on Wi-Fi";
            info["Root (info only)"].Text = root.Out.Contains("uid=0") ? "works  (" + root.Out.Split(' ')[0] + ")" : "not available";
            string ver = VersionName(fh);
            info["Frame Home"].Text = ver != null ? "installed, version " + ver : "not installed";

            check3.Set(St.Passed, "Looks like a frame Frame Home supports", maker + " " + model.Out + ", Android " + rel + ". Continue to install.");
            check3.ShowExtra(true);
            SetSt(3, St.Passed);
        }

        static string MemText(string meminfo)
        {
            var line = meminfo.Split('\n').FirstOrDefault(l => l.StartsWith("MemTotal"));
            if (line == null) return "–";
            long kb;
            if (!long.TryParse(new string(line.Where(char.IsDigit).ToArray()), out kb)) return "–";
            long mb = kb / 1024;
            return mb >= 700 ? Math.Max(1, Math.Round(mb / 1024.0, 1)) + " GB" : mb + " MB";
        }

        static string ParseInet(string s)
        {
            int i = s.IndexOf("inet ");
            if (i < 0) return null;
            string rest = s.Substring(i + 5);
            int j = rest.IndexOfAny(new[] { '/', ' ', '\n' });
            return j > 0 ? rest.Substring(0, j) : rest.Trim();
        }

        static string VersionName(string s)
        {
            int i = s.IndexOf("versionName=");
            if (i < 0) return null;
            return s.Substring(i + 12).Split(new[] { '\n', ' ', '\r' })[0].Trim();
        }

        // ---------------------------------------------------------------- 5. install

        Button installBtn;
        TextBox installLog;

        void BuildInstall()
        {
            var p = pages[4] = new StepPage("Install Frame Home", "Installs the copy of Frame Home that comes with this wizard, then opens it on the frame.");
            var c = p.Body.Add(Stack.Card("Install"));
            long kb = File.Exists(App.Apk) ? new FileInfo(App.Apk).Length / 1024 : 0;
            installBtn = Ui.Btn("Install Frame Home " + App.ApkVersion, true, async (s, e) => await Install());
            c.Add(Ui.Row(installBtn, Ui.Inline(File.Exists(App.Apk) ? "FrameHome.apk · " + kb + " KB" : "FrameHome.apk is missing next to this exe", Ui.Muted)));
            installLog = new TextBox { Multiline = true, AutoSize = false, ReadOnly = true, Height = Ui.S(150), BackColor = Ui.Head, ForeColor = Color.FromArgb(203, 213, 225), Font = Ui.Mono, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, TabStop = false };
            var wrap = new Stack(0) { BackColor = Ui.Head, Radius = Ui.S(6), Padding = new Padding(Ui.S(10), Ui.S(8), Ui.S(4), Ui.S(6)) };
            wrap.Add(installLog);
            c.Add(wrap);
            Stack.Show(wrap, false);
            c = p.Body.Add(Stack.Card("Check"));
            check4 = c.Add(new CheckView());
            check4.Extra.Add(Ui.Row(Ui.Btn("Back to Connect", false, (s, e) => Go(2))));
            check4.Set(St.Waiting, "Ready to install", "Press the button above.");
        }

        void UpdateInstallIdle()
        {
            if (st[4] == St.Passed) return;
            installBtn.Enabled = serial != null && File.Exists(App.Apk);
            if (serial == null) { check4.Set(St.Waiting, "Connect first", "Finish the Connect step, then install."); check4.ShowExtra(true); }
            else if (!File.Exists(App.Apk)) { check4.Set(St.Failed, "FrameHome.apk is missing", "It must sit next to FrameSetup.exe. Rebuild with build.cmd."); check4.ShowExtra(false); }
            else if (st[4] != St.Failed) { check4.Set(St.Waiting, "Ready to install", "Press the button above. Takes about 20 seconds."); check4.ShowExtra(false); }
        }

        void LogLine(string s)
        {
            Stack.Show(installLog.Parent, true);
            installLog.AppendText((installLog.TextLength > 0 ? "\r\n" : "") + s.Replace("\n", "\r\n"));
            CheckView.Relayout(installLog.Parent);
        }

        async Task Install()
        {
            if (demo || serial == null) return;
            installBtn.Enabled = false;
            installLog.Clear();
            check4.Set(St.Waiting, "Installing…", "This takes about 20 seconds.");
            check4.ShowExtra(false);
            SetSt(4, St.Waiting);
            try
            {
                var frames = await Dashboard.Frames(App.Get("server", App.DefaultServer));
                snapshot = frames.ToDictionary(f => f.Id, f => f.Online);
            }
            catch { snapshot = null; }

            AdbResult r = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                LogLine("> adb install -r FrameHome.apk");
                r = await Adb.Run("-s " + serial + " install -r \"" + App.Apk + "\"", 180000);
                LogLine(r.Out.Length > 0 ? r.Out : (r.TimedOut ? "(timed out)" : "(no output)"));
                if (r.Out.Contains("Success")) break;
                bool glitch = r.Out.Contains("failed to read copy response") || r.Out.Contains("EOF");
                if (attempt == 1 && glitch) { LogLine("Known glitch. Retrying once…"); await Task.Delay(1500); continue; }
                break;
            }

            if (!r.Out.Contains("Success"))
            {
                installBtn.Enabled = true;
                if (Adb.LostDevice(r)) { LostConnection(check4, 4); return; }
                string last = r.Out.Split('\n').LastOrDefault() ?? "";
                if (r.Out.Contains("VERSION_DOWNGRADE"))
                    check4.Set(St.Failed, "A newer Frame Home is already on the frame", last, "What to do",
                        "Nothing needs installing: the frame already has a newer version.",
                        "Press Next to carry on with the dashboard check.");
                else
                    check4.Set(St.Failed, "Install failed" + (r.Out.Contains("EOF") ? " twice" : ""), last, "Fix it",
                        "Turn ADB access off and on again on the frame (Frameo may have locked it).",
                        "Go back to Connect, wait for Connected, then come back and press Install again.",
                        "On USB, try another cable or port.");
                check4.ShowExtra(true);
                SetSt(4, St.Failed);
                if (r.Out.Contains("VERSION_DOWNGRADE")) SetSt(4, St.Passed);
                return;
            }

            LogLine("> dumpsys package " + App.Package + " | grep versionName");
            var v = await Adb.Shell(serial, "dumpsys package " + App.Package + " | grep versionName");
            LogLine(v.Out);
            LogLine("> am start -n " + App.Package + "/.MainActivity");
            var a = await Adb.Shell(serial, "am start -n " + App.Package + "/.MainActivity");
            LogLine(a.Out);
            string ver = VersionName(v.Out) ?? App.ApkVersion;
            info["Frame Home"].Text = "installed, version " + ver;
            installedAt = DateTime.Now;
            dashWaitStart = DateTime.Now;
            matchedId = null;
            SetSt(5, St.Waiting);
            check4.Set(St.Passed, "Frame Home " + ver + " installed and open", "You should see Frame Home on the frame now.");
            SetSt(4, St.Passed);
        }

        // ---------------------------------------------------------------- 6. dashboard

        TextBox serverBox;
        ListView framesList;

        void BuildDashboard()
        {
            var p = pages[5] = new StepPage("Connect to the dashboard", "Frame Home checks in with your Frame Home dashboard. The wizard waits until it shows up there.");
            var c = p.Body.Add(Stack.Card("Dashboard address"));
            serverBox = new TextBox { Width = Ui.S(290), Text = App.Get("server", App.DefaultServer) };
            c.Add(Ui.Row(serverBox, Ui.Btn("Check now", false, async (s, e) => { App.Set("server", serverBox.Text.Trim()); await PollDash(); }), Ui.Inline("Checking every 5 s", Ui.Muted)));
            serverBox.Leave += (s, e) => App.Set("server", serverBox.Text.Trim());
            c = p.Body.Add(Stack.Card("Frames the dashboard knows"));
            framesList = new ListView { View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, Height = Ui.S(110), BorderStyle = BorderStyle.FixedSingle, Font = Ui.Base };
            framesList.Columns.Add("Name", Ui.S(130));
            framesList.Columns.Add("ID", Ui.S(90));
            framesList.Columns.Add("State", Ui.S(70));
            framesList.Columns.Add("Version", Ui.S(70));
            framesList.Columns.Add("Screen", Ui.S(90));
            framesList.Columns.Add("", Ui.S(110));
            c.Add(framesList);
            check5 = c.Add(new CheckView());
            check5.Set(St.Waiting, "Waiting for the frame to check in…", "Usually takes under 30 seconds after install.");
        }

        FrameInfo FindFrame(List<FrameInfo> frames)
        {
            if (matchedId != null)
            {
                var m = frames.FirstOrDefault(f => f.Id == matchedId);
                if (m != null) return m;
            }
            if (deviceIp != null)
            {
                var byIp = frames.Where(f => f.Ip == deviceIp).OrderByDescending(f => f.Online).ThenByDescending(f => f.LastSeen).FirstOrDefault();
                if (byIp != null) return byIp;
            }
            if (snapshot != null)
            {
                var fresh = frames.FirstOrDefault(f => f.Online && !snapshot.ContainsKey(f.Id));
                if (fresh != null) return fresh;
                var woke = frames.FirstOrDefault(f => f.Online && snapshot.ContainsKey(f.Id) && !snapshot[f.Id] && f.Version == App.ApkVersion);
                if (woke != null) return woke;
            }
            return null;
        }

        async Task PollDash()
        {
            string server = serverBox.Text.Trim();
            List<FrameInfo> frames;
            try { frames = await Dashboard.Frames(server); }
            catch (Exception ex)
            {
                check5.Set(St.Failed, "Can't reach the dashboard", ex.Message, "Fix it",
                    "Check the address above. The default is " + App.DefaultServer + ".",
                    "Make sure the Frame Home dashboard stack is running in Portainer.",
                    "Open the address in a browser to test it.");
                SetSt(5, St.Failed);
                return;
            }
            var match = FindFrame(frames);
            framesList.BeginUpdate();
            framesList.Items.Clear();
            foreach (var f in frames)
                framesList.Items.Add(new ListViewItem(new[] { f.Name, f.Id, f.Online ? "online" : "offline", f.Online ? (f.Version ?? "?") : "–", f.Online ? (f.Screen ?? "?") : "–", f == match ? "← this frame" : "" }));
            framesList.EndUpdate();

            double waited = dashWaitStart == DateTime.MinValue ? 0 : (DateTime.Now - dashWaitStart).TotalSeconds;
            if (match != null && match.Online && match.Version == App.ApkVersion)
            {
                if (matchedId != match.Id) App.Log("dashboard: matched " + match.Name + " " + match.Id);
                matchedId = match.Id; matchedName = match.Name;
                App.Set("server", server);
                string when = installedAt != DateTime.MinValue && st[5] != St.Passed ? "Came online " + Clock(DateTime.Now - installedAt) + " after install." : "Checking in every few seconds.";
                if (st[5] != St.Passed || check5.State != St.Passed) check5.Set(St.Passed, match.Name + " is online with Frame Home " + match.Version, when);
                SetSt(5, St.Passed);
            }
            else if (match != null && match.Online)
                check5.Set(waited > 120 ? St.Failed : St.Waiting, match.Name + " is online but reports Frame Home " + (match.Version ?? "?"), "Waiting for " + App.ApkVersion + "…",
                    waited > 120 ? "Fix it" : null, "Go back to Install and install again.", "Restart Frame Home on the frame: long-press the photo → Settings → Restart.");
            else if (waited > 120)
            {
                check5.Set(St.Failed, "The frame hasn't checked in after 2 minutes", "Still checking every 5 s.", "Fix it",
                    "On the frame, long-press the photo and open Settings in Frame Home.",
                    "Set the dashboard address to " + server + " and save.",
                    "Make sure the frame and the dashboard are on networks that can reach each other.");
                SetSt(5, St.Failed);
            }
            else
            {
                check5.Set(St.Waiting, "Waiting for the frame to check in…", "Usually takes under 30 seconds after install.");
                SetSt(5, st[5] == St.Passed ? St.Passed : St.Waiting);
            }
            if (check5.State != St.Passed && st[5] == St.Passed) SetSt(5, check5.State);
        }

        static string Clock(TimeSpan t) { return (int)t.TotalMinutes + ":" + t.Seconds.ToString("00"); }

        // ---------------------------------------------------------------- 7. stays open

        Button restartBtn, selfBtn;
        ProgressBar restartBar;
        Label restartTime;
        int restartPhase; // 0 idle, 1 waiting for offline, 2 waiting for online, 3 back
        DateTime restartStart;
        long restartSeenBefore;
        bool skipped;
        TimeSpan backAfter;

        void BuildRestart()
        {
            var p = pages[6] = new StepPage("Stays open", "Frame Home's Keep open setting is on by default, so it comes back after Frameo or a restart pushes it away.");
            p.Body.Add(Stack.Note("After a restart Frame Home takes about 4–5 minutes to appear. The frame's software blocks quick auto-start, so Frame Home brings itself back with a timer. That's normal."));
            var c = p.Body.Add(Stack.Card("Optional: restart test"));
            restartBtn = Ui.Btn("Restart the frame", true, async (s, e) => await StartRestart(true));
            selfBtn = Ui.Btn("I'll restart it myself", false, async (s, e) => await StartRestart(false));
            c.Add(Ui.Row(restartBtn, selfBtn, Ui.Btn("Skip", false, (s, e) => { SkipRestart(); Go(7); })));
            c.Add(new WrapLabel("Runs adb reboot, then watches the dashboard. ADB is locked again after the restart, so the dashboard is the check, not ADB.", Ui.Small, Ui.Muted));
            restartBar = new ProgressBar { Height = Ui.S(8), Maximum = 1000, Style = ProgressBarStyle.Continuous };
            restartTime = new Label { AutoSize = true, ForeColor = Ui.Faint, Font = Ui.Small };
            c.Add(restartBar); c.Add(restartTime);
            Stack.Show(restartBar, false); Stack.Show(restartTime, false);
            check6 = c.Add(new CheckView());
            check6.Set(St.Waiting, "Not run yet", "Run the test, or skip it.");
        }

        void SkipRestart()
        {
            if (st[6] == St.Passed) return;
            skipped = true;
            check6.Set(St.Passed, "Skipped", "Keep open is on by default, so Frame Home should come back by itself.");
            SetSt(6, St.Passed);
        }

        async Task StartRestart(bool useAdb)
        {
            if (demo) return;
            if (matchedId == null)
            {
                check6.Set(St.Failed, "Finish the Dashboard step first", "The test watches the dashboard, so the wizard needs to know which frame this is.");
                SetSt(6, St.Failed);
                return;
            }
            if (useAdb)
            {
                if (serial == null) { NeedSelfRestart("Not connected over ADB right now."); return; }
                var r = await Adb.Run("-s " + serial + " reboot", 15000);
                if (Adb.LostDevice(r) || r.Code != 0) { NeedSelfRestart(r.Out); return; }
            }
            try
            {
                var f = (await Dashboard.Frames(serverBox.Text.Trim())).FirstOrDefault(x => x.Id == matchedId);
                restartSeenBefore = f != null ? f.LastSeen : 0;
            }
            catch { restartSeenBefore = 0; }
            App.Log("restart test started (" + (useAdb ? "adb reboot" : "user restarts") + ")");
            serial = null; SetSt(2, St.Waiting); // ADB is locked after a restart
            restartPhase = 1;
            restartStart = DateTime.Now;
            skipped = false;
            restartBtn.Enabled = selfBtn.Enabled = false;
            Stack.Show(restartBar, true); Stack.Show(restartTime, true);
            check6.Set(St.Waiting, "Frame is restarting… waiting for the dashboard to show it offline", "Checking the dashboard every 10 s.");
            SetSt(6, St.Waiting);
            UpdateRestartProgress();
        }

        void NeedSelfRestart(string why)
        {
            check6.Set(St.Failed, "ADB is locked, so the wizard couldn't restart the frame", why, "Do this instead",
                "Restart the frame yourself: unplug its power for 5 seconds, then plug it back in.",
                "Press \"I'll restart it myself\" straight away.");
            SetSt(6, St.Failed);
        }

        void UpdateRestartProgress()
        {
            var t = DateTime.Now - restartStart;
            restartBar.Value = (int)Math.Min(1000, t.TotalSeconds * 1000 / 300);
            restartTime.Text = Clock(t) + " elapsed · usually 4–5 min";
            if (t.TotalMinutes >= 8 && check6.State != St.Failed)
            {
                check6.Set(St.Failed, (matchedName ?? "The frame") + " hasn't come back after 8 minutes", "Still checking the dashboard.", "Try this",
                    "Look at the frame. If Frameo is showing, wait two more minutes; Frame Home returns on a timer.",
                    "In Frame Home long-press the photo → Settings, and make sure Keep open is on.",
                    "Still nothing? Open Frame Home from the frame's app list once, then run this test again.");
                SetSt(6, St.Failed);
            }
            CheckView.Relayout(restartTime);
        }

        async Task PollRestart()
        {
            List<FrameInfo> frames;
            try { frames = await Dashboard.Frames(serverBox.Text.Trim()); } catch { return; }
            var f = frames.FirstOrDefault(x => x.Id == matchedId);
            if (f == null) return;
            if (restartPhase == 1 && !f.Online)
            {
                restartPhase = 2;
                if (check6.State != St.Failed) check6.Set(St.Waiting, "Frame is restarting… the dashboard shows it offline", "Waiting for Frame Home to check in again.");
            }
            else if (restartPhase == 2 && f.Online)
            {
                restartPhase = 3;
                backAfter = DateTime.Now - restartStart;
                restartBar.Value = 1000;
                restartTime.Text = "Back online after " + Clock(backAfter);
                App.Log("restart test: back after " + Clock(backAfter));
                check6.Set(St.Passed, "Frame Home came back by itself", (f.Name ?? "The frame") + " is online again after " + Clock(backAfter) + " with " + (f.Version ?? "?") + ". ADB is locked again now, which is normal.");
                restartBtn.Enabled = selfBtn.Enabled = true;
                SetSt(6, St.Passed);
            }
        }

        // ---------------------------------------------------------------- 8. finished

        Stack summary;
        WrapLabel doneTitle;

        void BuildFinished()
        {
            var p = pages[7] = new StepPage("All done", "Summary for this frame.");
            doneTitle = (WrapLabel)((Stack)p.Body.Controls[0]).Controls[0];
            summary = p.Body.Add(Stack.Card(null));
            p.Body.Add(Stack.Note("Change photos, style and buttons from the dashboard, or on the frame: long-press the photo → Settings. To set up another frame, press Set up another."));
            p.Body.Add(Ui.Row(
                Ui.Btn("Set up another", true, (s, e) => ResetAll()),
                Ui.Btn("Open dashboard", false, (s, e) => { try { Process.Start(serverBox.Text.Trim()); } catch { } })));
        }

        void FillSummary()
        {
            bool all = st.Take(7).All(x => x == St.Passed);
            doneTitle.Text = all ? "All done" : "Almost done";
            if (all) st[7] = St.Passed;
            summary.Controls.Clear();
            for (int i = 0; i < 7; i++)
            {
                string extra = i == 6 && skipped ? " (skipped)" : i == 6 && restartPhase == 3 ? " (back after " + Clock(backAfter) + ")" : "";
                summary.Add(new WrapLabel((st[i] == St.Passed ? "✓  " : st[i] == St.Failed ? "✗  " : "–  ") + Names[i] + extra, Ui.Base, st[i] == St.Passed ? Ui.Text : Ui.Muted));
            }
            summary.Add(new WrapLabel(" ", Ui.Small, Ui.Muted));
            summary.Add(new WrapLabel("Frame:  " + (matchedName != null ? matchedName + " · " + matchedId : "not matched on the dashboard"), Ui.Base, Ui.Text));
            summary.Add(new WrapLabel("Address:  " + (deviceIp ?? "–"), Ui.Base, Ui.Text));
            summary.Add(new WrapLabel("Frame Home:  " + App.ApkVersion, Ui.Base, Ui.Text));
            summary.Add(new WrapLabel("Dashboard:  " + serverBox.Text.Trim(), Ui.Base, Ui.Text));
            pages[7].Fit();
            Refresh2();
        }

        void ResetAll()
        {
            App.Log("set up another");
            for (int i = 0; i < 8; i++) { st[i] = St.Waiting; visited[i] = false; }
            tick0.Checked = tick1.Checked = false; help0 = help1 = false;
            serial = deviceIp = matchedId = matchedName = null;
            snapshot = null; connectRequested = false; lastConnectMsg = null; skipped = false; restartPhase = 0;
            installedAt = dashWaitStart = DateTime.MinValue;
            installLog.Clear(); Stack.Show(installLog.Parent, false);
            foreach (var l in info.Values) l.Text = "–";
            framesList.Items.Clear();
            check3.Set(St.Waiting, "Connect first", "Finish the Connect step, then this step reads the frame.");
            check4.Set(St.Waiting, "Ready to install", "Press the button above.");
            check5.Set(St.Waiting, "Waiting for the frame to check in…", "Usually takes under 30 seconds after install.");
            check6.Set(St.Waiting, "Not run yet", "Run the test, or skip it.");
            Stack.Show(restartBar, false); Stack.Show(restartTime, false);
            restartBtn.Enabled = selfBtn.Enabled = true;
            ShowNothing();
            Update0(); Update1();
            Go(0);
        }

        // ---------------------------------------------------------------- good to know

        void ShowGotchas()
        {
            using (var f = new Form
            {
                Text = "Good to know", FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent, BackColor = Color.White, ClientSize = new Size(Ui.S(580), Ui.S(420)), Font = Ui.Base, AutoScaleMode = AutoScaleMode.None, Icon = Icon
            })
            {
                f.HandleCreated += (s, e) => ColorTitleBar(f.Handle, Color.White);
                var body = new Stack(10) { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(20), Ui.S(16), Ui.S(20), Ui.S(10)), BackColor = Color.White };
                foreach (var item in new[]
                {
                    new[] { "ADB gets locked again.", "Frameo clears this PC's ADB access every time the frame restarts or Frameo comes to the front. Fix: turn ADB access off and on again in Frameo settings (Settings → About)." },
                    new[] { "\"failed to read copy response: EOF\"", "This sometimes appears when installing. The wizard retries once by itself." },
                    new[] { "The wizard never taps the frame's screen.", "ADB taps can register as a long-press on these frames, so you do the tapping yourself." },
                    new[] { "No system files are changed.", "Frame Home is a normal app install. Nothing under /system is touched." },
                    new[] { "After a restart Frame Home takes about 4–5 minutes to appear.", "The frame's software blocks quick auto-start, so Frame Home brings itself back with a timer." },
                })
                {
                    var s = new Stack(2);
                    s.Add(new WrapLabel(item[0], Ui.Bold, Ui.Text));
                    s.Add(new WrapLabel(item[1], Ui.Base, Ui.Muted));
                    body.Add(s);
                }
                var ok = Ui.Btn("OK", true, (s, e) => f.Close());
                var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = Ui.S(50), Padding = new Padding(Ui.S(14), Ui.S(8), Ui.S(14), 0), BackColor = Color.White };
                ok.Margin = Padding.Empty;
                bottom.Controls.Add(ok);
                f.Controls.Add(body);
                f.Controls.Add(bottom);
                body.BringToFront();
                f.AcceptButton = ok;
                if (opts.ContainsKey("shot") && opts.ContainsKey("gotchas"))
                    f.Shown += async (s, e) => { await Task.Delay(500); Shot(f, opts["shot"]); f.Close(); };
                f.ShowDialog(this);
            }
            if (opts.ContainsKey("shot") && opts.ContainsKey("gotchas") && opts.ContainsKey("exit")) Close();
        }

        // ---------------------------------------------------------------- test hook

        // --demo fills every step with example values (no adb, no network): --step N --state waiting|failed|passed
        void ApplyDemo()
        {
            St s = St.Waiting;
            string want = opts.ContainsKey("state") ? opts["state"] : "passed";
            if (want == "failed") s = St.Failed; else if (want == "passed") s = St.Passed;
            for (int i = 0; i < cur; i++) st[i] = St.Passed;
            headRight.Text = "Bundled: Frame Home " + App.ApkVersion + " · adb 37.0.0";
            serial = "192.168.1.50:5555"; deviceIp = "192.168.1.50";
            tick0.Checked = cur > 0 || s == St.Passed; tick1.Checked = cur > 1 || s == St.Passed;
            if (s == St.Failed) { help0 = cur == 0; help1 = cur == 1; tick0.Checked = cur > 0; tick1.Checked = cur > 1; }
            Update0(); Update1();
            ipBox.Text = "192.168.1.50";
            wifiRadio.Checked = true;
            scanRange.Text = "192.168.1.0/24";
            serverBox.Text = App.ExampleServer;
            if (cur == 2)
            {
                scanList.Items.Add(new ListViewItem(new[] { "192.168.1.40", "open", "Kitchen frame (already has Frame Home 0.49)" }));
                scanList.Items.Add(new ListViewItem(new[] { "192.168.1.50", "open", "New device" }));
                scanNote.Text = "Scanned 192.168.1.0/24 in 3.1 s · 2 devices with port 5555 open. Click one to use it.";
                Stack.Show(scanList, true); Stack.Show(scanNote, true);
                if (s == St.Passed) { devText.Text = "List of devices attached\r\n192.168.1.50:5555       device"; check2.Set(St.Passed, "Connected", "192.168.1.50:5555. The frame allowed this PC. Continue to the next step."); }
                else if (s == St.Failed)
                {
                    devText.Text = "List of devices attached\r\n192.168.1.50:5555       unauthorized";
                    check2.Set(St.Failed, "The frame is refusing this PC (unauthorized)", "", "Fix it",
                        "On the frame turn ADB access off and on again (Settings → About).",
                        "Look at the frame for \"Allow USB debugging?\", tick \"Always allow from this computer\", then tap OK.",
                        "No prompt? Press Restart ADB below. This often fixes it.");
                    check2.ShowExtra(true);
                }
                st[2] = s;
            }
            if (cur >= 3)
            {
                info["Model"].Text = "rk312x · Lifelong"; info["Android"].Text = "6.0.1 (API 23)"; info["Screen"].Text = "800 × 1280";
                info["Memory"].Text = "1 GB"; info["Connected as"].Text = serial; info["Wi-Fi address"].Text = deviceIp;
                info["Root (info only)"].Text = "works  (uid=0(root))"; info["Frame Home"].Text = cur >= 5 ? "installed, version 0.49" : "not installed";
                if (cur == 3)
                {
                    if (s == St.Passed) check3.Set(St.Passed, "Looks like a frame Frame Home supports", "Lifelong rk312x, Android 6.0.1. Continue to install.");
                    else if (s == St.Failed) LostConnection(check3, 3);
                    else check3.Set(St.Waiting, "Reading frame details…", "getprop, wm size, su -c id");
                    check3.ShowExtra(s != St.Waiting);
                    st[3] = s; st[2] = s == St.Failed ? St.Waiting : St.Passed;
                }
            }
            if (cur == 4)
            {
                LogLine("> adb install -r FrameHome.apk\nPerforming Streamed Install\nadb: failed to read copy response: EOF\nKnown glitch. Retrying once…\n> adb install -r FrameHome.apk");
                if (s == St.Passed)
                {
                    LogLine("Performing Streamed Install\nSuccess\n> dumpsys package com.framehome.app | grep versionName\n    versionName=0.49\n> am start -n com.framehome.app/.MainActivity\nStarting: Intent { cmp=com.framehome.app/.MainActivity }");
                    check4.Set(St.Passed, "Frame Home 0.49 installed and open", "You should see Frame Home on the frame now.");
                }
                else if (s == St.Failed)
                {
                    LogLine("adb: failed to read copy response: EOF");
                    check4.Set(St.Failed, "Install failed twice", "adb: failed to read copy response: EOF", "Fix it",
                        "Turn ADB access off and on again on the frame (Frameo may have locked it).",
                        "Go back to Connect, wait for Connected, then come back and press Install again.",
                        "On USB, try another cable or port.");
                    check4.ShowExtra(true);
                }
                else check4.Set(St.Waiting, "Installing…", "This takes about 20 seconds.");
                st[4] = s;
            }
            if (cur == 5)
            {
                framesList.Items.Add(new ListViewItem(new[] { "Hallway frame", "demo0001", s == St.Passed ? "online" : "offline", s == St.Passed ? "0.49" : "–", s == St.Passed ? "800×1280" : "–", s == St.Passed ? "← this frame" : "" }));
                framesList.Items.Add(new ListViewItem(new[] { "Kitchen frame", "demo0002", "online", "0.49", "800×1280", "" }));
                if (s == St.Passed) check5.Set(St.Passed, "Hallway frame is online with Frame Home 0.49", "Came online 0:14 after install.");
                else if (s == St.Failed) check5.Set(St.Failed, "The frame hasn't checked in after 2 minutes", "Still checking every 5 s.", "Fix it",
                    "On the frame, long-press the photo and open Settings in Frame Home.",
                    "Set the dashboard address to " + App.ExampleServer + " and save.",
                    "Make sure the frame and the dashboard are on networks that can reach each other.");
                st[5] = s;
            }
            if (cur == 6)
            {
                matchedName = "Hallway frame";
                Stack.Show(restartBar, true); Stack.Show(restartTime, true);
                restartStart = DateTime.Now - (s == St.Passed ? TimeSpan.FromSeconds(277) : s == St.Failed ? TimeSpan.FromMinutes(8) : TimeSpan.FromSeconds(171));
                UpdateRestartProgress();
                if (s == St.Passed) { restartBar.Value = 1000; restartTime.Text = "Back online after 4:37"; check6.Set(St.Passed, "Frame Home came back by itself", "Hallway frame is online again after 4:37 with 0.49. ADB is locked again now, which is normal."); }
                else if (s == St.Waiting) check6.Set(St.Waiting, "Frame is restarting… the dashboard shows it offline", "Waiting for Frame Home to check in again.");
                st[6] = s;
            }
            if (cur == 7)
            {
                matchedId = "demo0001"; matchedName = "Hallway frame"; restartPhase = 3; backAfter = TimeSpan.FromSeconds(277);
                for (int i = 0; i < 7; i++) st[i] = St.Passed;
                FillSummary();
            }
            pages[cur].Fit();
            Refresh2();
        }

        [DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        async Task AutoShot()
        {
            if (opts.ContainsKey("gotchas")) return;
            int wait = 700;
            if (opts.ContainsKey("wait")) int.TryParse(opts["wait"], out wait);
            await Task.Delay(wait);
            Shot(this, opts["shot"]);
            if (opts.ContainsKey("exit")) Close();
        }

        // PrintWindow so the test never needs the window on top or focused; the DWM caption is drawn in the default style.
        static void Shot(Form f, string file)
        {
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(f.Handle, hdc, 2);
                    g.ReleaseHdc(hdc);
                }
                bmp.Save(file, ImageFormat.Png);
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var opts = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) continue;
                string key = args[i].Substring(2);
                string val = (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[++i] : "";
                opts[key] = val;
            }
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Ui.Scale = g.DpiX / 96f;
            App.LoadSettings();
            Application.Run(new WizardForm(opts));
        }
    }
}
