// RamLens user interface: themed header, cards, usage bar, menus, grids and the About window.
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("RamLens")]
[assembly: AssemblyDescription("Accurate RAM accounting for Windows")]
[assembly: AssemblyCompany("Dr. Mohammed Tawfik")]
[assembly: AssemblyProduct("RamLens")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Dr. Mohammed Tawfik - MIT License")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace RamLens
{
    static class AppInfo
    {
        public const string Version = "1.1.0";
        public static bool OpenAbout;
        public const string Email = "kmkhol01@gmail.com";
        public const string Repo = "https://github.com/kmkholm/RamLens";
        public const string Profile = "https://github.com/kmkholm";
        public const string Orcid = "https://orcid.org/0000-0002-1227-387X";
        public static string Developer { get { return L.T("د. محمد توفيق", "Dr. Mohammed Tawfik"); } }
        public static string Affiliation { get { return L.T("جامعة عجلون الوطنية، الأردن", "Ajloun National University, Jordan"); } }
    }

    static class Theme
    {
        public static float Sc = 1f;
        public static readonly Color Header = Hex(0x16213E), Header2 = Hex(0x1F3A7A), HeaderSub = Hex(0xB8C2DC);
        public static readonly Color Back = Hex(0xF3F5FA), Card = Color.White, Border = Hex(0xE1E6EF);
        public static readonly Color Text = Hex(0x1F2937), Muted = Hex(0x6B7280), Accent = Hex(0x2F6FED), AccentSoft = Hex(0xE4EDFD);
        public static readonly Color GridHeader = Hex(0xEEF2F8), GridLine = Hex(0xE9EDF3), GridAlt = Hex(0xFAFBFD);
        public static readonly Color Proc = Hex(0xE4572E), Compr = Hex(0x8E44AD), VM = Hex(0xF39C12), Kernel = Hex(0x34495E),
            Unattr = Hex(0xE9B949), Modified = Hex(0x9AA5B1), Standby = Hex(0x4FA3E0), Free = Hex(0x3BB273), Warn = Hex(0xC0392B);

        public static Color Hex(int v) { return Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255); }
        public static Font UI(float size, FontStyle st = FontStyle.Regular) { return new Font("Segoe UI", size, st); }
        public static int S(float px) { return (int)(px * Sc); }

        public static Color KindColor(string k)
        {
            if (k == Kinds.App) return Hex(0x2F6FED);
            if (k == Kinds.Service) return Hex(0x0E9384);
            if (k == Kinds.System) return Hex(0x475467);
            if (k == Kinds.VM) return Hex(0xD97706);
            if (k == Kinds.Hidden) return Hex(0x7A5AF8);
            return Hex(0x667085);
        }

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // Icons drawn from the Segoe MDL2 Assets font that ships with Windows 10/11.
    static class Glyph
    {
        static readonly bool Has = new System.Drawing.Text.InstalledFontCollection().Families.Any(f => f.Name == "Segoe MDL2 Assets");
        public const int Search = 0xE721, Refresh = 0xE72C, Save = 0xE74E, Doc = 0xE8A5, Group = 0xE8FD, Timer = 0xE916,
            Globe = 0xE774, Shield = 0xEA18, Info = 0xE946, Folder = 0xE838, Copy = 0xE8C8, Cancel = 0xE711, Exit = 0xE7E8,
            Chart = 0xE9D9, Code = 0xE943, Help = 0xE897, Mail = 0xE715, Settings = 0xE713;

        public static Image Make(int code, Color c, int px)
        {
            if (!Has) return null;
            var bmp = new Bitmap(px, px);
            using (var g = Graphics.FromImage(bmp))
            using (var f = new Font("Segoe MDL2 Assets", px * 0.72f, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(((char)code).ToString(), f, b, new RectangleF(0, 1, px, px), sf);
            }
            return bmp;
        }
    }

    class MenuColors : ProfessionalColorTable
    {
        static readonly Color W = Color.White, Soft = Theme.AccentSoft, Pressed = Theme.Hex(0xCFDDFB), Margin = Theme.Hex(0xF5F7FB);
        public override Color MenuItemSelected { get { return Soft; } }
        public override Color MenuItemBorder { get { return Theme.Hex(0xB9CCF7); } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemSelectedGradientBegin { get { return Soft; } }
        public override Color MenuItemSelectedGradientEnd { get { return Soft; } }
        public override Color MenuItemPressedGradientBegin { get { return Pressed; } }
        public override Color MenuItemPressedGradientMiddle { get { return Pressed; } }
        public override Color MenuItemPressedGradientEnd { get { return Pressed; } }
        public override Color MenuStripGradientBegin { get { return W; } }
        public override Color MenuStripGradientEnd { get { return W; } }
        public override Color ToolStripGradientBegin { get { return W; } }
        public override Color ToolStripGradientMiddle { get { return W; } }
        public override Color ToolStripGradientEnd { get { return W; } }
        public override Color ToolStripBorder { get { return Theme.Border; } }
        public override Color ToolStripDropDownBackground { get { return W; } }
        public override Color ImageMarginGradientBegin { get { return Margin; } }
        public override Color ImageMarginGradientMiddle { get { return Margin; } }
        public override Color ImageMarginGradientEnd { get { return Margin; } }
        public override Color ButtonSelectedHighlight { get { return Soft; } }
        public override Color ButtonSelectedGradientBegin { get { return Soft; } }
        public override Color ButtonSelectedGradientMiddle { get { return Soft; } }
        public override Color ButtonSelectedGradientEnd { get { return Soft; } }
        public override Color ButtonSelectedBorder { get { return Theme.Hex(0xB9CCF7); } }
        public override Color ButtonCheckedGradientBegin { get { return Soft; } }
        public override Color ButtonCheckedGradientMiddle { get { return Soft; } }
        public override Color ButtonCheckedGradientEnd { get { return Soft; } }
        public override Color ButtonCheckedHighlight { get { return Soft; } }
        public override Color ButtonPressedGradientBegin { get { return Pressed; } }
        public override Color ButtonPressedGradientMiddle { get { return Pressed; } }
        public override Color ButtonPressedGradientEnd { get { return Pressed; } }
        public override Color ButtonPressedBorder { get { return Theme.Accent; } }
        public override Color CheckBackground { get { return Soft; } }
        public override Color CheckSelectedBackground { get { return Pressed; } }
        public override Color CheckPressedBackground { get { return Pressed; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return W; } }
        public override Color StatusStripGradientBegin { get { return W; } }
        public override Color StatusStripGradientEnd { get { return W; } }
    }

    class HeaderPanel : Panel
    {
        public string Usage = "", Pct = "";
        public double Fraction;
        public bool Meter = true;
        public Image Logo;
        public HeaderPanel() { DoubleBuffered = true; ResizeRedraw = true; }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 4 || Height < 4) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new LinearGradientBrush(ClientRectangle, Theme.Header, Theme.Header2, LinearGradientMode.Horizontal))
                g.FillRectangle(br, ClientRectangle);
            bool rtl = !L.En;
            int pad = Theme.S(18), logo = Theme.S(40);
            Func<int, int, int> X = (x, w) => rtl ? Width - x - w : x;
            if (Logo != null) g.DrawImage(Logo, X(pad, logo), (Height - logo) / 2, logo, logo);
            int tx = pad + logo + Theme.S(12);
            var tf = TextFormatFlags.NoPadding | (rtl ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left);
            using (var f1 = Theme.UI(16f, FontStyle.Bold))
                TextRenderer.DrawText(g, "RamLens", f1, new Rectangle(X(tx, Theme.S(400)), Theme.S(10), Theme.S(400), Theme.S(32)), Color.White, tf);
            using (var f2 = Theme.UI(9.5f))
                TextRenderer.DrawText(g, L.T("من يستهلك الرام فعلاً  •  تطوير ", "Who really uses your RAM  •  by ") + AppInfo.Developer,
                    f2, new Rectangle(X(tx, Theme.S(460)), Theme.S(42), Theme.S(460), Theme.S(22)), Theme.HeaderSub, tf);

            // usage meter on the opposite side
            int mw = Theme.S(300), mx = Width - pad - mw;
            var ef = TextFormatFlags.NoPadding | (rtl ? TextFormatFlags.Left : TextFormatFlags.Right);
            int ex = rtl ? pad : mx;
            using (var f3 = Theme.UI(11f, FontStyle.Bold))
                TextRenderer.DrawText(g, Usage, f3, new Rectangle(ex, Theme.S(12), mw, Theme.S(24)), Color.White,
                    ef | (rtl ? TextFormatFlags.RightToLeft : 0));
            if (!Meter) return;
            var track = new Rectangle(ex, Theme.S(44), mw - Theme.S(48), Theme.S(10));
            if (rtl) track.X = ex + Theme.S(48);
            using (var p = Theme.Round(track, track.Height / 2)) using (var b = new SolidBrush(Color.FromArgb(60, 255, 255, 255))) g.FillPath(b, p);
            int fw = (int)(track.Width * Math.Max(0, Math.Min(1, Fraction)));
            if (fw > track.Height)
            {
                var fill = new Rectangle(rtl ? track.Right - fw : track.X, track.Y, fw, track.Height);
                Color c = Fraction < 0.6 ? Theme.Free : Fraction < 0.85 ? Theme.VM : Theme.Proc;
                using (var p = Theme.Round(fill, fill.Height / 2)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
            }
            using (var f4 = Theme.UI(9.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, Pct, f4, new Rectangle(rtl ? ex : track.Right + Theme.S(6), Theme.S(38), Theme.S(42), Theme.S(22)),
                    Color.White, TextFormatFlags.NoPadding | (rtl ? TextFormatFlags.Left : TextFormatFlags.Right) | TextFormatFlags.VerticalCenter);
        }
    }

    class Card : Control
    {
        public string Title = "", Value = "", Sub = "";
        public Color AccentColor = Theme.Accent;
        public bool Alert;
        public Card() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Theme.Back; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Back);
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var p = Theme.Round(r, Theme.S(8)))
            {
                using (var b = new SolidBrush(Alert ? Theme.Hex(0xFFF7E0) : Theme.Card)) g.FillPath(b, p);
                using (var pen = new Pen(Alert ? Theme.Unattr : Theme.Border)) g.DrawPath(pen, p);
            }
            bool rtl = !L.En;
            int stripe = Theme.S(4), pad = Theme.S(14);
            using (var b = new SolidBrush(AccentColor))
            using (var p = Theme.Round(new Rectangle(rtl ? r.Right - stripe - Theme.S(6) : r.X + Theme.S(6), r.Y + Theme.S(10), stripe, r.Height - Theme.S(20)), stripe / 2))
                g.FillPath(b, p);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            var al = rtl ? TextFormatFlags.Right : TextFormatFlags.Left;
            int w = Width - 2 * pad - Theme.S(6);
            int x = rtl ? pad : pad + Theme.S(6);
            using (var f = Theme.UI(9f)) TextRenderer.DrawText(g, Title, f, new Rectangle(x, Theme.S(8), w, Theme.S(20)), Theme.Muted, flags | al | (rtl ? TextFormatFlags.RightToLeft : 0));
            using (var f = Theme.UI(15f, FontStyle.Bold)) TextRenderer.DrawText(g, Value, f, new Rectangle(x, Theme.S(26), w, Theme.S(30)), Theme.Text, flags | al);
            using (var f = Theme.UI(8.5f)) TextRenderer.DrawText(g, Sub, f, new Rectangle(x, Height - Theme.S(24), w, Theme.S(18)), Theme.Muted, flags | al | (rtl ? TextFormatFlags.RightToLeft : 0));
        }
    }

    class BarPanel : Control
    {
        public List<Tuple<string, long, Color>> Segs = new List<Tuple<string, long, Color>>();
        readonly List<Tuple<Rectangle, string>> hits = new List<Tuple<Rectangle, string>>();
        readonly ToolTip tip = new ToolTip();
        string lastTip = "";
        public BarPanel() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Theme.Back; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Back);
            hits.Clear();
            long total = Segs.Sum(x => x.Item2);
            if (total <= 0 || Width < 10) return;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            bool rtl = !L.En;
            using (var clip = Theme.Round(r, Height / 2))
            {
                g.SetClip(clip);
                float x0 = 0;
                foreach (var s in Segs)
                {
                    float sw = (float)(r.Width * s.Item2 / (double)total);
                    var seg = new RectangleF(rtl ? r.Right - x0 - sw : x0, 0, sw + 1, Height);
                    using (var b = new SolidBrush(s.Item3)) g.FillRectangle(b, seg);
                    double pct = 100.0 * s.Item2 / total;
                    if (sw > Theme.S(46))
                        using (var f = Theme.UI(8.5f, FontStyle.Bold))
                            TextRenderer.DrawText(g, pct.ToString("0") + "%", f, Rectangle.Round(seg), Color.White,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    hits.Add(Tuple.Create(Rectangle.Round(seg), string.Format("{0}: {1} ({2:0.0}%)", s.Item1, Fmt.GB(s.Item2), pct)));
                    x0 += sw;
                }
                g.ResetClip();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var h = hits.FirstOrDefault(t => t.Item1.Contains(e.Location));
            string s = h == null ? "" : h.Item2;
            if (s != lastTip) { lastTip = s; tip.SetToolTip(this, s); }
            base.OnMouseMove(e);
        }
    }

    class MainForm : Form
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        DataGridView gridProc, gridPool;
        ToolStripTextBox txtSearch;
        ToolStripButton btnGroup, btnAuto;
        ToolStripMenuItem miGroup, miAuto;
        readonly List<ToolStripMenuItem> miIntervals = new List<ToolStripMenuItem>();
        RichTextBox rtbDiag;
        HeaderPanel header;
        BarPanel bar;
        FlowLayoutPanel legend;
        TabControl tabs;
        ToolStripStatusLabel lblStatus, lblCount;
        System.Windows.Forms.Timer timer;
        Snapshot last;
        bool busy, grouped = true;
        int tick, intervalSec = 5;
        double maxPriv = 1, maxPool = 1;
        string procSort = "Private DESC", poolSort = "Total DESC";
        readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();
        readonly bool isAdmin;
        readonly Image logo;

        static readonly string[,] ProcCols = {
            { "Name", L.T("البرنامج", "Program") }, { "Pid", "PID" }, { "Count", L.T("عدد العمليات", "Processes") }, { "Kind", L.T("النوع", "Type") },
            { "Private", L.T("الذاكرة الخاصة (MB)", "Private memory (MB)") }, { "Shared", L.T("المشتركة (MB)", "Shared (MB)") },
            { "WS", L.T("مجموعة العمل (MB)", "Working set (MB)") }, { "Commit", L.T("الالتزام الخاص (MB)", "Private commit (MB)") },
            { "Peak", L.T("الذروة (MB)", "Peak (MB)") }, { "PagedQ", "Paged pool (KB)" }, { "NonPagedQ", "NonPaged pool (KB)" },
            { "Handles", L.T("المقابض", "Handles") }, { "Threads", L.T("الخيوط", "Threads") }, { "Session", L.T("الجلسة", "Session") },
            { "Services", L.T("الخدمات المستضافة", "Hosted services") }, { "Path", L.T("المسار", "Path") } };

        public MainForm()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Sc = g.DpiX / 96f;
            ToolStripManager.Renderer = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false };
            isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); logo = new Icon(Icon, 48, 48).ToBitmap(); } catch { }

            Text = L.T("RamLens — من يستهلك الرام فعلاً", "RamLens — who really uses your RAM") + (isAdmin ? L.T("  [مسؤول]", "  [Administrator]") : "");
            Font = Theme.UI(9.5f);
            ForeColor = Theme.Text;
            BackColor = Theme.Back;
            RightToLeft = L.Rtl;
            RightToLeftLayout = !L.En;
            Size = new Size(Theme.S(1340), Theme.S(900));
            MinimumSize = new Size(Theme.S(900), Theme.S(600));
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            var menu = BuildMenu();
            header = new HeaderPanel { Dock = DockStyle.Top, Height = Theme.S(72), Logo = logo };

            // ---------- summary cards, usage bar, legend ----------
            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(Theme.S(12), Theme.S(10), Theme.S(12), 0), BackColor = Theme.Back };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var cardGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, Height = Theme.S(172), Margin = new Padding(0) };
            for (int i = 0; i < 5; i++) cardGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            cardGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(86)));
            cardGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(86)));
            var defs = new[] {
                Tuple.Create("inst", Theme.Hex(0x64748B)), Tuple.Create("inuse", Theme.Proc), Tuple.Create("standby", Theme.Standby),
                Tuple.Create("free", Theme.Free), Tuple.Create("commit", Theme.Accent), Tuple.Create("proc", Theme.Proc),
                Tuple.Create("compr", Theme.Compr), Tuple.Create("kernel", Theme.Kernel), Tuple.Create("unattr", Theme.Unattr),
                Tuple.Create("modified", Theme.Modified) };
            foreach (var d in defs)
            {
                var c = new Card { Dock = DockStyle.Fill, AccentColor = d.Item2, Margin = new Padding(Theme.S(4)) };
                cards[d.Item1] = c;
                cardGrid.Controls.Add(c);
            }
            bar = new BarPanel { Dock = DockStyle.Fill, Height = Theme.S(26), Margin = new Padding(Theme.S(4), Theme.S(10), Theme.S(4), Theme.S(4)) };
            legend = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 0, 0, Theme.S(6)) };
            top.Controls.Add(cardGrid, 0, 0);
            top.Controls.Add(bar, 0, 1);
            top.Controls.Add(legend, 0, 2);

            // ---------- tabs ----------
            tabs = new TabControl { Dock = DockStyle.Fill, RightToLeftLayout = !L.En, DrawMode = TabDrawMode.OwnerDrawFixed, SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(Theme.S(250), Theme.S(34)), Padding = new Point(Theme.S(12), Theme.S(4)) };
            tabs.DrawItem += Tabs_DrawItem;
            var tpProc = new TabPage(L.T("العمليات (الكل، بما فيها المخفية)", "Processes (all, including hidden)")) { BackColor = Color.White };
            var tpPool = new TabPage(L.T("ذاكرة النواة (Pool tags)", "Kernel memory (Pool tags)")) { BackColor = Color.White };
            var tpDiag = new TabPage(L.T("التشخيص والشرح", "Diagnosis & explanation")) { BackColor = Color.White };
            tabs.TabPages.AddRange(new[] { tpProc, tpPool, tpDiag });

            gridProc = MakeGrid();
            gridProc.CellFormatting += GridProc_CellFormatting;
            gridProc.CellPainting += delegate(object o, DataGridViewCellPaintingEventArgs e) { DataBar(gridProc, "Private", e, Theme.Proc, maxPriv); };
            gridProc.ContextMenuStrip = BuildContextMenu();
            gridProc.CellMouseDown += delegate(object o, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0) gridProc.CurrentCell = gridProc.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            };
            tpProc.Controls.Add(gridProc);
            tpProc.Controls.Add(BuildToolbar());

            gridPool = MakeGrid();
            gridPool.CellPainting += delegate(object o, DataGridViewCellPaintingEventArgs e) { DataBar(gridPool, "Total", e, Theme.Kernel, maxPool); };
            gridPool.CellDoubleClick += delegate(object o, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0) FindDriver(Convert.ToString(gridPool.Rows[e.RowIndex].Cells["Tag"].Value));
            };
            var poolHint = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = Theme.S(48), Padding = new Padding(Theme.S(10), Theme.S(6), Theme.S(10), Theme.S(6)),
                BackColor = Theme.AccentSoft, ForeColor = Theme.Text,
                Text = L.T("هذه ذاكرة يحجزها ويندوز والتعريفات داخل النواة ولا تظهر لأي عملية في مدير المهام. الوسم (Tag) يدل على المكوّن. انقر مرتين على أي وسم ليبحث البرنامج في ملفات التعريفات (.sys) عن المسؤول عنه.",
                           "Memory reserved by Windows and drivers inside the kernel. No process shows it in Task Manager. The tag identifies the component; double-click a tag to search driver files (.sys) for its owner.")
            };
            tpPool.Controls.Add(gridPool);
            tpPool.Controls.Add(poolHint);

            rtbDiag = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White, BorderStyle = BorderStyle.None, Font = Theme.UI(11f), RightToLeft = L.Rtl };
            var diagWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(18)), BackColor = Color.White };
            diagWrap.Controls.Add(rtbDiag);
            tpDiag.Controls.Add(diagWrap);

            // ---------- status bar ----------
            var status = new StatusStrip { SizingGrip = true, BackColor = Color.White };
            lblStatus = new ToolStripStatusLabel(L.T("جارٍ القراءة...", "Reading...")) { Spring = true, TextAlign = L.En ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight };
            lblCount = new ToolStripStatusLabel("") { ForeColor = Theme.Muted };
            var lblDev = new ToolStripStatusLabel(L.T("المطوّر: ", "Developer: ") + AppInfo.Developer) { IsLink = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Header2 };
            lblDev.Click += delegate { ShowAbout(); };
            status.Items.AddRange(new ToolStripItem[] { lblStatus, lblCount, new ToolStripSeparator(), lblDev });

            Controls.Add(tabs);
            Controls.Add(top);
            Controls.Add(header);
            Controls.Add(menu);
            Controls.Add(status);
            MainMenuStrip = menu;

            timer = new System.Windows.Forms.Timer { Interval = intervalSec * 1000 };
            timer.Tick += delegate { if (btnAuto.Checked) { tick++; Collect(tick % Math.Max(1, 30 / intervalSec) == 0); } };
            Shown += delegate { Collect(true); timer.Start(); if (AppInfo.OpenAbout) BeginInvoke((MethodInvoker)ShowAbout); };
        }

        // ---------------- menus and toolbar ----------------
        ToolStripMenuItem Item(string text, int glyph, EventHandler h, Keys keys = Keys.None, Color? color = null)
        {
            var m = new ToolStripMenuItem(text, Glyph.Make(glyph, color ?? Theme.Header2, Theme.S(16)), h);
            if (keys != Keys.None) m.ShortcutKeys = keys;
            return m;
        }

        MenuStrip BuildMenu()
        {
            var menu = new MenuStrip { BackColor = Color.White, Padding = new Padding(Theme.S(6), Theme.S(3), 0, Theme.S(3)), RightToLeft = L.Rtl };
            var file = new ToolStripMenuItem(L.T("ملف", "File"));
            file.DropDownItems.Add(Item(L.T("حفظ تقرير نصّي...", "Save text report..."), Glyph.Save, delegate { SaveReport(); }, Keys.Control | Keys.S));
            file.DropDownItems.Add(Item(L.T("تصدير الجدول CSV...", "Export table to CSV..."), Glyph.Doc, delegate { ExportCsv(); }, Keys.Control | Keys.E));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item(L.T("خروج", "Exit"), Glyph.Exit, delegate { Close(); }, Keys.Alt | Keys.F4));

            var view = new ToolStripMenuItem(L.T("عرض", "View"));
            view.DropDownItems.Add(Item(L.T("تحديث الآن", "Refresh now"), Glyph.Refresh, delegate { Collect(true); }, Keys.F5));
            miGroup = Item(L.T("تجميع حسب البرنامج", "Group by program"), Glyph.Group, delegate { SetGrouped(!grouped); }, Keys.Control | Keys.G);
            miGroup.Checked = grouped;
            view.DropDownItems.Add(miGroup);
            miAuto = Item(L.T("تحديث تلقائي", "Auto-refresh"), Glyph.Timer, delegate { SetAuto(!btnAuto.Checked); });
            miAuto.Checked = true;
            view.DropDownItems.Add(miAuto);
            var interval = new ToolStripMenuItem(L.T("فترة التحديث", "Refresh interval"), Glyph.Make(Glyph.Settings, Theme.Header2, Theme.S(16)));
            foreach (int s in new[] { 2, 5, 10, 30 })
            {
                int sec = s;
                var mi = new ToolStripMenuItem(string.Format(L.T("{0} ثوانٍ", "{0} seconds"), sec)) { Checked = sec == intervalSec, Tag = sec };
                mi.Click += delegate { SetInterval(sec); };
                miIntervals.Add(mi);
                interval.DropDownItems.Add(mi);
            }
            view.DropDownItems.Add(interval);
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(Item(L.T("العمليات", "Processes"), Glyph.Chart, delegate { tabs.SelectedIndex = 0; }, Keys.Control | Keys.D1));
            view.DropDownItems.Add(Item(L.T("ذاكرة النواة", "Kernel memory"), Glyph.Code, delegate { tabs.SelectedIndex = 1; }, Keys.Control | Keys.D2));
            view.DropDownItems.Add(Item(L.T("التشخيص", "Diagnosis"), Glyph.Info, delegate { tabs.SelectedIndex = 2; }, Keys.Control | Keys.D3));
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(Item(L.En ? "العربية" : "English", Glyph.Globe, delegate { SwitchLanguage(); }, Keys.Control | Keys.L));

            var tools = new ToolStripMenuItem(L.T("أدوات", "Tools"));
            var admin = Item(L.T("إعادة التشغيل كمسؤول", "Restart as administrator"), Glyph.Shield, delegate { RunAsAdmin(); });
            admin.Enabled = !isAdmin;
            tools.DropDownItems.Add(admin);
            tools.DropDownItems.Add(new ToolStripSeparator());
            tools.DropDownItems.Add(Item(L.T("مدير المهام", "Task Manager"), Glyph.Chart, delegate { Launch("taskmgr.exe"); }));
            tools.DropDownItems.Add(Item(L.T("مراقب الموارد", "Resource Monitor"), Glyph.Chart, delegate { Launch("resmon.exe"); }));
            tools.DropDownItems.Add(Item(L.T("إعدادات الذاكرة الافتراضية", "Virtual memory settings"), Glyph.Settings, delegate { Launch("SystemPropertiesPerformance.exe"); }));

            var help = new ToolStripMenuItem(L.T("مساعدة", "Help"));
            help.DropDownItems.Add(Item(L.T("شرح المصطلحات والتشخيص", "Terms and diagnosis"), Glyph.Help, delegate { tabs.SelectedIndex = 2; }, Keys.F1));
            help.DropDownItems.Add(Item(L.T("صفحة المشروع على GitHub", "Project page on GitHub"), Glyph.Globe, delegate { Launch(AppInfo.Repo); }));
            help.DropDownItems.Add(Item(L.T("الإبلاغ عن مشكلة أو اقتراح", "Report a problem or suggestion"), Glyph.Mail, delegate { Launch(AppInfo.Repo + "/issues"); }));
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(Item(L.T("حول البرنامج والمطوّر", "About RamLens and the developer"), Glyph.Info, delegate { ShowAbout(); }));

            menu.Items.AddRange(new ToolStripItem[] { file, view, tools, help });
            return menu;
        }

        ToolStrip BuildToolbar()
        {
            var ts = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, BackColor = Color.White,
                Padding = new Padding(Theme.S(8), Theme.S(6), Theme.S(8), Theme.S(6)), ImageScalingSize = new Size(Theme.S(16), Theme.S(16)), RightToLeft = L.Rtl };
            ts.Items.Add(new ToolStripLabel(Glyph.Make(Glyph.Search, Theme.Muted, Theme.S(16))));
            txtSearch = new ToolStripTextBox { AutoSize = false, Width = Theme.S(240), BorderStyle = BorderStyle.FixedSingle };
            txtSearch.TextChanged += delegate { ApplyFilter(); };
            txtSearch.Control.HandleCreated += delegate { SendMessage(txtSearch.Control.Handle, 0x1501, (IntPtr)1, L.T("ابحث بالاسم أو المسار أو الخدمة...", "Search name, path or service...")); };
            ts.Items.Add(txtSearch);
            ts.Items.Add(new ToolStripSeparator());
            btnGroup = new ToolStripButton(L.T("تجميع حسب البرنامج", "Group by program"), Glyph.Make(Glyph.Group, Theme.Accent, Theme.S(16))) { Checked = grouped };
            btnGroup.Click += delegate { SetGrouped(!grouped); };
            btnAuto = new ToolStripButton(L.T("تحديث تلقائي", "Auto-refresh"), Glyph.Make(Glyph.Timer, Theme.Accent, Theme.S(16))) { Checked = true };
            btnAuto.Click += delegate { SetAuto(!btnAuto.Checked); };
            var btnRefresh = new ToolStripButton(L.T("تحديث", "Refresh"), Glyph.Make(Glyph.Refresh, Theme.Accent, Theme.S(16)));
            btnRefresh.Click += delegate { Collect(true); };
            ts.Items.AddRange(new ToolStripItem[] { btnGroup, btnAuto, btnRefresh, new ToolStripSeparator() });
            var btnCsv = new ToolStripButton(L.T("تصدير CSV", "Export CSV"), Glyph.Make(Glyph.Doc, Theme.Header2, Theme.S(16)));
            btnCsv.Click += delegate { ExportCsv(); };
            var btnRep = new ToolStripButton(L.T("تقرير", "Report"), Glyph.Make(Glyph.Save, Theme.Header2, Theme.S(16)));
            btnRep.Click += delegate { SaveReport(); };
            ts.Items.AddRange(new ToolStripItem[] { btnCsv, btnRep });
            var btnLang = new ToolStripButton(L.En ? "العربية" : "English", Glyph.Make(Glyph.Globe, Theme.Header2, Theme.S(16))) { Alignment = ToolStripItemAlignment.Right };
            btnLang.Click += delegate { SwitchLanguage(); };
            var btnAbout = new ToolStripButton(L.T("حول", "About"), Glyph.Make(Glyph.Info, Theme.Header2, Theme.S(16))) { Alignment = ToolStripItemAlignment.Right };
            btnAbout.Click += delegate { ShowAbout(); };
            ts.Items.Add(btnAbout);
            ts.Items.Add(btnLang);
            if (!isAdmin)
            {
                var btnAdmin = new ToolStripButton(L.T("كمسؤول", "As admin"), Glyph.Make(Glyph.Shield, Theme.VM, Theme.S(16)))
                { Alignment = ToolStripItemAlignment.Right, ToolTipText = L.T("إعادة التشغيل كمسؤول لعرض كل المسارات", "Restart elevated to show every path") };
                btnAdmin.Click += delegate { RunAsAdmin(); };
                ts.Items.Add(btnAdmin);
            }
            return ts;
        }

        ContextMenuStrip BuildContextMenu()
        {
            var m = new ContextMenuStrip { RightToLeft = L.Rtl, ShowImageMargin = true };
            m.Items.Add(Item(L.T("فتح موقع الملف", "Open file location"), Glyph.Folder, delegate
            {
                string p = SelectedValue("Path");
                if (p.Length > 0) Process.Start("explorer.exe", "/select,\"" + p + "\"");
            }));
            m.Items.Add(Item(L.T("نسخ المسار", "Copy path"), Glyph.Copy, delegate { string p = SelectedValue("Path"); if (p.Length > 0) Clipboard.SetText(p); }));
            m.Items.Add(Item(L.T("نسخ السطر", "Copy row"), Glyph.Copy, delegate
            {
                if (gridProc.CurrentRow == null) return;
                Clipboard.SetText(string.Join("\t", gridProc.CurrentRow.Cells.Cast<DataGridViewCell>().Select(c => Convert.ToString(c.FormattedValue))));
            }));
            m.Items.Add(Item(L.T("بحث في الإنترنت عن الاسم", "Search the web for this name"), Glyph.Globe, delegate
            {
                string n = SelectedValue("Name");
                if (n.Length > 0) Launch("https://www.google.com/search?q=" + Uri.EscapeDataString(n + " process"));
            }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(Item(L.T("إنهاء العملية...", "End process..."), Glyph.Cancel, delegate { KillSelected(); }, Keys.Delete, Theme.Warn));
            return m;
        }

        void SetGrouped(bool g)
        {
            grouped = g;
            btnGroup.Checked = miGroup.Checked = g;
            BindProcs();
        }

        void SetAuto(bool a)
        {
            btnAuto.Checked = miAuto.Checked = a;
        }

        void SetInterval(int sec)
        {
            intervalSec = sec;
            timer.Interval = sec * 1000;
            foreach (var mi in miIntervals) mi.Checked = (int)mi.Tag == sec;
            SetAuto(true);
        }

        void Launch(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }

        void ShowAbout()
        {
            using (var f = new AboutForm(logo, Icon)) f.ShowDialog(this);
        }

        // ---------------- grids ----------------
        DataGridView MakeGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowHeadersVisible = false,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                RightToLeft = L.Rtl, AllowUserToResizeRows = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Theme.GridLine, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, EnableHeadersVisualStyles = false
            };
            g.RowTemplate.Height = Theme.S(28);
            g.DefaultCellStyle.Font = Theme.UI(9.5f);
            g.DefaultCellStyle.ForeColor = Theme.Text;
            g.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            g.DefaultCellStyle.SelectionForeColor = Theme.Text;
            g.DefaultCellStyle.Padding = new Padding(Theme.S(6), 0, Theme.S(6), 0);
            g.AlternatingRowsDefaultCellStyle.BackColor = Theme.GridAlt;
            g.ColumnHeadersDefaultCellStyle.Font = Theme.UI(9f, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.BackColor = Theme.GridHeader;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Hex(0x475467);
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.GridHeader;
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(Theme.S(6), 0, Theme.S(6), 0);
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = Theme.S(46);
            typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(g, true, null);
            return g;
        }

        void DataBar(DataGridView grid, string col, DataGridViewCellPaintingEventArgs e, Color c, double max)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].Name != col || e.Value == null || e.Value == DBNull.Value) return;
            bool sel = (e.State & DataGridViewElementStates.Selected) != 0;
            e.PaintBackground(e.CellBounds, sel);
            double frac = max > 0 ? Math.Min(1, Convert.ToDouble(e.Value) / max) : 0;
            var r = e.CellBounds;
            int w = (int)((r.Width - Theme.S(10)) * frac);
            if (w > 0)
            {
                var b = new Rectangle(L.En ? r.X + Theme.S(5) : r.Right - Theme.S(5) - w, r.Y + Theme.S(6), w, r.Height - Theme.S(12));
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = Theme.Round(b, Math.Max(1, Theme.S(3)))) using (var br = new SolidBrush(Color.FromArgb(frac > 0.5 ? 110 : 70, c))) e.Graphics.FillPath(br, p);
            }
            e.PaintContent(e.CellBounds);
            e.Handled = true;
        }

        void Tabs_DrawItem(object sender, DrawItemEventArgs e)
        {
            var g = e.Graphics;
            bool sel = e.Index == tabs.SelectedIndex;
            var r = e.Bounds;
            // with RightToLeftLayout the GDI text is mirrored by Windows but GDI+ fills are not
            var fr = tabs.RightToLeftLayout ? new Rectangle(tabs.Width - r.Right, r.Y, r.Width, r.Height) : r;
            using (var b = new SolidBrush(sel ? Color.White : Theme.Back)) g.FillRectangle(b, fr);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, fr.X + Theme.S(8), fr.Bottom - Theme.S(3), fr.Width - Theme.S(16), Theme.S(3));
            using (var f = Theme.UI(9.5f, sel ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, tabs.TabPages[e.Index].Text, f, r, sel ? Theme.Accent : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | (L.En ? 0 : TextFormatFlags.RightToLeft));
        }

        string SelectedValue(string col)
        {
            if (gridProc.CurrentRow == null || !gridProc.Columns.Contains(col)) return "";
            return Convert.ToString(gridProc.CurrentRow.Cells[col].Value);
        }

        void KillSelected()
        {
            if (last == null || gridProc.CurrentRow == null) return;
            List<int> pids;
            string name = SelectedValue("Name");
            if (grouped) pids = last.Procs.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(p => p.Pid).ToList();
            else pids = new List<int> { Convert.ToInt32(gridProc.CurrentRow.Cells["Pid"].Value) };
            var kind = SelectedValue("Kind");
            string warn = (kind == Kinds.System || kind == Kinds.Service) ? L.T("\n\nتنبيه: هذه عملية نظام/خدمة، وإنهاؤها قد يسبّب عدم استقرار ويندوز.", "\n\nWarning: this is a system process/service; ending it may make Windows unstable.") : "";
            if (MessageBox.Show(string.Format(L.T("إنهاء {0} ({1} عملية)؟ ستفقد أي عمل غير محفوظ فيها.{2}", "End {0} ({1} processes)? Any unsaved work in it will be lost.{2}"), name, pids.Count, warn),
                L.T("تأكيد", "Confirm"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2, L.MbOpt) != DialogResult.Yes) return;
            int ok = 0;
            foreach (int pid in pids) { try { Process.GetProcessById(pid).Kill(); ok++; } catch { } }
            lblStatus.Text = string.Format(L.T("أُنهيت {0} من {1} عملية.", "Ended {0} of {1} processes."), ok, pids.Count);
            Collect(false);
        }

        // ---------------- data ----------------
        void Collect(bool services)
        {
            if (busy) return;
            busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Snapshot s = null; string err = null;
                try { s = Collector.Take(services); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        busy = false;
                        if (s == null) { lblStatus.Text = L.T("خطأ: ", "Error: ") + err; return; }
                        last = s;
                        ShowSnapshot();
                    });
                }
                catch { busy = false; }
            });
        }

        void SetCard(string key, string title, long bytes, string sub, bool alert = false)
        {
            var c = cards[key];
            c.Title = title; c.Value = Fmt.GB(bytes); c.Sub = sub; c.Alert = alert;
            c.Invalidate();
        }

        void ShowSnapshot()
        {
            var s = last;
            double frac = s.Total > 0 ? (double)s.InUse / s.Total : 0;
            header.Usage = string.Format(L.T("قيد الاستخدام {0} من {1}", "In use {0} of {1}"), Fmt.GB(s.InUse), Fmt.GB(s.Total));
            header.Fraction = frac;
            header.Pct = (frac * 100).ToString("0") + "%";
            header.Invalidate();

            SetCard("inst", L.T("الذاكرة المركّبة", "Installed RAM"), s.Installed, string.Format(L.T("محجوزة للعتاد {0}", "Hardware reserved {0}"), Fmt.GB(s.HwReserved)));
            SetCard("inuse", L.T("المستخدمة فعلياً", "In use"), s.InUse, string.Format(L.T("{0} من القابلة للاستخدام", "{0} of usable"), (frac * 100).ToString("0") + "%"));
            SetCard("standby", L.T("Standby (مؤقتة)", "Standby (cache)"), s.Standby, L.T("تتحرّر فوراً عند الحاجة", "Released instantly when needed"));
            SetCard("free", L.T("حرّة", "Free"), s.Free, string.Format(L.T("المتاح الحقيقي {0}", "Truly available {0}"), Fmt.GB(s.Standby + s.Free)));
            SetCard("commit", L.T("الالتزام Commit", "Commit charge"), s.Commit, string.Format(L.T("من حدّ {0}", "of {0} limit"), Fmt.GB(s.CommitLimit)));
            SetCard("proc", L.T("خاص العمليات", "Process private"), s.ProcPrivate + s.VM, string.Format(L.T("{0} عملية", "{0} processes"), s.Procs.Count));
            SetCard("compr", L.T("ضغط الذاكرة", "Memory compression"), s.Compression, L.T("مخفية في مدير المهام", "Hidden in Task Manager"));
            SetCard("kernel", L.T("نواة ويندوز", "Windows kernel"), s.Kernel, string.Format("NonPaged {0}", Fmt.GB(s.NonPaged)), s.NonPaged > (1536L << 20));
            SetCard("unattr", L.T("غير منسوبة", "Unattributed"), s.Unattributed, L.T("مشتركة أو مقفلة بالتعريفات", "Shared or driver-locked"));
            SetCard("modified", "Modified", s.Modified, L.T("بانتظار الكتابة للقرص", "Waiting to be written to disk"));

            bar.Segs = new List<Tuple<string, long, Color>> {
                Tuple.Create(L.T("العمليات (خاص)", "Processes (private)"), s.ProcPrivate, Theme.Proc),
                Tuple.Create(L.T("ضغط الذاكرة", "Memory compression"), s.Compression, Theme.Compr),
                Tuple.Create(L.T("آلات افتراضية", "Virtual machines"), s.VM, Theme.VM),
                Tuple.Create(L.T("نواة ويندوز", "Windows kernel"), s.Kernel, Theme.Kernel),
                Tuple.Create(L.T("غير منسوبة (مشتركة/مقفلة)", "Unattributed (shared/locked)"), s.Unattributed, Theme.Unattr),
                Tuple.Create("Modified", s.Modified, Theme.Modified),
                Tuple.Create(L.T("Standby (مؤقتة)", "Standby (cache)"), s.Standby, Theme.Standby),
                Tuple.Create(L.T("حرّة", "Free"), s.Free, Theme.Free) };
            bar.Invalidate();
            legend.SuspendLayout();
            while (legend.Controls.Count > 0) legend.Controls[0].Dispose();
            foreach (var seg in bar.Segs)
            {
                legend.Controls.Add(new Panel { Size = new Size(Theme.S(11), Theme.S(11)), BackColor = seg.Item3, Margin = new Padding(Theme.S(10), Theme.S(7), Theme.S(3), Theme.S(2)) });
                legend.Controls.Add(new Label { AutoSize = true, Text = seg.Item1 + "  " + Fmt.GB(seg.Item2), ForeColor = Theme.Text, Margin = new Padding(0, Theme.S(4), Theme.S(6), Theme.S(2)) });
            }
            legend.ResumeLayout();

            BindProcs();
            BindPools();
            ShowDiagnosis(s);
            lblStatus.Text = string.Format(L.T("آخر تحديث {0}", "Last refresh {0}"), s.Time.ToString("HH:mm:ss")) + "   •   " +
                (isAdmin ? L.T("صلاحيات مسؤول", "Administrator rights")
                         : L.T("بدون صلاحيات مسؤول: بعض المسارات قد تكون فارغة، لكن أرقام الذاكرة كاملة ودقيقة", "Not elevated: some paths may be empty, but all memory figures are complete and accurate"));
            lblCount.Text = string.Format(L.T("{0} عملية", "{0} processes"), s.Procs.Count);
        }

        void ShowDiagnosis(Snapshot s)
        {
            int pos = rtbDiag.SelectionStart;
            rtbDiag.SuspendLayout();
            rtbDiag.Clear();
            rtbDiag.SelectionFont = Theme.UI(15f, FontStyle.Bold);
            rtbDiag.SelectionColor = Theme.Header2;
            rtbDiag.AppendText(L.T("تشخيص الذاكرة", "Memory diagnosis") + "\n");
            rtbDiag.SelectionFont = Theme.UI(9.5f);
            rtbDiag.SelectionColor = Theme.Muted;
            rtbDiag.AppendText(s.Time.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n");
            foreach (var line in Report.Findings(s))
            {
                bool warn = line.StartsWith(L.T("تحذير", "Warning"));
                rtbDiag.SelectionBullet = true;
                rtbDiag.SelectionFont = Theme.UI(11f, warn ? FontStyle.Bold : FontStyle.Regular);
                rtbDiag.SelectionColor = warn ? Theme.Warn : Theme.Text;
                rtbDiag.AppendText(line + "\n\n");
            }
            rtbDiag.SelectionBullet = false;
            rtbDiag.SelectionFont = Theme.UI(9f);
            rtbDiag.SelectionColor = Theme.Muted;
            rtbDiag.AppendText("RamLens " + AppInfo.Version + "  •  " + AppInfo.Developer + "  •  " + AppInfo.Repo);
            rtbDiag.SelectionStart = Math.Min(pos, rtbDiag.TextLength);
            rtbDiag.ResumeLayout();
        }

        void BindProcs()
        {
            if (last == null) return;
            var dvOld = gridProc.DataSource as DataView;
            if (dvOld != null && !string.IsNullOrEmpty(dvOld.Sort)) procSort = dvOld.Sort;
            string selName = SelectedValue("Name");
            string selPid = SelectedValue("Pid");
            int firstRow = gridProc.FirstDisplayedScrollingRowIndex;

            var t = new DataTable();
            bool grp = grouped;
            t.Columns.Add("Name", typeof(string));
            if (grp) t.Columns.Add("Count", typeof(int)); else t.Columns.Add("Pid", typeof(int));
            t.Columns.Add("Kind", typeof(string));
            t.Columns.Add("Private", typeof(double));
            t.Columns.Add("Shared", typeof(double));
            t.Columns.Add("WS", typeof(double));
            t.Columns.Add("Commit", typeof(double));
            if (!grp)
            {
                t.Columns.Add("Peak", typeof(double));
                t.Columns.Add("PagedQ", typeof(double));
                t.Columns.Add("NonPagedQ", typeof(double));
                t.Columns.Add("Handles", typeof(int));
                t.Columns.Add("Threads", typeof(int));
                t.Columns.Add("Session", typeof(int));
                t.Columns.Add("Services", typeof(string));
            }
            t.Columns.Add("Path", typeof(string));
            if (grp)
                foreach (var g in Report.Groups(last))
                    t.Rows.Add(g.Name, g.Count, g.Kind, Fmt.MBd(g.Priv), Fmt.MBd(Math.Max(0, g.WS - g.Priv)), Fmt.MBd(g.WS), Fmt.MBd(g.Commit), g.Path);
            else
                foreach (var p in last.Procs)
                    t.Rows.Add(p.Name, p.Pid, p.Kind, Fmt.MBd(p.PrivWS), Fmt.MBd(Math.Max(0, p.WS - p.PrivWS)), Fmt.MBd(p.WS), Fmt.MBd(p.Commit),
                        Fmt.MBd(p.PeakWS), Math.Round(p.PagedQ / 1024.0), Math.Round(p.NonPagedQ / 1024.0), p.Handles, p.Threads, p.Session, p.Services, p.Path);
            maxPriv = t.Rows.Count == 0 ? 1 : t.AsEnumerable().Max(r => r.Field<double>("Private"));

            var dv = new DataView(t);
            try { dv.Sort = procSort; } catch { dv.Sort = procSort = "Private DESC"; }
            gridProc.DataSource = dv;
            for (int i = 0; i < ProcCols.GetLength(0); i++)
                if (gridProc.Columns.Contains(ProcCols[i, 0])) gridProc.Columns[ProcCols[i, 0]].HeaderText = ProcCols[i, 1];
            foreach (DataGridViewColumn c in gridProc.Columns)
            {
                if (c.ValueType == typeof(double)) { c.DefaultCellStyle.Format = "N0"; c.Width = Theme.S(118); }
                else if (c.ValueType == typeof(int)) c.Width = Theme.S(84);
            }
            gridProc.Columns["Name"].Width = Theme.S(210);
            gridProc.Columns["Kind"].Width = Theme.S(150);
            gridProc.Columns["Private"].Width = Theme.S(150);
            if (grp) gridProc.Columns["Count"].Width = Theme.S(100);
            gridProc.Columns["Path"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            gridProc.Columns["Path"].MinimumWidth = Theme.S(200);
            if (!grp) gridProc.Columns["Services"].Width = Theme.S(220);
            ApplyFilter();

            foreach (DataGridViewRow r in gridProc.Rows)
            {
                bool match = grp ? Convert.ToString(r.Cells["Name"].Value) == selName : Convert.ToString(r.Cells["Pid"].Value) == selPid;
                if (match && selName.Length > 0) { gridProc.CurrentCell = r.Cells["Name"]; break; }
            }
            if (firstRow >= 0 && firstRow < gridProc.RowCount) try { gridProc.FirstDisplayedScrollingRowIndex = firstRow; } catch { }
        }

        void ApplyFilter()
        {
            var dv = gridProc.DataSource as DataView;
            if (dv == null) return;
            string q = txtSearch.Text.Trim();
            if (q.Length == 0) { dv.RowFilter = ""; return; }
            var e = new StringBuilder();
            foreach (char c in q)
            {
                if (c == '*' || c == '%' || c == '[' || c == ']') e.Append('[').Append(c).Append(']');
                else if (c == '\'') e.Append("''");
                else e.Append(c);
            }
            string like = "LIKE '%" + e + "%'";
            string f = "Name " + like + " OR Path " + like + " OR Kind " + like;
            if (dv.Table.Columns.Contains("Services")) f += " OR Services " + like;
            dv.RowFilter = f;
        }

        void GridProc_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.Value == null || e.Value == DBNull.Value) return;
            string col = gridProc.Columns[e.ColumnIndex].Name;
            if (col == "Kind")
            {
                e.CellStyle.ForeColor = e.CellStyle.SelectionForeColor = Theme.KindColor(Convert.ToString(e.Value));
            }
            else if (col == "Private" && Convert.ToDouble(e.Value) >= 1024)
            {
                e.CellStyle.Font = Theme.UI(9.5f, FontStyle.Bold);
            }
            else if (col == "Name")
            {
                e.CellStyle.Font = Theme.UI(9.5f, FontStyle.Bold);
            }
        }

        void BindPools()
        {
            var dvOld = gridPool.DataSource as DataView;
            if (dvOld != null && !string.IsNullOrEmpty(dvOld.Sort)) poolSort = dvOld.Sort;
            int firstRow = gridPool.FirstDisplayedScrollingRowIndex;
            var t = new DataTable();
            t.Columns.Add("Tag", typeof(string));
            t.Columns.Add("NonPaged", typeof(double));
            t.Columns.Add("Paged", typeof(double));
            t.Columns.Add("Total", typeof(double));
            t.Columns.Add("Allocs", typeof(long));
            t.Columns.Add("Desc", typeof(string));
            foreach (var p in last.Pools)
                t.Rows.Add(p.Tag, Math.Round(p.NonPaged / 1048576.0, 2), Math.Round(p.Paged / 1048576.0, 2),
                    Math.Round((p.Paged + p.NonPaged) / 1048576.0, 2), p.Allocs, PoolInfo.Describe(p.Tag));
            maxPool = t.Rows.Count == 0 ? 1 : t.AsEnumerable().Max(r => r.Field<double>("Total"));
            var dv = new DataView(t);
            try { dv.Sort = poolSort; } catch { dv.Sort = poolSort = "Total DESC"; }
            gridPool.DataSource = dv;
            string[,] hdr = { { "Tag", L.T("الوسم", "Tag") }, { "NonPaged", L.T("NonPaged (MB) — لا تُرحَّل", "NonPaged (MB) - never paged") }, { "Paged", "Paged (MB)" },
                              { "Total", L.T("المجموع (MB)", "Total (MB)") }, { "Allocs", L.T("تخصيصات قائمة", "Live allocations") }, { "Desc", L.T("الوصف", "Description") } };
            for (int i = 0; i < hdr.GetLength(0); i++) gridPool.Columns[hdr[i, 0]].HeaderText = hdr[i, 1];
            foreach (DataGridViewColumn c in gridPool.Columns) { c.Width = Theme.S(150); if (c.ValueType == typeof(double)) c.DefaultCellStyle.Format = "N1"; }
            gridPool.Columns["Tag"].DefaultCellStyle.Font = new Font("Consolas", 10f, FontStyle.Bold);
            gridPool.Columns["Desc"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            if (firstRow >= 0 && firstRow < gridPool.RowCount) try { gridPool.FirstDisplayedScrollingRowIndex = firstRow; } catch { }
        }

        void FindDriver(string tag)
        {
            lblStatus.Text = L.T("جارٍ البحث عن الوسم '", "Searching driver files for tag '") + tag + L.T("' في ملفات التعريفات...", "'...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                var hits = new List<string>();
                byte[] pat = Encoding.ASCII.GetBytes(tag);
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
                foreach (var f in Directory.GetFiles(dir, "*.sys"))
                {
                    try { if (IndexOf(File.ReadAllBytes(f), pat) >= 0) hits.Add(Path.GetFileName(f)); } catch { }
                }
                BeginInvoke((MethodInvoker)delegate
                {
                    lblStatus.Text = L.T("انتهى البحث.", "Search finished.");
                    string msg = hits.Count == 0
                        ? L.T("لم يُعثر على الوسم في ملفات التعريفات. قد يكون من نواة ويندوز (ntoskrnl.exe) أو من تعريف في مجلد آخر.", "Tag not found in driver files. It may belong to the Windows kernel (ntoskrnl.exe) or to a driver in another folder.")
                        : L.T("الوسم '", "Tag '") + tag + L.T("' موجود في التعريفات التالية (أحدها المسؤول؛ الوسوم القصيرة الشائعة تعطي نتائج إضافية):\n\n", "' appears in these drivers (one of them is the owner; short common tags give extra hits):\n\n") + string.Join("\n", hits.Take(40));
                    MessageBox.Show(msg, L.T("البحث عن التعريف", "Find driver"), MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, L.MbOpt);
                });
            });
        }

        static int IndexOf(byte[] b, byte[] p)
        {
            for (int i = 0; i <= b.Length - p.Length; i++)
            {
                if (b[i] != p[0]) continue;
                int j = 1;
                while (j < p.Length && b[i + j] == p[j]) j++;
                if (j == p.Length) return i;
            }
            return -1;
        }

        void ExportCsv()
        {
            var dv = gridProc.DataSource as DataView;
            if (dv == null) return;
            using (var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "RamLens_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var sb = new StringBuilder();
                var cols = gridProc.Columns.Cast<DataGridViewColumn>().ToList();
                sb.AppendLine(string.Join(",", cols.Select(c => Csv(c.HeaderText))));
                foreach (DataRowView r in dv) sb.AppendLine(string.Join(",", cols.Select(c => Csv(Convert.ToString(r[c.DataPropertyName])))));
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                lblStatus.Text = L.T("حُفظ: ", "Saved: ") + d.FileName;
            }
        }

        static string Csv(string v) { return "\"" + (v ?? "").Replace("\"", "\"\"") + "\""; }

        void SaveReport()
        {
            if (last == null) return;
            using (var d = new SaveFileDialog { Filter = "Text|*.txt", FileName = "RamLens_report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(d.FileName, Report.Build(last), new UTF8Encoding(true));
                lblStatus.Text = L.T("حُفظ: ", "Saved: ") + d.FileName;
            }
        }

        void SwitchLanguage()
        {
            L.Save(!L.En);
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true }); Close(); } catch { }
        }

        void RunAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true });
                Close();
            }
            catch { }
        }
    }

    class AboutForm : Form
    {
        public AboutForm(Image logo, Icon icon)
        {
            Text = L.T("حول RamLens", "About RamLens");
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            RightToLeft = L.Rtl;
            RightToLeftLayout = !L.En;
            BackColor = Color.White;
            Font = Theme.UI(10f);
            ClientSize = new Size(Theme.S(520), Theme.S(500));

            var head = new HeaderPanel { Dock = DockStyle.Top, Height = Theme.S(80), Logo = logo, Usage = "v" + AppInfo.Version, Meter = false };
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(Theme.S(24), Theme.S(18), Theme.S(24), Theme.S(8)) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var desc = new Label
            {
                AutoSize = true, MaximumSize = new Size(Theme.S(470), 0), ForeColor = Theme.Text, Margin = new Padding(0, 0, 0, Theme.S(14)),
                Text = L.T("أداة تكشف أين تذهب ذاكرة الرام فعلاً: كل العمليات بما فيها المخفية، وذاكرة النواة، وضغط الذاكرة، مع تشخيص واضح.",
                           "A tool that shows where your RAM really goes: every process including hidden ones, kernel memory and memory compression, with a plain-language diagnosis.")
            };
            body.Controls.Add(desc, 0, 0);
            body.SetColumnSpan(desc, 2);

            AddRow(body, L.T("المطوّر", "Developer"), new Label { AutoSize = true, Text = AppInfo.Developer, Font = Theme.UI(10.5f, FontStyle.Bold), ForeColor = Theme.Header2 });
            AddRow(body, L.T("الجهة", "Affiliation"), new Label { AutoSize = true, Text = AppInfo.Affiliation, ForeColor = Theme.Text });
            AddRow(body, L.T("البريد", "E-mail"), Link(AppInfo.Email, "mailto:" + AppInfo.Email));
            AddRow(body, "GitHub", Link("github.com/kmkholm/RamLens", AppInfo.Repo));
            AddRow(body, "ORCID", Link("0000-0002-1227-387X", AppInfo.Orcid));
            AddRow(body, L.T("الإصدار", "Version"), new Label { AutoSize = true, Text = "\u200E" + AppInfo.Version + "  •  Windows x64", ForeColor = Theme.Text });
            AddRow(body, L.T("الترخيص", "License"), new Label { AutoSize = true, Text = L.T("MIT — مجاني ومفتوح المصدر", "MIT — free and open source"), ForeColor = Theme.Text });

            var foot = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(56), BackColor = Theme.Back, Padding = new Padding(Theme.S(16), Theme.S(11), Theme.S(16), Theme.S(11)) };
            var ok = new Button
            {
                Text = L.T("إغلاق", "Close"), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White,
                Width = Theme.S(110), Dock = DockStyle.Right
            };
            ok.FlatAppearance.BorderSize = 0;
            var copy = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = L.En ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight, Text = "© 2026 " + AppInfo.Developer };
            foot.Controls.Add(copy);
            foot.Controls.Add(ok);
            AcceptButton = CancelButton = ok;

            Controls.Add(body);
            Controls.Add(foot);
            Controls.Add(head);
        }

        static void AddRow(TableLayoutPanel t, string label, Control value)
        {
            var l = new Label { AutoSize = true, Text = label, ForeColor = Theme.Muted, Margin = new Padding(0, Theme.S(5), Theme.S(18), Theme.S(5)) };
            value.Margin = new Padding(0, Theme.S(5), 0, Theme.S(5));
            t.Controls.Add(l);
            t.Controls.Add(value);
        }

        static LinkLabel Link(string text, string url)
        {
            var l = new LinkLabel { AutoSize = true, Text = text, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Header2, LinkBehavior = LinkBehavior.HoverUnderline };
            l.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
            return l;
        }
    }
}
