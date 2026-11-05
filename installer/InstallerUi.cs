// 安装器窗口界面：无边框窗口，左侧渐变背景 + 主形象，熊猫从卡片右上角探头，右侧白色圆角卡片（左栏步骤和进度，右栏日志）。
// 安装流程见 DafeiyuInstaller.cs。

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

partial class InstallerForm : Form
{
    // 配色取自形象：深蓝头发、浅蓝蝴蝶结、白色围裙
    static readonly Color Ink = Color.FromArgb(30, 36, 64);
    static readonly Color Muted = Color.FromArgb(118, 126, 150);
    static readonly Color Line = Color.FromArgb(222, 228, 242);
    static readonly Color Accent = Color.FromArgb(64, 110, 230);
    static readonly Color OkColor = Color.FromArgb(34, 168, 140);
    static readonly Color BadColor = Color.FromArgb(214, 54, 54);
    static readonly Color BgTop = Color.FromArgb(240, 246, 255);
    static readonly Color BgBottom = Color.FromArgb(214, 226, 252);
    static readonly Color SoftFill = Color.FromArgb(245, 247, 252);

    const int W = 1100, H = 644;
    static readonly Rectangle CardBox = new Rectangle(400, 80, 672, 536);

    static readonly string[] StepTitles = { "检测 DeepSeek Harness", "安装 DSH 桌面版", "安装大肥鱼插件", "重启 DSH" };
    static readonly string[] StepHints = { "查找本机已安装的 DSH", "优先用同目录安装包，没有才下载", "需要能访问 GitHub", "让插件在 DSH 里生效" };

    readonly string dshOverride;
    readonly string dshUrl;
    readonly StepState[] states = new StepState[4];
    readonly string[] details = (string[])StepHints.Clone();
    string statusText = "点击「开始安装」，过程中请保持联网";
    Color statusColor = Muted;
    int progressValue;
    bool marquee;
    float phase;

    float scale = 1f;
    Bitmap background;
    Image mascot, panda;
    Font titleFont, subFont, stepFont, smallFont, numFont, bodyFont, brandFont;
    TextBox logBox;
    RoundPanel logFrame, donePanel;
    PillButton mainButton, tutorialButton, closeButton;
    System.Windows.Forms.Timer animTimer;
    StreamWriter logFile;
    readonly string logPath = Path.Combine(Path.GetTempPath(), "dafeiyu-install.log");
    volatile bool busy, cancel;
    int currentStep;
    bool finished;
    int hoverTitleButton = -1; // 0 = 最小化, 1 = 关闭

    public InstallerForm(string dshOverride, string dshUrl)
    {
        this.dshOverride = dshOverride;
        this.dshUrl = dshUrl;
        try { logFile = new StreamWriter(logPath, false, new UTF8Encoding(false)); logFile.AutoFlush = true; } catch { }

        using (var g = CreateGraphics()) scale = g.DpiX / 96f;
        SuspendLayout();
        Text = Config.Title;
        AutoScaleMode = AutoScaleMode.None;
        Font = new Font("Microsoft YaHei UI", 9F);
        titleFont = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);
        subFont = new Font("Microsoft YaHei UI", 9F);
        stepFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        smallFont = new Font("Microsoft YaHei UI", 8.5F);
        numFont = new Font("Segoe UI", 9F, FontStyle.Bold);
        bodyFont = new Font("Microsoft YaHei UI", 9F);
        brandFont = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(S(W), S(H));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        mascot = LoadImage("mascot.png");
        panda = LoadImage("panda.png");
        try
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                if (s != null) Icon = new Icon(s);
        }
        catch { }

        BuildControls();
        animTimer = new System.Windows.Forms.Timer { Interval = 30 };
        animTimer.Tick += delegate { Animate(); };
        animTimer.Start();
        ResumeLayout(false);
        Log("安装器启动，日志：" + logPath);
    }

    int S(int v) { return (int)Math.Round(v * scale); }
    Rectangle S(Rectangle r) { return new Rectangle(S(r.X), S(r.Y), S(r.Width), S(r.Height)); }

    static Image LoadImage(string name)
    {
        try
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                using (var img = Image.FromStream(s)) return new Bitmap(img);
            }
        }
        catch { return null; }
    }

    // ---------- 无边框窗口：任务栏最小化、Win11 圆角、拖动 ----------

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x00020000 | 0x00080000; // WS_MINIMIZEBOX | WS_SYSMENU
            cp.ClassStyle |= 0x00020000;         // CS_DROPSHADOW
            return cp;
        }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { } // DWMWA_WINDOW_CORNER_PREFERENCE = 圆角
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x84 && (int)m.Result == 1) // WM_NCHITTEST 命中空白处 → 当作标题栏，可拖动
        {
            long lp = m.LParam.ToInt64();
            var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            if (TitleButtonAt(pt) < 0) m.Result = (IntPtr)2; // HTCAPTION
        }
    }

    // ---------- 控件 ----------

    void BuildControls()
    {
        var frame = new Rectangle(CardBox.X + 336, CardBox.Y + 116, CardBox.Width - 372, 324);
        logFrame = new RoundPanel { Bounds = S(frame), BackColor = SoftFill, Radius = S(12) };
        logBox = new TextBox
        {
            Location = new Point(S(12), S(10)), Size = new Size(S(frame.Width - 18), S(frame.Height - 20)),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
            BackColor = SoftFill, ForeColor = Muted, Font = smallFont, TabStop = false
        };
        logFrame.Controls.Add(logBox);

        donePanel = new RoundPanel { Bounds = S(frame), BackColor = Color.FromArgb(236, 244, 255), Radius = S(12), Visible = false };
        donePanel.Controls.Add(new Label
        {
            Text = "① 在 DSH 左侧栏点「Aurabot」，打开设置向导\r\n\r\n" +
                   "② 填写机器人 QQ（小号）和主人 QQ（你自己）\r\n\r\n" +
                   "③ 网关选 SnowLuma：自己下载解压，并在面板里同意协议\r\n\r\n" +
                   "④ 先「试运行」确认没问题，再切到正式运行",
            Font = bodyFont, ForeColor = Ink, BackColor = Color.Transparent,
            Location = new Point(S(16), S(16)), Size = new Size(S(frame.Width - 28), S(240))
        });
        AddLink("下载 SnowLuma", 16, delegate { OpenUrl(Config.SnowLumaUrl); });
        AddLink("打开安装教程", 118, delegate { OpenTutorial(); });
        AddLink("查看安装日志", 210, delegate { logFrame.Visible = true; donePanel.Visible = false; });

        int by = CardBox.Bottom - 28 - 42;
        tutorialButton = new PillButton { Text = "查看教程", Bounds = S(new Rectangle(CardBox.X + 36, by, 104, 42)), Font = bodyFont };
        tutorialButton.Click += delegate { OpenTutorial(); };
        closeButton = new PillButton { Text = "关闭", Bounds = S(new Rectangle(CardBox.Right - 36 - 156 - 12 - 92, by, 92, 42)), Font = bodyFont };
        closeButton.Click += delegate { Close(); };
        mainButton = new PillButton { Text = "开始安装", Primary = true, Bounds = S(new Rectangle(CardBox.Right - 36 - 156, by, 156, 42)), Font = stepFont };
        mainButton.Click += delegate { OnMainButton(); };

        Controls.AddRange(new Control[] { logFrame, donePanel, tutorialButton, closeButton, mainButton });
        AcceptButton = mainButton;
        ActiveControl = mainButton;
    }

    void AddLink(string text, int x, EventHandler onClick)
    {
        var link = new LinkLabel
        {
            Text = text, AutoSize = true, Font = bodyFont, BackColor = Color.Transparent,
            LinkColor = Accent, ActiveLinkColor = Shade(Accent, -40), LinkBehavior = LinkBehavior.HoverUnderline,
            Location = new Point(S(x), S(280))
        };
        link.Click += onClick;
        donePanel.Controls.Add(link);
    }

    // ---------- 静态背景（只画一次，缓存成位图） ----------

    Bitmap RenderBackground()
    {
        var bmp = new Bitmap(ClientSize.Width, ClientSize.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var all = new Rectangle(Point.Empty, ClientSize);
            using (var b = new LinearGradientBrush(all, BgTop, BgBottom, 60f)) g.FillRectangle(b, all);

            // 柔和的装饰圆
            Circle(g, Color.FromArgb(110, 255, 255, 255), 196, H - 190, 160);
            Circle(g, Color.FromArgb(40, Accent), 352, 150, 18);
            Circle(g, Color.FromArgb(50, OkColor), 48, 200, 10);
            Circle(g, Color.FromArgb(36, Accent), W - 70, 22, 30);

            // 品牌
            using (var b = new SolidBrush(Accent)) using (var p = Gfx.Round(S(new Rectangle(26, 30, 14, 14)), S(4))) g.FillPath(b, p);
            using (var b = new SolidBrush(Ink)) g.DrawString("大肥鱼 Aurabot", brandFont, b, S(46), S(25));

            // 大肥鱼形象：站在卡片左边，裙摆落到窗口底边
            if (mascot != null)
            {
                int mh = 380, mw = mh * mascot.Width / mascot.Height;
                g.DrawImage(mascot, S(new Rectangle((CardBox.X - mw) / 2, H - mh, mw, mh)));
            }

            // 熊猫背景形象：从卡片右上角后面探出头，下半身被卡片挡住
            if (panda != null)
            {
                int ph = 110, pw = ph * panda.Width / panda.Height;
                g.DrawImage(panda, S(new Rectangle(CardBox.Right - 76 - pw, CardBox.Y - 58, pw, ph)));
            }

            // 白色卡片 + 柔和阴影
            var card = S(CardBox);
            for (int i = 8; i >= 1; i--)
            {
                var r = card;
                r.Inflate(S(i), S(i));
                r.Offset(0, S(4));
                using (var b = new SolidBrush(Color.FromArgb(6, 40, 60, 130))) using (var p = Gfx.Round(r, S(20 + i))) g.FillPath(b, p);
            }
            using (var p = Gfx.Round(card, S(20))) g.FillPath(Brushes.White, p);

            TextRenderer.DrawText(g, "一键安装", titleFont, S(new Point(CardBox.X + 36, CardBox.Y + 32)), Ink, Color.White);
            TextRenderer.DrawText(g, "自动安装 DSH 桌面版和大肥鱼插件，全程只需点几下", subFont, S(new Point(CardBox.X + 38, CardBox.Y + 72)), Muted, Color.White);
        }
        return bmp;
    }

    Point S(Point p) { return new Point(S(p.X), S(p.Y)); }

    void Circle(Graphics g, Color c, int cx, int cy, int r)
    {
        using (var b = new SolidBrush(c)) g.FillEllipse(b, S(cx - r), S(cy - r), S(r * 2), S(r * 2));
    }

    // ---------- 动态内容 ----------

    static readonly Rectangle LiveBox = new Rectangle(CardBox.X + 16, CardBox.Y + 100, 312, 344);

    int StepY(int i) { return CardBox.Y + 126 + i * 66; }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (background == null) background = RenderBackground();
        var g = e.Graphics;
        g.DrawImageUnscaled(background, 0, 0);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        PaintTitleButtons(g);

        int x = CardBox.X + 36;
        using (var pen = new Pen(Line, S(2)))
            for (int i = 0; i < 3; i++)
                g.DrawLine(pen, S(x + 14), S(StepY(i) + 32), S(x + 14), S(StepY(i + 1) - 4));
        for (int i = 0; i < 4; i++) PaintStep(g, i, x, StepY(i));

        TextRenderer.DrawText(g, statusText, bodyFont, S(new Rectangle(x, CardBox.Y + 402, 276, 20)), statusColor, Color.White,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        PaintProgress(g, S(new Rectangle(x, CardBox.Y + 430, 276, 6)));
    }

    void PaintStep(Graphics g, int i, int x, int y)
    {
        var dot = S(new Rectangle(x, y, 28, 28));
        var st = states[i];
        Color fill = st == StepState.Running ? Accent : st == StepState.Done ? OkColor : st == StepState.Failed ? BadColor : Color.White;
        if (st == StepState.Running)
        {
            float t = (float)(0.5 + 0.5 * Math.Sin(phase));
            var halo = dot;
            halo.Inflate(S(3) + (int)(S(4) * t), S(3) + (int)(S(4) * t));
            using (var b = new SolidBrush(Color.FromArgb((int)(70 - 50 * t), Accent))) g.FillEllipse(b, halo);
        }
        if (st == StepState.Pending || st == StepState.Skipped)
        {
            using (var b = new SolidBrush(st == StepState.Skipped ? SoftFill : Color.White)) g.FillEllipse(b, dot);
            using (var pen = new Pen(Line, S(2))) g.DrawEllipse(pen, dot);
        }
        else using (var b = new SolidBrush(fill)) g.FillEllipse(b, dot);

        using (var pen = new Pen(Color.White, S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            float cx = dot.X + dot.Width / 2f, cy = dot.Y + dot.Height / 2f, u = S(1);
            if (st == StepState.Done)
                g.DrawLines(pen, new[] { new PointF(cx - 6 * u, cy), new PointF(cx - 2 * u, cy + 4 * u), new PointF(cx + 6 * u, cy - 4 * u) });
            else if (st == StepState.Failed)
            {
                g.DrawLine(pen, cx - 5 * u, cy - 5 * u, cx + 5 * u, cy + 5 * u);
                g.DrawLine(pen, cx + 5 * u, cy - 5 * u, cx - 5 * u, cy + 5 * u);
            }
            else
            {
                Color numColor = st == StepState.Running ? Color.White : Muted;
                string num = st == StepState.Skipped ? "–" : (i + 1).ToString();
                TextRenderer.DrawText(g, num, numFont, dot, numColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        Color titleColor = st == StepState.Pending || st == StepState.Skipped ? Muted : Ink;
        int tx = x + 42, tw = x + 276 - tx;
        TextRenderer.DrawText(g, StepTitles[i], stepFont, S(new Rectangle(tx, y - 1, tw, 22)), titleColor, Color.White, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, details[i], smallFont, S(new Rectangle(tx, y + 21, tw, 18)), st == StepState.Failed ? BadColor : Muted, Color.White,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    void PaintProgress(Graphics g, Rectangle r)
    {
        using (var b = new SolidBrush(Color.FromArgb(230, 236, 248))) using (var p = Gfx.Round(r, r.Height / 2)) g.FillPath(b, p);
        Rectangle bar;
        if (marquee)
        {
            int seg = r.Width / 4;
            int pos = (int)((phase * 40) % (r.Width + seg)) - seg;
            bar = Rectangle.Intersect(r, new Rectangle(r.X + pos, r.Y, seg, r.Height));
        }
        else bar = new Rectangle(r.X, r.Y, r.Width * progressValue / 100, r.Height);
        if (bar.Width < r.Height) { if (bar.Width <= 0) return; bar.Width = Math.Max(bar.Width, r.Height); }
        using (var b = new LinearGradientBrush(r, Shade(Accent, 20), finished ? OkColor : Accent, 0f))
        using (var p = Gfx.Round(bar, r.Height / 2)) g.FillPath(b, p);
    }

    // ---------- 右上角的最小化 / 关闭 ----------

    Rectangle TitleButtonRect(int i) { return S(new Rectangle(W - 8 - 36 * (2 - i), 0, 36, 24)); }

    int TitleButtonAt(Point pt)
    {
        for (int i = 0; i < 2; i++) if (TitleButtonRect(i).Contains(pt)) return i;
        return -1;
    }

    void PaintTitleButtons(Graphics g)
    {
        for (int i = 0; i < 2; i++)
        {
            var r = TitleButtonRect(i);
            bool hot = hoverTitleButton == i;
            if (hot)
                using (var b = new SolidBrush(i == 1 ? BadColor : Color.FromArgb(60, 255, 255, 255)))
                using (var p = Gfx.Round(new Rectangle(r.X, r.Y - S(8), r.Width, r.Height + S(8)), S(8))) g.FillPath(b, p);
            using (var pen = new Pen(hot && i == 1 ? Color.White : Ink, S(1)))
            {
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, u = S(1);
                if (i == 0) g.DrawLine(pen, cx - 5 * u, cy, cx + 5 * u, cy);
                else { g.DrawLine(pen, cx - 4.5f * u, cy - 4.5f * u, cx + 4.5f * u, cy + 4.5f * u); g.DrawLine(pen, cx + 4.5f * u, cy - 4.5f * u, cx - 4.5f * u, cy + 4.5f * u); }
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = TitleButtonAt(e.Location);
        if (hit != hoverTitleButton) { hoverTitleButton = hit; Invalidate(S(new Rectangle(W - 90, 0, 90, 30))); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverTitleButton >= 0) { hoverTitleButton = -1; Invalidate(S(new Rectangle(W - 90, 0, 90, 30))); }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        int hit = TitleButtonAt(e.Location);
        if (hit == 0) WindowState = FormWindowState.Minimized;
        else if (hit == 1) Close();
    }

    void Animate()
    {
        if (!marquee && Array.IndexOf(states, StepState.Running) < 0) return;
        phase += 0.12f;
        Invalidate(S(LiveBox));
    }

    // ---------- 按钮与流程控制 ----------

    void OnMainButton()
    {
        if (finished) { Close(); return; }
        if (busy) return;
        for (int i = 0; i < 4; i++) { states[i] = StepState.Pending; details[i] = StepHints[i]; }
        logFrame.Visible = true;
        donePanel.Visible = false;
        busy = true;
        cancel = false;
        progressValue = 0;
        mainButton.Enabled = false;
        mainButton.Text = "安装中...";
        Invalidate(S(LiveBox));
        var t = new Thread(Work) { IsBackground = true };
        t.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (busy && MessageBox.Show(this, "正在安装，确定要中止并退出吗？", Config.Title,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        cancel = true;
        base.OnFormClosing(e);
    }

    void Work()
    {
        bool ok = false;
        string failMessage = null;
        try { ok = RunSteps(out failMessage); }
        catch (OperationCanceledException) { failMessage = "已取消。"; }
        catch (Exception ex)
        {
            Log("意外错误：" + ex);
            SetStep(currentStep, StepState.Failed, ex.Message);
            failMessage = "出错了：" + ex.Message;
        }
        Ui(delegate
        {
            busy = false;
            marquee = false;
            mainButton.Enabled = true;
            if (ok)
            {
                finished = true;
                progressValue = 100;
                SetStatus("全部完成！接下来在 DSH 里完成右边几步", OkColor);
                mainButton.Text = "完成";
                logFrame.Visible = false;
                donePanel.Visible = true;
            }
            else
            {
                SetStatus(failMessage ?? "安装没有完成。", BadColor);
                mainButton.Text = "重试";
            }
            Invalidate(S(LiveBox));
        });
    }

    // ---------- 界面辅助（可在后台线程调用） ----------

    void Ui(MethodInvoker action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(action); } catch (InvalidOperationException) { } }
        else action();
    }

    bool Ask(string question)
    {
        bool yes = false;
        MethodInvoker ask = delegate
        {
            yes = MessageBox.Show(this, question, Config.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        };
        if (InvokeRequired) Invoke(ask); else ask();
        Log(question.Replace("\r\n", " ") + " → " + (yes ? "是" : "否"));
        if (cancel) throw new OperationCanceledException();
        return yes;
    }

    void SetStep(int i, StepState state, string detail)
    {
        Ui(delegate
        {
            states[i] = state;
            if (detail != null) details[i] = detail;
            Invalidate(S(LiveBox));
        });
        if (detail != null && state != StepState.Running) Log("[第 " + (i + 1) + " 步] " + detail);
    }

    void SetStatus(string text, Color? color)
    {
        Ui(delegate { statusText = text; statusColor = color ?? Muted; Invalidate(S(LiveBox)); });
        Log(text);
    }

    void SetProgress(int pct)
    {
        Ui(delegate { marquee = false; progressValue = Math.Max(0, Math.Min(100, pct)); Invalidate(S(LiveBox)); });
    }

    void Marquee(bool on)
    {
        Ui(delegate { marquee = on; if (!on) progressValue = 0; Invalidate(S(LiveBox)); });
    }

    readonly object logLock = new object();

    void Log(string line)
    {
        string stamped = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line;
        lock (logLock) { if (logFile != null) { try { logFile.WriteLine(stamped); } catch { } } }
        Ui(delegate { if (logBox != null) logBox.AppendText(line + "\r\n"); });
    }

    void OpenTutorial()
    {
        string path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), Config.TutorialFile);
        if (!File.Exists(path)) { OpenUrl(Config.RepoUrl); return; }
        // 用默认浏览器打开网页版教程
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { OpenUrl(Config.RepoUrl); }
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    static string Mb(long bytes)
    {
        return bytes < 0 ? "未知大小" : (bytes / 1048576.0).ToString("0.0") + " MB";
    }

    static Color Shade(Color c, int d)
    {
        Func<int, int> f = v => Math.Max(0, Math.Min(255, v + d));
        return Color.FromArgb(c.A, f(c.R), f(c.G), f(c.B));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (animTimer != null) animTimer.Dispose();
            if (background != null) background.Dispose();
            if (logFile != null) { lock (logLock) { logFile.Dispose(); logFile = null; } }
        }
        base.Dispose(disposing);
    }
}

static class Gfx
{
    public static GraphicsPath Round(Rectangle r, int radius)
    {
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// 圆角按钮：Primary 为蓝色实心，否则为浅色描边
class PillButton : Control, IButtonControl
{
    static readonly Color Accent = Color.FromArgb(64, 110, 230);
    bool hover, down;
    public bool Primary;

    public PillButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        BackColor = Color.White;
    }

    public DialogResult DialogResult { get; set; }
    public void NotifyDefault(bool value) { }
    public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) PerformClick(); base.OnKeyUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill, border, text;
        if (Primary)
        {
            fill = !Enabled ? Color.FromArgb(166, 188, 240) : down ? Color.FromArgb(46, 88, 200) : hover ? Color.FromArgb(86, 130, 240) : Accent;
            border = fill;
            text = Color.White;
        }
        else
        {
            fill = down ? Color.FromArgb(228, 234, 248) : hover ? Color.FromArgb(242, 246, 254) : Color.White;
            border = Color.FromArgb(214, 222, 240);
            text = Enabled ? Color.FromArgb(30, 36, 64) : Color.FromArgb(160, 168, 190);
        }
        using (var p = Gfx.Round(r, Height / 2))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, p);
            using (var pen = new Pen(border)) g.DrawPath(pen, p);
        }
        TextRenderer.DrawText(g, Text, Font, r, text, fill, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

// 圆角底色面板（父容器背景为白色）
class RoundPanel : Panel
{
    public int Radius = 12;

    public RoundPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.White);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(BackColor)) using (var p = Gfx.Round(new Rectangle(0, 0, Width - 1, Height - 1), Radius)) g.FillPath(b, p);
    }
}
