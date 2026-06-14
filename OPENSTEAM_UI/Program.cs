#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using Guna.UI2.WinForms.Enums;
using System.Drawing;
using System.Net.Http;
using System.Reflection;

namespace OpenSteamLaunch
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared UI styling helpers (from OpenSteam)
    // ─────────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────────
    // Theme palette — burn orange & charcoal grey
    // ─────────────────────────────────────────────────────────────────────────

    public static class Theme
    {
        // Backgrounds
        public static readonly Color BgDark      = Color.FromArgb(22,  22,  24);   // near-black charcoal
        public static readonly Color BgPanel     = Color.FromArgb(32,  32,  36);   // main panel surface
        public static readonly Color BgInput     = Color.FromArgb(42,  42,  48);   // text-box fill
        public static readonly Color BgInputHov  = Color.FromArgb(50,  50,  58);

        // Orange accent
        public static readonly Color Orange      = Color.FromArgb(230, 100,  20);  // burn orange
        public static readonly Color OrangeHov   = Color.FromArgb(245, 120,  35);
        public static readonly Color OrangeDim   = Color.FromArgb(160,  68,  10);

        // Neutral greys
        public static readonly Color GreyMid     = Color.FromArgb( 80,  80,  88);
        public static readonly Color GreyLight   = Color.FromArgb(160, 160, 170);
        public static readonly Color GreyBorder  = Color.FromArgb( 60,  60,  68);

        // Semantic
        public static readonly Color Green       = Color.FromArgb( 40, 190,  90);
        public static readonly Color Red         = Color.FromArgb(210,  50,  50);
        public static readonly Color RedHov      = Color.FromArgb(235,  70,  70);
        public static readonly Color Warn        = Color.FromArgb(230, 180,  40);

        // Text
        public static readonly Color TextPrimary = Color.FromArgb(240, 240, 245);
        public static readonly Color TextMuted   = Color.FromArgb(140, 140, 150);
        public static readonly Color TextOrange  = Color.FromArgb(230, 130,  50);
    }

    public static class FloatingUiStyle
    {
        public static void AddFloatingShadow(Guna2Button button, int radius)
        {
            if (button == null) return;

            Color fill = button.FillColor;
            if (fill.IsEmpty)
                fill = Theme.Orange;

            button.Animated = true;
            button.BackColor = Color.Transparent;
            button.UseTransparentBackground = true;
            button.AutoRoundedCorners = false;

            if (button.Height > 0)
                button.BorderRadius = Math.Max(1, (button.Height / 2) - 1);
            else
                button.BorderRadius = radius;

            button.ShadowDecoration.Enabled = false;
            button.BorderThickness = 0;
            button.HoverState.FillColor = MakeLighter(fill, 18);
        }

        public static void StyleFieldBox(Guna2TextBox box, int radius)
        {
            if (box == null) return;

            box.BackColor  = Color.Transparent;
            box.BorderRadius = radius;
            box.BorderThickness = 1;
            box.BorderColor = Theme.GreyBorder;
            box.FillColor  = Theme.BgInput;
            box.ForeColor  = Theme.TextPrimary;
            box.PlaceholderForeColor = Theme.TextMuted;
            box.Font = new Font("Segoe UI", 9.5f);
            box.Padding = new Padding(2, 0, 0, 0);
            box.FocusedState.BorderColor = Theme.Orange;
            box.HoverState.BorderColor   = Theme.OrangeDim;
            box.ShadowDecoration.Enabled = false;
        }

        public static void StyleFloatingControlBox(Guna2ControlBox box, int radius)
        {
            if (box == null) return;

            box.Animated = true;
            box.BackColor = Color.Transparent;
            box.UseTransparentBackground = true;
            box.BorderRadius = radius;
            box.IconColor = Theme.TextPrimary;
            box.ShadowDecoration.Enabled = false;
        }

        public static Guna2Button MakeOrangeButton(string text, Font? font = null)
        {
            var btn = new Guna2Button
            {
                Text      = text,
                FillColor = Theme.Orange,
                ForeColor = Color.White,
                Font      = font ?? new Font("Segoe UI Semibold", 10f),
                Cursor    = Cursors.Hand,
                BackColor = Color.Transparent,
                UseTransparentBackground = true,
                AutoSize  = true,
                Margin    = new Padding(0, 0, 8, 0),
            };
            btn.ShadowDecoration.Enabled = false;
            btn.BorderThickness = 0;
            btn.HoverState.FillColor = Theme.OrangeHov;
            AddFloatingShadow(btn, 18);
            return btn;
        }

        public static Guna2Button MakeGreyButton(string text, Font? font = null)
        {
            var btn = new Guna2Button
            {
                Text      = text,
                FillColor = Theme.GreyMid,
                ForeColor = Color.White,
                Font      = font ?? new Font("Segoe UI Semibold", 10f),
                Cursor    = Cursors.Hand,
                BackColor = Color.Transparent,
                UseTransparentBackground = true,
                AutoSize  = true,
                Margin    = new Padding(0, 0, 8, 0),
            };
            btn.ShadowDecoration.Enabled = false;
            btn.BorderThickness = 0;
            btn.HoverState.FillColor = MakeLighter(Theme.GreyMid, 20);
            AddFloatingShadow(btn, 18);
            return btn;
        }

        private static Color MakeLighter(Color color, int amount)
        {
            int r = Math.Min(255, color.R + amount);
            int g = Math.Min(255, color.G + amount);
            int b = Math.Min(255, color.B + amount);
            return Color.FromArgb(color.A, r, g, b);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Main form — OpenSteam look + OpenSteamTool Start/End Process buttons
    // ─────────────────────────────────────────────────────────────────────────

    public partial class MainForm : Form
    {
        private const string SETTINGS_FILE = "LSsettings.json";
        private const int COLLAPSED_WIDTH = 700;
        private const int FORM_HEIGHT = 270;
        private const int GAME_LIST_WIDTH = 430;

        // Folder/file name constants (from OpenSteamTool Launch Tool)
        private const string SFF_FOLDER      = "sff";
        private const string OpenSteamTool_FOLDER = "OpenSteamTool";
        private const string DLL_DWMAPI      = "dwmapi.dll";
        private const string DLL_OpenSteamTool    = "OpenSteamTool.dll";
		private const string DLL_XINPUT1_4   = "xinput1_4.dll";
        private const string PLUGIN_FOLDER   = "lua";

        // Path inputs
        private Guna2TextBox steamPathTextBox   = null!;
        private Guna2TextBox luaFolderTextBox   = null!;

        // Browse buttons
        private Guna2Button steamBrowseButton = null!;
        private Guna2Button luaBrowseButton   = null!;

        // Action buttons (OpenSteam — Apply only)
        private Guna2Button applyButton       = null!;

        // Process buttons (OpenSteamTool Launch Tool) — replacing Auto LC Setup
        private Guna2Button startButton = null!;
        private Guna2Button endButton   = null!;

        // Game List pull-tab and slide-out panel
        private Panel gameListTab = null!;
        private GameListPanel? _gameListPanel;

        private Guna2ProgressBar progressBar = null!;
        private Label statusLabel    = null!;
        private Label attentionLabel = null!;
        private Label gameNameLabel  = null!;
        private TableLayoutPanel mainPanel = null!;
		
		protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // Suppress resize-arrow cursor on edges — form is not resizable
            if (m.Msg == 0x0084) // WM_NCHITTEST
            {
                int result = m.Result.ToInt32();
                // Replace any edge/corner hit-test result with HTCLIENT (1)
                if (result >= 10 && result <= 17) // HTLEFT..HTBOTTOMRIGHT
                    m.Result = new IntPtr(1);
            }
        }


        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED — composites all child paints before display
                //cp.ExStyle |= 0x00080000; // WS_EX_LAYERED — required for smooth compositing on borderless forms
                return cp;
            }
        }

        public class Settings
        {
            public string SteamFolder { get; set; } = "";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Steam API game name lookup
        // ─────────────────────────────────────────────────────────────────────

        private string GetGameNameFromSteam(string appId)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                string json = client.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}").Result;
                using var doc = JsonDocument.Parse(json);
                var appData = doc.RootElement.GetProperty(appId);
                if (appData.GetProperty("success").GetBoolean())
                    return appData.GetProperty("data").GetProperty("name").GetString() ?? "Unknown Game";
            }
            catch { }
            return "Unknown Game";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Constructor
        // ─────────────────────────────────────────────────────────────────────

        public MainForm()
        {
            InitializeComponent();

            new Guna2BorderlessForm
            {
                ContainerControl = this,
                BorderRadius     = 14,
                DragForm         = false,
            };

            _ = new Guna2DragControl { TargetControl = mainPanel };

            Point _lastLocation = this.Location;
            var _opacityTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _opacityTimer.Tick += (s, e) =>
            {
                if (_gameListPanel != null && !_gameListPanel.IsDisposed)
                    _gameListPanel.Opacity = this.Opacity;
            };
            _opacityTimer.Start();

            var closeButton = new Guna2ControlBox
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(this.Width - 66, 10),
                FillColor = Theme.Red,
                Size = new Size(30, 13)
            };

            var minimizeButton = new Guna2ControlBox
            {
                ControlBoxType = ControlBoxType.MinimizeBox,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(this.Width - 100, 10),
                FillColor = Theme.GreyMid,
                Size = new Size(30, 13)
            };

            FloatingUiStyle.StyleFloatingControlBox(minimizeButton, 7);
            FloatingUiStyle.StyleFloatingControlBox(closeButton, 7);

            this.Controls.Add(minimizeButton);
            this.Controls.Add(closeButton);

            var assembly = Assembly.GetExecutingAssembly();

            string? bgResource = assembly.GetManifestResourceNames()
                .FirstOrDefault(r => r.EndsWith("background.png"));
            if (bgResource != null)
            {
                using Stream? stream = assembly.GetManifestResourceStream(bgResource);
                if (stream != null)
                    this.BackgroundImage = Image.FromStream(stream);
            }

            this.BackgroundImageLayout = ImageLayout.Stretch;

            try
            {
                string? iconResource = assembly.GetManifestResourceNames()
                    .FirstOrDefault(r => r.EndsWith(".ico"));
                if (iconResource != null)
                {
                    using var iconStream = assembly.GetManifestResourceStream(iconResource);
                    if (iconStream != null)
                        this.Icon = new Icon(iconStream);
                }
            }
            catch { }

            minimizeButton.BringToFront();
            closeButton.BringToFront();

            LoadSettings();
        }

        // ─────────────────────────────────────────────────────────────────────
        // UI initialisation
        // ─────────────────────────────────────────────────────────────────────

        private void AddFloatingShadow(Guna2Button button, int radius)
            => FloatingUiStyle.AddFloatingShadow(button, radius);

        private static Label MakeFieldLabel(string text) => new Label
        {
            Text      = text,
            AutoSize  = true,
            Anchor    = AnchorStyles.Left | AnchorStyles.Top,
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 9f),
            Margin    = new Padding(0, 6, 8, 0)
        };

        private static void SetDoubleBuffered(Control c)
        {
            typeof(Control)
                .GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(c, true);
        }

        private void InitializeComponent()
        {
            this.DoubleBuffered  = true;
            this.Size            = new Size(COLLAPSED_WIDTH, FORM_HEIGHT);
            this.MinimumSize     = new Size(COLLAPSED_WIDTH, FORM_HEIGHT);
            this.MaximumSize     = new Size(COLLAPSED_WIDTH, FORM_HEIGHT);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition   = FormStartPosition.CenterScreen;
            this.BackColor       = Theme.BgDark;

            int row = 0;

            mainPanel = new TableLayoutPanel
            {
                Location    = new Point(0, 0),
                Size        = new Size(COLLAPSED_WIDTH, FORM_HEIGHT - 6),
                Padding     = new Padding(28, 30, 36, 18),
                ColumnCount = 3,
                RowCount    = 10,
                AutoSize    = false,
                BackColor   = Color.Transparent
            };
            SetDoubleBuffered(mainPanel);

            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            for (int i = 0; i < 10; i++)
                mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ── Row: Steam Folder ────────────────────────────────────────────
            mainPanel.Controls.Add(MakeFieldLabel("Steam Folder"), 0, row);

            steamPathTextBox = new Guna2TextBox
            {
                Dock   = DockStyle.Top,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Height = 26,
                Margin = new Padding(6, 0, 8, 0)
            };
            FloatingUiStyle.StyleFieldBox(steamPathTextBox, 6);
            mainPanel.Controls.Add(steamPathTextBox, 1, row);

            steamBrowseButton = new Guna2Button
            {
                Text      = "Browse",
                FillColor = Color.FromArgb(50, 50, 56),
                ForeColor = Color.White,
                Size      = new Size(90, 26),
                AutoSize  = false,
                Cursor    = Cursors.Hand,
                BackColor = Color.Transparent,
                UseTransparentBackground = true,
                Font      = new Font("Segoe UI Semibold", 9.5f),
                Margin    = new Padding(0)
            };
            steamBrowseButton.ShadowDecoration.Enabled = false;
            steamBrowseButton.BorderThickness = 0;
            steamBrowseButton.BorderRadius    = 12;
            steamBrowseButton.HoverState.FillColor = Color.FromArgb(68, 68, 76);
            steamBrowseButton.Click      += SteamBrowseButton_Click;
            steamPathTextBox.TextChanged += (s, e) => { SaveSettings(); CheckOpenSteamToolStatus(); };
            mainPanel.Controls.Add(steamBrowseButton, 2, row++);

            // ── Row: Lua + Manifest Source ───────────────────────────────────
            var luaLabel = MakeFieldLabel("Lua / Manifest Source");
            luaLabel.Margin = new Padding(0, 6, 8, 0);
            mainPanel.Controls.Add(luaLabel, 0, row);

            luaFolderTextBox = new Guna2TextBox
            {
                Dock      = DockStyle.Top,
                Anchor    = AnchorStyles.Left | AnchorStyles.Right,
                AllowDrop = true,
                Height    = 26,
                Margin    = new Padding(6, 6, 8, 0)
            };
            FloatingUiStyle.StyleFieldBox(luaFolderTextBox, 6);
            luaFolderTextBox.DragEnter   += LuaFolderTextBox_DragEnter;
            luaFolderTextBox.DragDrop    += LuaFolderTextBox_DragDrop;
            luaFolderTextBox.TextChanged += LuaFolderTextBox_TextChanged;
            mainPanel.Controls.Add(luaFolderTextBox, 1, row);

            luaBrowseButton = new Guna2Button
            {
                Text      = "Browse",
                FillColor = Color.FromArgb(50, 50, 56),
                ForeColor = Color.White,
                Size      = new Size(90, 26),
                AutoSize  = false,
                Cursor    = Cursors.Hand,
                BackColor = Color.Transparent,
                UseTransparentBackground = true,
                Font      = new Font("Segoe UI Semibold", 9.5f),
                Margin    = new Padding(0, 6, 0, 0)
            };
            luaBrowseButton.ShadowDecoration.Enabled = false;
            luaBrowseButton.BorderThickness = 0;
            luaBrowseButton.BorderRadius    = 12;
            luaBrowseButton.HoverState.FillColor = Color.FromArgb(68, 68, 76);
            luaBrowseButton.Click += LuaBrowseButton_Click;
            mainPanel.Controls.Add(luaBrowseButton, 2, row++);

            // ── Apply Changes — col 2 only, directly under Browse ────────────
            applyButton = new Guna2Button
            {
                Text      = "▶  Apply Changes",
                FillColor = Theme.Orange,
                ForeColor = Color.White,
                Size      = new Size(160, 32),
                AutoSize  = false,
                Enabled   = false,
                Cursor    = Cursors.Hand,
                BackColor = Color.Transparent,
                UseTransparentBackground = true,
                Font      = new Font("Segoe UI Semibold", 10f),
                Margin    = new Padding(0, 6, 0, 0)
            };
            applyButton.ShadowDecoration.Enabled = false;
            applyButton.BorderThickness = 0;
            applyButton.BorderRadius    = 15;
            applyButton.HoverState.FillColor = Theme.OrangeHov;
            applyButton.Click += ApplyButton_Click;

            luaFolderTextBox.TextChanged += (s, e) =>
                applyButton.Enabled = !string.IsNullOrWhiteSpace(luaFolderTextBox.Text);

            mainPanel.Controls.Add(new Label { Size = new Size(0, 0), Margin = new Padding(0) }, 0, row);
            mainPanel.SetColumnSpan(mainPanel.GetControlFromPosition(0, row), 2);
            mainPanel.Controls.Add(applyButton, 2, row++);

            // ── Row: Start Process / Game Name / End Process ─────────────────
            startButton = new Guna2Button
            {
                Text      = "▶  Start Process",
                FillColor = Theme.Green,
                Size      = new Size(138, 32),
                AutoSize  = false,
                Anchor    = AnchorStyles.Left,
                Margin    = new Padding(0, 10, 0, 0),
                Font      = new Font("Segoe UI Semibold", 10f)
            };

            endButton = new Guna2Button
            {
                Text      = "■  End Process",
                FillColor = Theme.Red,
                Size      = new Size(138, 32),
                AutoSize  = false,
                Anchor    = AnchorStyles.Right,
                Margin    = new Padding(0, 10, 0, 0),
                Font      = new Font("Segoe UI Semibold", 10f)
            };

            startButton.Click += StartButton_Click;
            endButton.Click   += EndButton_Click;
            FloatingUiStyle.AddFloatingShadow(startButton, 18);
            FloatingUiStyle.AddFloatingShadow(endButton,   18);

            gameNameLabel = new Label
            {
                Text      = "",
                AutoSize  = true,
                Anchor    = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.TextOrange,
                Font      = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Margin    = new Padding(0, 10, 0, 0)
            };

            mainPanel.Controls.Add(startButton,   0, row);
            mainPanel.Controls.Add(gameNameLabel, 1, row);
            mainPanel.Controls.Add(endButton,     2, row);
            row++;

            // ── Pull-tab: slim vertical "Game List" tab on the right edge ────
            const int TAB_WIDTH = 20;

            gameListTab = new Panel
            {
                Width     = TAB_WIDTH,
                BackColor = Theme.BgPanel,
                Cursor    = Cursors.Hand,
            };

            gameListTab.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode     = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                pe.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                using var font  = new Font("Segoe UI Semibold", 8f, FontStyle.Bold);
                string    label = "GAME LIST";
                SizeF     sz    = pe.Graphics.MeasureString(label, font);

                pe.Graphics.TranslateTransform(TAB_WIDTH / 2f, gameListTab.Height / 2f);
                pe.Graphics.RotateTransform(-90);
                pe.Graphics.DrawString(label, font, new SolidBrush(Theme.TextMuted),
                    -sz.Width / 2f, -sz.Height / 2f);
                pe.Graphics.ResetTransform();

                // left-edge accent line
                using var pen = new Pen(Theme.Orange, 2);
                pe.Graphics.DrawLine(pen, 0, 0, 0, gameListTab.Height);
            };

            gameListTab.MouseEnter += (s, e) => { gameListTab.BackColor = Color.FromArgb(48, 48, 56); };
            gameListTab.MouseLeave += (s, e) => { gameListTab.BackColor = Theme.BgPanel; };
            gameListTab.Click      += GameListButton_Click;

            this.Controls.Add(gameListTab);
            gameListTab.BringToFront();

            this.Shown  += (s, e) => PositionGameListTab();
            this.Resize += (s, e) => PositionGameListTab();
            this.Move   += (s, e) => PositionGameListWindow();
            this.FormClosed += (s, e) =>
            {
                if (_gameListPanel != null && !_gameListPanel.IsDisposed)
                    _gameListPanel.Close();
            };

            // ── Progress bar ─────────────────────────────────────────────────
            progressBar = new Guna2ProgressBar
            {
                Size               = new Size(300, 6),
                FillColor          = Theme.BgInput,
                ProgressColor      = Theme.Orange,
                Visible            = false,
                AutoRoundedCorners = true,
                Margin             = new Padding(0, 8, 0, 2)
            };
            mainPanel.Controls.Add(progressBar, 0, row);
            mainPanel.SetColumnSpan(progressBar, 3);
            row++;

            // ── Status label ──────────────────────────────────────────────────
            statusLabel = new Label
            {
                Text      = "",
                AutoSize  = true,
                Anchor    = AnchorStyles.Left,
                ForeColor = Theme.Green,
                Font      = new Font("Segoe UI", 8.5f),
                Margin    = new Padding(0, 2, 0, 0)
            };
            mainPanel.Controls.Add(statusLabel, 0, row);
            mainPanel.SetColumnSpan(statusLabel, 3);
            row++;

            // ── Attention label ───────────────────────────────────────────────
            attentionLabel = new Label
            {
                Text      = "",
                AutoSize  = true,
                Anchor    = AnchorStyles.Left,
                ForeColor = Theme.Warn,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Visible   = false,
                Margin    = new Padding(0, 1, 0, 0)
            };
            mainPanel.Controls.Add(attentionLabel, 0, row);
            mainPanel.SetColumnSpan(attentionLabel, 3);

            this.Controls.Add(mainPanel);
        }

        private void PositionGameListTab()
        {
            if (gameListTab == null) return;
            // Pin the tab flush to the right edge, full height of the main form area
            gameListTab.SetBounds(COLLAPSED_WIDTH - gameListTab.Width, 0, gameListTab.Width, FORM_HEIGHT);
            gameListTab.BringToFront();
        }

        // ─────────────────────────────────────────────────────────────────────
        // UI helpers
        // ─────────────────────────────────────────────────────────────────────

        private bool IsOpenSteamActive()
        {
            string steam = steamPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(steam) || !Directory.Exists(steam)) return false;
            return File.Exists(Path.Combine(steam, DLL_DWMAPI))
                && File.Exists(Path.Combine(steam, DLL_XINPUT1_4))
                && File.Exists(Path.Combine(steam, DLL_OpenSteamTool));
        }

        private void CheckOpenSteamToolStatus()
        {
            string steam = steamPathTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(steam) || !Directory.Exists(steam))
            {
                statusLabel.Text      = "⚠ Enter a valid Steam folder path to check OpenSteamTool status.";
                statusLabel.ForeColor = Theme.TextMuted;
                startButton.Enabled   = false;
                endButton.Enabled     = false;
                return;
            }

            bool dwm = File.Exists(Path.Combine(steam, DLL_DWMAPI));
			bool dww = File.Exists(Path.Combine(steam, DLL_XINPUT1_4));
            bool lc  = File.Exists(Path.Combine(steam, DLL_OpenSteamTool));
            bool installed = dwm && dww && lc;

            if (installed)
            {
                statusLabel.Text      = "✔ OpenSteamTool is installed in your Steam folder.";
                statusLabel.ForeColor = Theme.Green;
                startButton.Enabled   = false;   // already installed — no need to start
                endButton.Enabled     = true;
            }
            else
            {
                var missing = new List<string>();
                if (!dwm) missing.Add(DLL_DWMAPI);
				if (!dww) missing.Add(DLL_XINPUT1_4);
                if (!lc)  missing.Add(DLL_OpenSteamTool);
                statusLabel.Text      = $"⚠ OpenSteamTool NOT installed — missing: {string.Join(", ", missing)}. Run Start Process.";
                statusLabel.ForeColor = Theme.TextOrange;
                startButton.Enabled   = true;
                endButton.Enabled     = false;   // nothing to end
            }
        }

        private void UpdateStatus(string message)
        {
            statusLabel.Text      = message;
            statusLabel.ForeColor = Theme.Green;
        }

        private void SetButtonsEnabled(bool enabled)
        {
            applyButton.Enabled = enabled && !string.IsNullOrWhiteSpace(luaFolderTextBox.Text);

            if (enabled)
            {
                // Restore correct start/end state based on actual install status
                CheckOpenSteamToolStatus();
            }
            else
            {
                startButton.Enabled = false;
                endButton.Enabled   = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Browse handlers
        // ─────────────────────────────────────────────────────────────────────

        private void SteamBrowseButton_Click(object? sender, EventArgs e)
        {
            using var d = new FolderBrowserDialog();
            if (d.ShowDialog() == DialogResult.OK)
                steamPathTextBox.Text = d.SelectedPath;
        }

        private void LuaBrowseButton_Click(object? sender, EventArgs e)
        {
            using var selector = new SourceSelectionDialog();
            DialogResult result = selector.ShowDialog();

            if (result == DialogResult.OK)
            {
                using var d = new FolderBrowserDialog();
                if (d.ShowDialog() == DialogResult.OK)
                    luaFolderTextBox.Text = d.SelectedPath;
            }
            else if (result == DialogResult.Yes)
            {
                using var d = new OpenFileDialog { Filter = "Zip Files (*.zip)|*.zip" };
                if (d.ShowDialog() == DialogResult.OK)
                    luaFolderTextBox.Text = d.FileName;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Drag-and-drop on the Lua source field
        // ─────────────────────────────────────────────────────────────────────

        private void LuaFolderTextBox_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void LuaFolderTextBox_DragDrop(object? sender, DragEventArgs e)
        {
            var paths = (string[])e.Data!.GetData(DataFormats.FileDrop)!;
            if (paths.Length > 0)
                luaFolderTextBox.Text = paths[0];
        }

        private void LuaFolderTextBox_TextChanged(object? sender, EventArgs e)
        {
            gameNameLabel.Text = "";
            string path = luaFolderTextBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(path))
                _ = FetchAndShowGameNameAsync(path);
        }

        private async Task FetchAndShowGameNameAsync(string path)
        {
            try
            {
                string appId;

                if (File.Exists(path) && path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    appId = Path.GetFileNameWithoutExtension(path);
                }
                else if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var zip = ZipFile.OpenRead(path);
                    var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".lua"));
                    if (entry == null) return;
                    appId = Path.GetFileNameWithoutExtension(entry.Name);
                }
                else if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, "*.lua");
                    if (files.Length == 0) return;
                    appId = Path.GetFileNameWithoutExtension(files[0]);
                }
                else return;

                if (!long.TryParse(appId, out _)) return;

                var name = await Task.Run(() => GetGameNameFromSteam(appId));

                if (gameNameLabel.IsDisposed) return;
                gameNameLabel.Invoke(new Action(() => gameNameLabel.Text = name));
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Validate / Preview / Apply
        // ─────────────────────────────────────────────────────────────────────

        private bool ValidateSteamPath(string path)
            => File.Exists(Path.Combine(path, "config", "config.vdf"));

        private bool ValidatePaths()
        {
            if (!Directory.Exists(steamPathTextBox.Text)) return false;
            string src = luaFolderTextBox.Text.Trim();
            return Directory.Exists(src)
                || src.EndsWith(".zip",      StringComparison.OrdinalIgnoreCase)
                || src.EndsWith(".lua",      StringComparison.OrdinalIgnoreCase)
                || src.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase);
        }

        private async void ApplyButton_Click(object? sender, EventArgs e)
        {
            if (!ValidatePaths()) return;

            SetButtonsEnabled(false);
            progressBar.Value   = 0;
            progressBar.Maximum = 5;
            progressBar.Visible = true;

            bool success = false;

            try
            {
                await Task.Run(() => EditConfig());
                success = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("Error occurred");
            }
            finally
            {
                SetButtonsEnabled(true);
                progressBar.Visible = false;

                if (success)
                {
                    string luaDestDesc = IsOpenSteamActive()
                        ? "Steam/config/lua (OpenSteamTool is active)."
                        : "local lua folder next to this app.";
                    attentionLabel.Text    = $"✔ Changes applied. Lua written to {luaDestDesc}";
                    attentionLabel.Visible = true;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Start Process (from OpenSteamTool Launch Tool)
        //
        //  Expected layout next to the .exe:
        //    sff/OpenSteamTool/dwmapi.dll
        //    sff/OpenSteamTool/OpenSteamTool.dll
        //    lua/   (folder with lua files)
        //
        //  Actions:
        //    1. Copy dwmapi.dll  → <steam>/dwmapi.dll
        //    2. Copy OpenSteamTool.dll → <steam>/OpenSteamTool.dll
        //    3. Move lua/  → <steam>/config/lua/
        // ─────────────────────────────────────────────────────────────────────

        private async void StartButton_Click(object? sender, EventArgs e)
        {
            string steamFolder = steamPathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(steamFolder) || !Directory.Exists(steamFolder))
            {
                MessageBox.Show("Please enter a valid Steam folder path.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SetButtonsEnabled(false);
            this.SuspendLayout();
            progressBar.Value   = 0;
            progressBar.Maximum = 100;
            progressBar.Visible = true;
            this.ResumeLayout(false);

            await Task.Run(() => ExecuteStartProcess(steamFolder));

            this.SuspendLayout();
            SetButtonsEnabled(true);
            progressBar.Visible = false;
            this.ResumeLayout(false);
            CheckOpenSteamToolStatus();
        }

        private void ExecuteStartProcess(string steamFolder)
        {
            try
            {
                string currentDir  = AppDomain.CurrentDomain.BaseDirectory;
                string OpenSteamToolDir = Path.Combine(currentDir, SFF_FOLDER, OpenSteamTool_FOLDER);
                string srcDwmapi   = Path.Combine(OpenSteamToolDir, DLL_DWMAPI);
				string srcXinput1_4 = Path.Combine(OpenSteamToolDir, DLL_XINPUT1_4);
                string srcOpenSteamTool = Path.Combine(OpenSteamToolDir, DLL_OpenSteamTool);
                string srcPlugin   = Path.Combine(currentDir, PLUGIN_FOLDER);
                string steamConfig = Path.Combine(steamFolder, "config");
                string destPlugin  = Path.Combine(steamConfig, PLUGIN_FOLDER);

                BeginInvoke(new Action(() => { UpdateStatus("Validating source files..."); progressBar.Value = 5; }));

                if (!Directory.Exists(OpenSteamToolDir))
                {
                    BeginInvoke(new Action(() => UpdateStatus($"Failed: sff/OpenSteamTool folder missing")));
                    MessageBox.Show($"Folder not found: {OpenSteamToolDir}", "Start Process Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!File.Exists(srcDwmapi) || !File.Exists(srcOpenSteamTool))
                {
                    BeginInvoke(new Action(() => UpdateStatus("Failed: DLL(s) missing in sff/OpenSteamTool")));
                    MessageBox.Show($"Missing DLLs in {OpenSteamToolDir}.\nExpected: {DLL_DWMAPI} and {DLL_XINPUT1_4} and {DLL_OpenSteamTool}",
                        "Start Process Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!Directory.Exists(steamConfig))
                {
                    BeginInvoke(new Action(() => UpdateStatus("Failed: Steam config folder missing")));
                    MessageBox.Show($"Steam config folder not found: {steamConfig}", "Start Process Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Step 1: Copy DLLs to Steam folder
                BeginInvoke(new Action(() => { UpdateStatus($"Copying {DLL_DWMAPI} to Steam..."); progressBar.Value = 15; }));
                File.Copy(srcDwmapi, Path.Combine(steamFolder, DLL_DWMAPI), overwrite: true);
				
				BeginInvoke(new Action(() => { UpdateStatus($"Copying {DLL_XINPUT1_4} to Steam..."); progressBar.Value = 30; }));
                File.Copy(srcXinput1_4, Path.Combine(steamFolder, DLL_XINPUT1_4), overwrite: true);

                BeginInvoke(new Action(() => { UpdateStatus($"Copying {DLL_OpenSteamTool} to Steam..."); progressBar.Value = 55; }));
                File.Copy(srcOpenSteamTool, Path.Combine(steamFolder, DLL_OpenSteamTool), overwrite: true);

                // Step 2: Move lua next to the app → steam/config/lua
                //         (only if the local lua folder exists)
                if (Directory.Exists(srcPlugin))
                {
                    BeginInvoke(new Action(() => { UpdateStatus("Moving lua to Steam config..."); progressBar.Value = 75; }));

                    if (Directory.Exists(destPlugin))
                        Directory.Delete(destPlugin, true);

                    CopyDirectory(srcPlugin, destPlugin);
                    Directory.Delete(srcPlugin, true);
                }

                BeginInvoke(new Action(() =>
                {
                    progressBar.Value = 100;
                    UpdateStatus("Start Process completed successfully!");
                    attentionLabel.Text    = "✔ OpenSteamTool installed. Steam will load it automatically on next launch.";
                    attentionLabel.Visible = true;
                }));
            }
            catch (Exception ex)
            {
                BeginInvoke(new Action(() => UpdateStatus($"Failed: {ex.Message}")));
                MessageBox.Show($"Start Process failed:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // End Process (from OpenSteamTool Launch Tool)
        //
        //  Actions:
        //    1. Delete <steam>/dwmapi.dll
        //    2. Delete <steam>/OpenSteamTool.dll
        //    3. Move <steam>/config/lua/ back next to the app
        // ─────────────────────────────────────────────────────────────────────

        private async void EndButton_Click(object? sender, EventArgs e)
        {
            string steamFolder = steamPathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(steamFolder) || !Directory.Exists(steamFolder))
            {
                MessageBox.Show("Please enter a valid Steam folder path.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SetButtonsEnabled(false);
            this.SuspendLayout();
            progressBar.Value   = 0;
            progressBar.Maximum = 100;
            progressBar.Visible = true;
            this.ResumeLayout(false);

            await Task.Run(() => ExecuteEndProcess(steamFolder));

            this.SuspendLayout();
            SetButtonsEnabled(true);
            progressBar.Visible = false;
            this.ResumeLayout(false);
            CheckOpenSteamToolStatus();
        }

        private void ExecuteEndProcess(string steamFolder)
        {
            try
            {
                string currentDir  = AppDomain.CurrentDomain.BaseDirectory;
                string destDwmapi  = Path.Combine(steamFolder, DLL_DWMAPI);
				string destXinput1_4 = Path.Combine(steamFolder, DLL_XINPUT1_4);
                string desttool    = Path.Combine(steamFolder, DLL_OpenSteamTool);
                string steamConfig = Path.Combine(steamFolder, "config");
                string srcPlugin   = Path.Combine(steamConfig, PLUGIN_FOLDER);
                string appPlugin   = Path.Combine(currentDir, PLUGIN_FOLDER);

                BeginInvoke(new Action(() => { UpdateStatus("Starting End Process..."); progressBar.Value = 10; }));
                bool anyAction = false;

                BeginInvoke(new Action(() => { UpdateStatus($"Deleting {DLL_DWMAPI}..."); progressBar.Value = 25; }));
                if (File.Exists(destDwmapi)) { File.Delete(destDwmapi); anyAction = true; }
				
				BeginInvoke(new Action(() => { UpdateStatus($"Deleting {DLL_XINPUT1_4}..."); progressBar.Value = 25; }));
                if (File.Exists(destXinput1_4)) { File.Delete(destXinput1_4); anyAction = true; }

                BeginInvoke(new Action(() => { UpdateStatus($"Deleting {DLL_OpenSteamTool}..."); progressBar.Value = 50; }));
                if (File.Exists(desttool)) { File.Delete(desttool); anyAction = true; }

                BeginInvoke(new Action(() => { UpdateStatus("Moving lua back to app directory..."); progressBar.Value = 70; }));
                if (Directory.Exists(srcPlugin))
                {
                    if (Directory.Exists(appPlugin))
                        Directory.Delete(appPlugin, true);

                    CopyDirectory(srcPlugin, appPlugin);
                    Directory.Delete(srcPlugin, true);
                    anyAction = true;
                }

                string resultMsg = anyAction
                    ? "End Process completed successfully!"
                    : "Nothing to remove — already clean.";

                BeginInvoke(new Action(() =>
                {
                    progressBar.Value = 100;
                    UpdateStatus(resultMsg);
                    attentionLabel.Text    = anyAction
                        ? "✔ OpenSteamTool removed from Steam folder."
                        : "ℹ Steam folder was already clean.";
                    attentionLabel.Visible = true;
                }));
            }
            catch (Exception ex)
            {
                BeginInvoke(new Action(() => UpdateStatus($"Failed: {ex.Message}")));
                MessageBox.Show($"End Process failed:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Game List panel toggle
        // ─────────────────────────────────────────────────────────────────────

        private void GameListButton_Click(object? sender, EventArgs e)
        {
            // True toggle: if the slide-out exists, remove it and shrink the main form back.
            if (_gameListPanel != null && !_gameListPanel.IsDisposed)
            {
                CollapseGameListPanel();
                return;
            }

            string luaDir = IsOpenSteamActive()
                ? Path.Combine(steamPathTextBox.Text.Trim(), "config", PLUGIN_FOLDER)
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PLUGIN_FOLDER);

            ExpandGameListPanel(luaDir);
        }

        private void ExpandGameListPanel(string luaDir)
        {
            if (_gameListPanel != null && !_gameListPanel.IsDisposed) return;

            _gameListPanel = new GameListPanel(luaDir, CollapseGameListPanel)
            {
                Owner = this,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false
            };

            _gameListPanel.FormClosed += (s, e) =>
            {
                _gameListPanel = null;
                gameListTab.Invalidate();
            };

            PositionGameListWindow();
            _gameListPanel.Show(this);
        }

        private void PositionGameListWindow()
        {
            if (_gameListPanel == null || _gameListPanel.IsDisposed) return;

            int extraHeight = (FORM_HEIGHT * 3) / 4; // 75% taller than the main app
            int panelHeight = FORM_HEIGHT + extraHeight;

            _gameListPanel.Size = new Size(GAME_LIST_WIDTH, panelHeight);
            _gameListPanel.Location = new Point(this.Right - 2, this.Top - (extraHeight / 2));
        }

        private void CollapseGameListPanel()
        {
            if (_gameListPanel != null && !_gameListPanel.IsDisposed)
            {
                var panel = _gameListPanel;
                _gameListPanel = null;
                panel.Close();
            }

            gameListTab.Invalidate();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Core apply logic — EditConfig
        //
        //  Lua routing:
        //    - Zip/folder or solo .lua:
        //        OpenSteamTool active   → steam/config/lua/
        //        OpenSteamTool inactive → lua/ next to this app
        //    - Solo .manifest: copied to steam/depotcache/ (normal rules)
        //    - Manifest from zip/folder: always steam/depotcache/ (unchanged)
        // ─────────────────────────────────────────────────────────────────────

        private void EditConfig()
        {
            var steamFolder = steamPathTextBox.Text;
            var luaSource   = luaFolderTextBox.Text.Trim();
            var configPath  = Path.Combine(steamFolder, "config", "config.vdf");

            bool isSoloManifest = File.Exists(luaSource) && luaSource.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase);
            bool isSoloLua      = File.Exists(luaSource) && luaSource.EndsWith(".lua",      StringComparison.OrdinalIgnoreCase);

            // ── Solo manifest: just copy it, nothing else to do ──────────────
            if (isSoloManifest)
            {
                BeginInvoke(new Action(() => { UpdateStatus("Copying manifest file..."); progressBar.Value = 3; }));
                string cache = Path.Combine(steamFolder, "depotcache");
                Directory.CreateDirectory(cache);
                File.Copy(luaSource, Path.Combine(cache, Path.GetFileName(luaSource)), overwrite: true);
                SaveSettings();
                BeginInvoke(new Action(() => UpdateStatus("Done — manifest copied to depotcache.")));
                return;
            }

            BeginInvoke(new Action(() => { UpdateStatus("Copying manifest files..."); progressBar.Value = 1; }));
            CopyManifestFiles(luaSource, steamFolder);

            BeginInvoke(new Action(() => { UpdateStatus("Parsing Lua file..."); progressBar.Value = 2; }));
            var (luaContent, luaFileName) = FindFirstLuaFileContent(luaSource);

            // ── Solo lua: only write the lua file, skip config.vdf editing ───
            if (isSoloLua)
            {
                BeginInvoke(new Action(() => { UpdateStatus("Writing Lua file..."); progressBar.Value = 4; }));
                CopyLuaToDestination(luaSource, luaFileName);
                SaveSettings();
                string dest = IsOpenSteamActive() ? "Steam/config/lua" : "local lua folder";
                BeginInvoke(new Action(() => UpdateStatus($"Done — Lua written to {dest}.")));
                return;
            }

            var entries = ExtractAllAddAppIdValues(luaContent);

            BeginInvoke(new Action(() => { UpdateStatus("Creating backup..."); progressBar.Value = 3; }));
            CreateBackup(configPath);

            BeginInvoke(new Action(() => { UpdateStatus("Injecting decryption keys..."); progressBar.Value = 4; }));
            var lines      = File.ReadAllLines(configPath).ToList();
            int targetLine = lines.FindIndex(line => line.Contains("\"CurrentCellID\""));
            if (targetLine == -1)
                targetLine = lines.FindIndex(line => line.Contains("\"RecentDownloadRate\""));

            int insertIndex = -1;
            for (int i = targetLine - 1; i >= 0; i--)
            {
                if (lines[i].Trim() == "}") { insertIndex = i; break; }
            }

            string indent      = DetectIndentation(lines, insertIndex - 1);
            string innerIndent = indent + "\t";
            var insertBlocks   = new List<string>();

            foreach (var entry in entries)
            {
                insertBlocks.Add($"{indent}\"{entry.AppId}\"");
                insertBlocks.Add($"{indent}{{");
                insertBlocks.Add($"{innerIndent}\"DecryptionKey\"\t\t\"{entry.Key}\"");
                insertBlocks.Add($"{indent}}}");
            }

            lines.InsertRange(insertIndex, insertBlocks);
            File.WriteAllLines(configPath, lines);

            // ── Write Lua → destination based on OpenSteamTool state ─────────
            BeginInvoke(new Action(() => { UpdateStatus("Writing Lua file..."); progressBar.Value = 5; }));
            CopyLuaToDestination(luaSource, luaFileName);

            SaveSettings();
            string luaDestDesc = IsOpenSteamActive() ? "Steam/config/lua" : "local lua folder";
            BeginInvoke(new Action(() => UpdateStatus($"Done — Lua written to {luaDestDesc}.")));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Lua destination helpers
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the directory where lua files should be placed:
        ///   - OpenSteamTool active  → steam/config/lua/
        ///   - OpenSteamTool inactive → lua/ next to this app
        /// </summary>
        private string GetLuaDestinationDir()
        {
            if (IsOpenSteamActive())
                return Path.Combine(steamPathTextBox.Text.Trim(), "config", PLUGIN_FOLDER);
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PLUGIN_FOLDER);
        }

        private void CopyLuaToDestination(string luaSource, string luaFileName)
        {
            string destDir = GetLuaDestinationDir();
            Directory.CreateDirectory(destDir);
            string destPath = Path.Combine(destDir, Path.GetFileName(luaFileName));

            var (rawBytes, _) = FindFirstLuaFile(luaSource);
            File.WriteAllBytes(destPath, rawBytes);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Lua parsing helpers
        // ─────────────────────────────────────────────────────────────────────

        private (byte[] rawBytes, string fileName) FindFirstLuaFile(string path)
        {
            // Solo .lua file dragged/browsed in directly
            if (File.Exists(path) && path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                return (File.ReadAllBytes(path), Path.GetFileName(path));

            if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip   = ZipFile.OpenRead(path);
                var entry       = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
                if (entry == null) throw new FileNotFoundException("No .lua file found in zip.");
                using var stream = entry.Open();
                using var ms    = new MemoryStream();
                stream.CopyTo(ms);
                return (ms.ToArray(), Path.GetFileName(entry.FullName));
            }

            var files = Directory.GetFiles(path, "*.lua");
            if (files.Length == 0) throw new FileNotFoundException("No .lua file found in folder.");
            return (File.ReadAllBytes(files[0]), Path.GetFileName(files[0]));
        }

        private (string content, string fileName) FindFirstLuaFileContent(string path)
        {
            var (raw, name) = FindFirstLuaFile(path);
            string content = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF
                ? Encoding.UTF8.GetString(raw, 3, raw.Length - 3)
                : Encoding.UTF8.GetString(raw);
            return (content, name);
        }

        private List<AppEntry> ExtractAllAddAppIdValues(string content)
        {
            var pattern = @"addappid\s*\(\s*(\d+)\s*,\s*[01]\s*,\s*[""']([a-fA-F0-9]{64})[""']\s*\)";
            return Regex.Matches(content, pattern, RegexOptions.IgnoreCase)
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => new AppEntry { AppId = m.Groups[1].Value, Key = m.Groups[2].Value })
                .ToList();
        }

        private string CreateBackup(string path)
        {
            var b = $"{path}.bak_{DateTime.Now:yyyyMMdd_HHmmss}";
            File.Copy(path, b);
            return b;
        }

        private void CopyManifestFiles(string sourcePath, string steamFolder)
        {
            // Solo .lua file — no manifests to copy
            if (File.Exists(sourcePath) && sourcePath.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                return;

            var cache = Path.Combine(steamFolder, "depotcache");
            Directory.CreateDirectory(cache);

            if (File.Exists(sourcePath) && sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(sourcePath);
                foreach (var e in zip.Entries.Where(e => e.FullName.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)))
                    e.ExtractToFile(Path.Combine(cache, Path.GetFileName(e.FullName)), true);
            }
            else if (Directory.Exists(sourcePath))
            {
                foreach (var f in Directory.GetFiles(sourcePath, "*.manifest"))
                    File.Copy(f, Path.Combine(cache, Path.GetFileName(f)), true);
            }
        }

        private int GetManifestFileCount(string path)
        {
            if (File.Exists(path) && path.EndsWith(".lua",      StringComparison.OrdinalIgnoreCase)) return 0;
            if (File.Exists(path) && path.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)) return 1;
            if (File.Exists(path) && path.EndsWith(".zip",      StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(path);
                return zip.Entries.Count(e => e.FullName.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase));
            }
            return Directory.Exists(path) ? Directory.GetFiles(path, "*.manifest").Length : 0;
        }

        private string DetectIndentation(List<string> lines, int index)
        {
            if (index < 0) return "\t\t\t\t\t";
            var m = Regex.Match(lines[index], @"^(\s*)");
            return m.Success ? m.Groups[1].Value : "\t\t\t\t\t";
        }

        // ─────────────────────────────────────────────────────────────────────
        // Directory copy helper
        // ─────────────────────────────────────────────────────────────────────

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            foreach (string subDir in Directory.GetDirectories(sourceDir))
                CopyDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
        }

        // ─────────────────────────────────────────────────────────────────────
        // Settings
        // ─────────────────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            if (File.Exists(SETTINGS_FILE))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SETTINGS_FILE));
                if (settings != null)
                    steamPathTextBox.Text = settings.SteamFolder;
            }

            // Always update status on load — shows install state or prompts for a path
            CheckOpenSteamToolStatus();
        }

        private void SaveSettings()
        {
            File.WriteAllText(
                SETTINGS_FILE,
                JsonSerializer.Serialize(
                    new Settings { SteamFolder = steamPathTextBox.Text },
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        public class AppEntry
        {
            public string AppId { get; set; } = "";
            public string Key   { get; set; } = "";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Source selection dialog (unchanged from OpenSteam)
    // ─────────────────────────────────────────────────────────────────────────

    public class SourceSelectionDialog : Form
    {
        public SourceSelectionDialog()
        {
            this.Text          = "Select Source Type";
            this.Size          = new Size(360, 155);
            this.StartPosition = FormStartPosition.CenterParent;
            this.ControlBox    = false;
            this.BackColor     = Theme.BgPanel;

            var prompt = new Label
            {
                Text      = "Choose the source type for Lua and Manifest files:",
                Location  = new Point(16, 20),
                AutoSize  = true,
                ForeColor = Theme.TextMuted,
                Font      = new Font("Segoe UI", 9.25f)
            };

            var folder = new Guna2Button
            {
                Text      = "Folder",
                Location  = new Point(16, 66),
                Size      = new Size(100, 32),
                FillColor = Theme.Orange,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
            };

            var zip = new Guna2Button
            {
                Text      = "Zip File",
                Location  = new Point(126, 66),
                Size      = new Size(100, 32),
                FillColor = Theme.GreyMid,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
            };

            var cancel = new Guna2Button
            {
                Text      = "Cancel",
                Location  = new Point(236, 66),
                Size      = new Size(100, 32),
                FillColor = Color.FromArgb(55, 55, 62),
                ForeColor = Theme.TextMuted,
                Font      = new Font("Segoe UI", 9.5f)
            };

            FloatingUiStyle.AddFloatingShadow(folder, 18);
            FloatingUiStyle.AddFloatingShadow(zip,    18);
            FloatingUiStyle.AddFloatingShadow(cancel, 18);

            folder.Click += (s, e) => { this.DialogResult = DialogResult.OK;     this.Close(); };
            zip.Click    += (s, e) => { this.DialogResult = DialogResult.Yes;    this.Close(); };
            cancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            this.Controls.AddRange(new Control[] { prompt, folder, zip, cancel });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Game List Panel — shows .lua files from steam/config/lua
    //   Columns : Lua Name | Game Name
    //   Button  : Delete Selected
    //   Source  : steam path is passed in from MainForm (no browse box needed)
    // ─────────────────────────────────────────────────────────────────────────

    public class GameListPanel : Form
    {
        private readonly string _luaDir;
        private readonly Action _closeRequested;

        private Guna2DataGridView _grid       = null!;
        private Guna2Button       _btnDelete  = null!;
        private Guna2Button       _btnRefresh = null!;
        private Guna2Button       _btnClose   = null!;
        private Label             _lblTitle   = null!;
        private Label             _lblPath    = null!;
        private PictureBox        _bgLayer    = null!;
        private Image?            _backgroundImage;

        private bool  _dragging;
        private Point _dragCursor;
        private Point _dragForm;

        public GameListPanel(string luaDir, Action closeRequested)
        {
            _luaDir = luaDir;
            _closeRequested = closeRequested;
            BuildUi();
            _ = LoadLuaFilesAsync();
        }

        private void BuildUi()
        {
            Text            = "Game List";
            Size            = new Size(430, 480);
            MinimumSize     = new Size(370, 400);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition   = FormStartPosition.Manual;
            ShowInTaskbar   = false;
            BackColor       = Theme.BgDark;
            DoubleBuffered  = true;

            new Guna2BorderlessForm
            {
                ContainerControl = this,
                BorderRadius     = 14,
                TransparentWhileDrag = false
            };

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            _backgroundImage = LoadGameListBackgroundImage();

            _bgLayer = new PictureBox
            {
                Dock      = DockStyle.Fill,
                SizeMode  = PictureBoxSizeMode.StretchImage,
                Image     = _backgroundImage,
                BackColor = Theme.BgDark
            };
            Controls.Add(_bgLayer);
            _bgLayer.SendToBack();

            // Title
            _lblTitle = new Label
            {
                Text      = "GAME LIST",
                AutoSize  = true,
                Font      = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Theme.Orange,
                BackColor = Color.Transparent,
                Parent    = this,
                Location  = new Point(18, 15)
            };

            // Close button
            _btnClose = MakeButton("✕", 14);
            _btnClose.FillColor = Theme.Red;
            _btnClose.HoverState.FillColor = Theme.RedHov;
            _btnClose.Click += (s, e) => _closeRequested();

            // Path label
            _lblPath = new Label
            {
                Text      = string.IsNullOrWhiteSpace(_luaDir)
                                ? "⚠  No Steam path set — enter the Steam folder in the main app first."
                                : $"Reading:  {_luaDir}",
                AutoSize  = false,
                ForeColor = Theme.TextMuted,
                BackColor = Color.Transparent,
                Font      = new Font("Segoe UI", 8.75f),
                TextAlign = ContentAlignment.MiddleLeft
            };

            // Refresh button
            _btnRefresh = MakeButton("⟳ Refresh", 14);
            _btnRefresh.Click += async (s, e) => await LoadLuaFilesAsync();

            // Grid
            _grid = new Guna2DataGridView
            {
                BackgroundColor          = Theme.BgPanel,
                BorderStyle              = System.Windows.Forms.BorderStyle.None,
                CellBorderStyle          = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                GridColor                = Theme.GreyBorder,
                AllowUserToAddRows       = false,
                AllowUserToDeleteRows    = false,
                AllowUserToResizeRows    = false,
                AllowUserToResizeColumns = false,
                ReadOnly                 = true,
                MultiSelect              = true,
                SelectionMode            = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible        = false,
                AutoSizeColumnsMode      = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles= false,
                ScrollBars               = ScrollBars.Vertical,
                ColumnHeadersHeight      = 36,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                BackColor                = Theme.BgPanel,
                Visible                  = false
            };

            _grid.ColumnHeadersDefaultCellStyle.BackColor          = Theme.BgDark;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor          = Theme.Orange;
            _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.BgDark;
            _grid.ColumnHeadersDefaultCellStyle.Font               = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
            _grid.ColumnHeadersDefaultCellStyle.Padding            = new Padding(8, 2, 6, 2);

            _grid.DefaultCellStyle.BackColor          = Theme.BgPanel;
            _grid.DefaultCellStyle.ForeColor          = Theme.TextPrimary;
            _grid.DefaultCellStyle.SelectionBackColor = Theme.OrangeDim;
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;
            _grid.DefaultCellStyle.Font               = new Font("Segoe UI", 10f);
            _grid.DefaultCellStyle.Padding            = new Padding(8, 2, 6, 2);

            _grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.BgDark;
            _grid.AlternatingRowsDefaultCellStyle.ForeColor = Theme.TextPrimary;
            _grid.RowTemplate.Height = 34;

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name       = "LuaName",
                HeaderText = "Lua File",
                FillWeight = 34,
                SortMode   = DataGridViewColumnSortMode.Automatic
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name       = "GameName",
                HeaderText = "Game Name",
                FillWeight = 66,
                SortMode   = DataGridViewColumnSortMode.Automatic
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name    = "FilePath",
                Visible = false
            });

            _grid.SelectionChanged += (s, e) =>
                _btnDelete.Enabled = _grid.SelectedRows.Count > 0;

            _btnDelete = MakeButton("Delete Selected", 18);
            _btnDelete.FillColor = Theme.Red;
            _btnDelete.HoverState.FillColor = Theme.RedHov;
            _btnDelete.Enabled = false;
            _btnDelete.Visible = false;
            _btnDelete.Click += BtnDelete_Click;

            Controls.AddRange(new Control[]
            {
                _lblTitle, _btnClose, _lblPath, _btnRefresh, _grid, _btnDelete
            });
            _bgLayer.SendToBack();

            _lblTitle.MouseDown += BeginDrag;
            _lblTitle.MouseMove += ContinueDrag;
            _lblTitle.MouseUp   += EndDrag;
            this.MouseDown      += BeginDrag;
            this.MouseMove      += ContinueDrag;
            this.MouseUp        += EndDrag;

            Resize      += (s, e) => LayoutUi();
            HandleCreated += (s, e) => { LayoutUi(); _grid.ClearSelection(); };
        }

        private Image? LoadGameListBackgroundImage()
        {
            // gamelist.png must be embedded into the EXE the same way background.png is.
            // Required in the .csproj:
            //   <EmbeddedResource Include="gamelist.png" />
            try
            {
                var assembly = Assembly.GetExecutingAssembly();

                string? resource = assembly.GetManifestResourceNames()
                    .FirstOrDefault(r => r.EndsWith("gamelist.png", StringComparison.OrdinalIgnoreCase));

                if (resource == null)
                    return null;

                using Stream? stream = assembly.GetManifestResourceStream(resource);
                if (stream == null)
                    return null;

                using var temp = Image.FromStream(stream);
                return new Bitmap(temp);
            }
            catch
            {
                return null;
            }
        }

        private void BeginDrag(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _dragCursor = Cursor.Position;
            _dragForm = Location;
        }

        private void ContinueDrag(object? sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Location = Point.Add(_dragForm, new Size(Point.Subtract(Cursor.Position, new Size(_dragCursor))));
        }

        private void EndDrag(object? sender, MouseEventArgs e)
        {
            _dragging = false;
        }

        private void LayoutUi()
        {
            int pad = 16;

            _lblTitle.Location = new Point(pad + 4, 14);

            _btnClose.SetBounds(ClientSize.Width - pad - 38, 12, 38, 30);
            _btnRefresh.SetBounds(_btnClose.Left - 10 - 110, 12, 110, 30);

            int pathTop = _lblTitle.Bottom + 4;
            _lblPath.SetBounds(pad, pathTop, ClientSize.Width - pad * 2, 26);

            // thin divider line painted separately via background — just space it
            int gridTop    = pathTop + 34;
            int actionH    = 38;
            int actionY    = ClientSize.Height - pad - actionH;
            int gridHeight = actionY - 8 - gridTop;

            _grid.SetBounds(pad, gridTop, ClientSize.Width - pad * 2, Math.Max(90, gridHeight));
            _btnDelete.SetBounds(pad, actionY, 170, actionH);
        }

        private Guna2Button MakeButton(string text, int radius)
        {
            var btn = new Guna2Button
            {
                Text         = text,
                BorderRadius = radius,
                FillColor    = Theme.Orange,
                HoverState   = { FillColor = Theme.OrangeHov },
                Font         = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor    = Color.White,
                Cursor       = Cursors.Hand,
                BackColor    = Color.Transparent,
                UseTransparentBackground = true
            };

            btn.ShadowDecoration.Enabled = false;
            btn.BorderThickness = 0;
            return btn;
        }

        private async Task LoadLuaFilesAsync()
        {
            _grid.Rows.Clear();
            _btnDelete.Enabled = false;

            if (string.IsNullOrWhiteSpace(_luaDir) || !Directory.Exists(_luaDir))
            {
                _lblPath.Text = string.IsNullOrWhiteSpace(_luaDir)
                    ? "⚠  No Steam path set — enter the Steam folder in the main app first."
                    : $"⚠  Folder not found:  {_luaDir}";

                _grid.Visible = false;
                _btnDelete.Visible = false;
                return;
            }

            _lblPath.Text = $"Reading:  {_luaDir}";

            var luaFiles = Directory.GetFiles(_luaDir, "*.lua")
                .Where(f => !Path.GetFileName(f).Equals("00_LetUpdate_override.lua", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f)
                .ToList();

            bool hasRows = luaFiles.Count > 0;
            _grid.Visible = hasRows;
            _btnDelete.Visible = hasRows;

            if (!hasRows)
                return;

            _lblPath.Text = $"Reading:  {_luaDir}  — loading game names...";
            _btnRefresh.Enabled = false;

            try
            {
                foreach (var filePath in luaFiles)
                {
                    string luaName  = Path.GetFileName(filePath);
                    string gameName = await GetGameNameForLuaFileAsync(filePath);

                    if (_grid.IsDisposed) return;
                    _grid.Rows.Add(luaName, gameName, filePath);
                }
            }
            finally
            {
                if (!_btnRefresh.IsDisposed)
                    _btnRefresh.Enabled = true;
            }

            _lblPath.Text = $"Reading:  {_luaDir}";
            _grid.ClearSelection();
        }

        private static async Task<string> GetGameNameForLuaFileAsync(string filePath)
        {
            string? commentName = TryExtractGameNameFromLuaComment(filePath);
            if (!string.IsNullOrWhiteSpace(commentName))
                return commentName;

            string? appId = TryExtractFirstAppId(filePath);
            if (string.IsNullOrWhiteSpace(appId))
                return "";

            string? steamApiName = await SteamStoreNameLookup.GetGameNameAsync(appId);
            if (!string.IsNullOrWhiteSpace(steamApiName))
                return steamApiName;

            return $"AppID {appId}";
        }

        private static string? TryExtractGameNameFromLuaComment(string filePath)
        {
            try
            {
                foreach (var line in File.ReadLines(filePath))
                {
                    // Preferred format:
                    //   addappid(21690) -- Resident Evil 5
                    var addAppComment = Regex.Match(
                        line,
                        @"addappid\s*\(\s*\d+[^\)]*\)\s*--\s*(.+)$",
                        RegexOptions.IgnoreCase);

                    if (addAppComment.Success)
                    {
                        string name = addAppComment.Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                            return name;
                    }

                    // Older/alternate format:
                    //   -- Game Name: Resident Evil 5
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith("--", StringComparison.Ordinal))
                        continue;

                    var namedComment = Regex.Match(
                        trimmed,
                        @"--\s*(?:game\s*name?|name)\s*[:\-]\s*(.+)",
                        RegexOptions.IgnoreCase);

                    if (namedComment.Success)
                    {
                        string name = namedComment.Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                            return name;
                    }
                }
            }
            catch { }

            return null;
        }

        private static string? TryExtractFirstAppId(string filePath)
        {
            try
            {
                string content = File.ReadAllText(filePath);
                var appIdMatch = Regex.Match(content, @"addappid\s*\(\s*(\d+)", RegexOptions.IgnoreCase);
                if (appIdMatch.Success)
                    return appIdMatch.Groups[1].Value;

                string fileNameAppId = Path.GetFileNameWithoutExtension(filePath);
                return long.TryParse(fileNameAppId, out _) ? fileNameAppId : null;
            }
            catch
            {
                return null;
            }
        }

        private static class SteamStoreNameLookup
        {
            private static readonly HttpClient _http = new HttpClient();
            private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>();

            static SteamStoreNameLookup()
            {
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                _http.Timeout = TimeSpan.FromSeconds(15);
            }

            public static async Task<string?> GetGameNameAsync(string appId)
            {
                if (string.IsNullOrWhiteSpace(appId))
                    return null;

                if (_cache.TryGetValue(appId, out string? cached))
                    return cached;

                try
                {
                    string url = $"https://store.steampowered.com/api/appdetails?appids={appId}";
                    string json = await _http.GetStringAsync(url);

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty(appId, out var appData) ||
                        !appData.TryGetProperty("success", out var successProp) ||
                        !successProp.GetBoolean() ||
                        !appData.TryGetProperty("data", out var dataNode) ||
                        !dataNode.TryGetProperty("name", out var nameProp))
                    {
                        return null;
                    }

                    string? name = nameProp.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        _cache[appId] = name;
                        return name;
                    }
                }
                catch { }

                return null;
            }
        }

        private void BtnDelete_Click(object? sender, EventArgs e)
        {
            var selected = _grid.SelectedRows
                .Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r => new
                {
                    LuaName  = r.Cells["LuaName"].Value?.ToString()  ?? "",
                    FilePath = r.Cells["FilePath"].Value?.ToString() ?? ""
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.FilePath))
                .ToList();

            if (selected.Count == 0) return;

            string preview = string.Join(Environment.NewLine,
                selected.Take(10).Select(x => x.LuaName));
            if (selected.Count > 10)
                preview += Environment.NewLine + $"...and {selected.Count - 10} more";

            var confirm = MessageBox.Show(
                $"Permanently delete {selected.Count} Lua file{(selected.Count == 1 ? "" : "s")}?\n\n{preview}",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            var errors = new List<string>();
            foreach (var item in selected)
            {
                try
                {
                    if (File.Exists(item.FilePath))
                        File.Delete(item.FilePath);
                }
                catch (Exception ex)
                {
                    errors.Add($"{item.LuaName}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
                MessageBox.Show(
                    "Some files could not be deleted:\n\n" + string.Join("\n", errors),
                    "Delete Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            _ = LoadLuaFilesAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Entry point
    // ─────────────────────────────────────────────────────────────────────────

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}