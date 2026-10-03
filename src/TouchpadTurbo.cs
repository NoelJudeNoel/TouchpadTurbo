using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TouchpadTurbo
{
    public class MainForm : Form
    {
        // --- Win32 Hook Constants & Structs ---
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_HOTKEY = 0x0312;

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;

        private const uint LLMHF_INJECTED = 0x0001;
        private const uint MAGIC_ID = 0x54555242; // "TURB"

        private const int HOTKEY_TOGGLE_ID = 9001;
        private const uint MOD_CTRL_ALT = 0x0003;
        private const uint VK_END = 0x23;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // --- Settings State ---
        public static double BaseMultiplier = 2.8;
        public static double FlickBoost = 3.5;
        public static bool PrecisionMode = true;
        public static bool ClickProtect = true;
        public static bool SwipeTapFilter = true;
        public static bool IsEnabled = true;

        private static IntPtr _hookID = IntPtr.Zero;
        private static LowLevelMouseProc _staticProc;
        private static GCHandle _gcHandle;
        private static POINT _lastPt;
        private static bool _hasPt = false;

        // Button state & filter tracking
        private static bool _isButtonDown = false;
        private static int _buttonUpTime = 0;
        private static int _lastFastMoveTime = 0;
        private static double _lastFastMoveDist = 0;
        private static bool _dropNextLButtonUp = false;
        private static int _dropTime = 0;

        private static readonly string ConfigPath = @"C:\Tools\TouchpadTurbo\config.ini";

        // DPI Scaling Factor
        private float _scale = 1.0f;
        private int S(int val) { return (int)Math.Round(val * _scale); }

        // --- UI Controls ---
        private NotifyIcon _trayIcon;
        private Label _lblStatus;
        private Label _lblBaseVal;
        private Label _lblFlickVal;
        private Label _lblBaseSub;
        private Label _lblFlickSub;
        private TrackBar _tbBase;
        private TrackBar _tbFlick;
        private CheckBox _chkPrecision;
        private CheckBox _chkClickProtect;
        private CheckBox _chkSwipeTapFilter;
        private CheckBox _chkAutoStart;
        private Label _lblTouchpadAAP;
        private Button _btnOptimizeAAP;
        private Button _btnToggle;

        public MainForm()
        {
            SetProcessDPIAware();
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                _scale = g.DpiX / 96.0f;
            }

            LoadConfig();

            // Set up Low-Level Mouse Hook
            _staticProc = HookCallback;
            _gcHandle = GCHandle.Alloc(_staticProc);
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                _hookID = SetWindowsHookEx(WH_MOUSE_LL, _staticProc, GetModuleHandle(curModule.ModuleName), 0);
            }

            BuildUI();
            InitTray();

            RegisterHotKey(this.Handle, HOTKEY_TOGGLE_ID, MOD_CTRL_ALT, VK_END);
        }

        private void BuildUI()
        {
            this.Text = "TouchpadTurbo - 触摸板极速倍增调节器 v1.1";
            this.ClientSize = new Size(S(500), S(600));
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(24, 25, 32);
            this.ForeColor = Color.White;
            this.Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular);

            // 1. Header Banner
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = S(72),
                BackColor = Color.FromArgb(32, 34, 46)
            };
            var lblTitle = new Label
            {
                Text = "⚡ TouchpadTurbo 灵敏度调节",
                Font = new Font("Microsoft YaHei UI", 12.0f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 180, 216),
                Location = new Point(S(20), S(12)),
                AutoSize = true
            };
            var lblSubtitle = new Label
            {
                Text = "专为蓝牙触摸板 + 2.5K/4K 高分屏打造的物理倍增引擎 (防失焦/防误触强化版)",
                Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(160, 160, 175),
                Location = new Point(S(22), S(42)),
                AutoSize = true
            };
            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSubtitle);
            this.Controls.Add(pnlHeader);

            int curY = S(82);
            int cardW = S(460);
            int cardX = S(20);

            // 2. Card 1: Base Multiplier
            var grpBase = CreateCard(cardX, curY, cardW, S(110));
            var lblBaseTitle = new Label
            {
                Text = "常态轻推倍率 (Base Multiplier)",
                Location = new Point(S(14), S(10)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold)
            };
            _lblBaseVal = new Label
            {
                Text = string.Format("{0:F1}x", BaseMultiplier),
                Location = new Point(cardW - S(70), S(8)),
                Size = new Size(S(60), S(22)),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Microsoft YaHei UI", 11.0f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 180, 216)
            };
            _tbBase = new TrackBar
            {
                Location = new Point(S(8), S(34)),
                Width = cardW - S(16),
                Height = S(45),
                Minimum = 10,
                Maximum = 60,
                Value = Math.Max(10, Math.Min(60, (int)Math.Round(BaseMultiplier * 10))),
                TickFrequency = 5
            };
            _lblBaseSub = new Label
            {
                Text = string.Format("手指轻移 1cm = 屏幕光标移动 {0:F1}cm 像素量 (支持 1.0x ~ 6.0x)", BaseMultiplier),
                Location = new Point(S(14), S(82)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.2f),
                ForeColor = Color.FromArgb(160, 160, 175)
            };
            _tbBase.ValueChanged += (s, e) =>
            {
                BaseMultiplier = _tbBase.Value / 10.0;
                _lblBaseVal.Text = string.Format("{0:F1}x", BaseMultiplier);
                _lblBaseSub.Text = string.Format("手指轻移 1cm = 屏幕光标移动 {0:F1}cm 像素量 (支持 1.0x ~ 6.0x)", BaseMultiplier);
                UpdateStatus();
                SaveConfig();
            };
            grpBase.Controls.Add(lblBaseTitle);
            grpBase.Controls.Add(_lblBaseVal);
            grpBase.Controls.Add(_tbBase);
            grpBase.Controls.Add(_lblBaseSub);
            this.Controls.Add(grpBase);

            curY += S(120);

            // 3. Card 2: Flick Acceleration
            var grpFlick = CreateCard(cardX, curY, cardW, S(110));
            var lblFlickTitle = new Label
            {
                Text = "快速甩指额外爆发 (Flick Boost)",
                Location = new Point(S(14), S(10)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold)
            };
            _lblFlickVal = new Label
            {
                Text = string.Format("+{0:F1}x", FlickBoost),
                Location = new Point(cardW - S(70), S(8)),
                Size = new Size(S(60), S(22)),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Microsoft YaHei UI", 11.0f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 179, 0)
            };
            _tbFlick = new TrackBar
            {
                Location = new Point(S(8), S(34)),
                Width = cardW - S(16),
                Height = S(45),
                Minimum = 0,
                Maximum = 60,
                Value = Math.Max(0, Math.Min(60, (int)Math.Round(FlickBoost * 10))),
                TickFrequency = 5
            };
            _lblFlickSub = new Label
            {
                Text = string.Format("手指甩动时动态爆发加速 (当前极速可达 {0:F1}x)", BaseMultiplier + FlickBoost),
                Location = new Point(S(14), S(82)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.2f),
                ForeColor = Color.FromArgb(160, 160, 175)
            };
            _tbFlick.ValueChanged += (s, e) =>
            {
                FlickBoost = _tbFlick.Value / 10.0;
                _lblFlickVal.Text = string.Format("+{0:F1}x", FlickBoost);
                _lblFlickSub.Text = string.Format("手指甩动时动态爆发加速 (当前极速可达 {0:F1}x)", BaseMultiplier + FlickBoost);
                SaveConfig();
            };
            grpFlick.Controls.Add(lblFlickTitle);
            grpFlick.Controls.Add(_lblFlickVal);
            grpFlick.Controls.Add(_tbFlick);
            grpFlick.Controls.Add(_lblFlickSub);
            this.Controls.Add(grpFlick);

            curY += S(120);

            // 4. Card 3: Modes & Anti-Interference Protections
            var grpOpt = CreateCard(cardX, curY, cardW, S(206));

            _chkClickProtect = new CheckBox
            {
                Text = "点击 / 拖拽原生保护 (按住按键自动1:1原生，彻底修复Ditto剪贴板与选区)",
                Checked = ClickProtect,
                Location = new Point(S(14), S(10)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 220, 240)
            };
            _chkClickProtect.CheckedChanged += (s, e) => { ClickProtect = _chkClickProtect.Checked; SaveConfig(); };

            _chkSwipeTapFilter = new CheckBox
            {
                Text = "快速划过防误点击 (滑动时阻断偶发误触轻击，防止看视频误暂停/失焦)",
                Checked = SwipeTapFilter,
                Location = new Point(S(14), S(34)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 205, 110)
            };
            _chkSwipeTapFilter.CheckedChanged += (s, e) => { SwipeTapFilter = _chkSwipeTapFilter.Checked; SaveConfig(); };

            _chkPrecision = new CheckBox
            {
                Text = "微操智能平抑 (慢移微动自动平稳，方便精准微调选中)",
                Checked = PrecisionMode,
                Location = new Point(S(14), S(58)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.8f),
                ForeColor = Color.FromArgb(220, 220, 230)
            };
            _chkPrecision.CheckedChanged += (s, e) => { PrecisionMode = _chkPrecision.Checked; SaveConfig(); };

            _chkAutoStart = new CheckBox
            {
                Text = "开机静默自启动 (登录后后台持续生效)",
                Checked = IsAutoStartEnabled(),
                Location = new Point(S(14), S(82)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.8f),
                ForeColor = Color.FromArgb(220, 220, 230)
            };
            _chkAutoStart.CheckedChanged += (s, e) => { SetAutoStart(_chkAutoStart.Checked); };

            // AAP Touchpad Sensitivity status row
            int currentAAP = GetTouchpadSensitivity();
            _lblTouchpadAAP = new Label
            {
                Text = string.Format("系统触摸板灵敏度: {0}", currentAAP == 0 ? "最灵敏 (AAP=0, 极易误触)" : (currentAAP == 1 ? "高灵敏度" : "标准推荐 (AAP=2)")),
                Location = new Point(S(14), S(112)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.5f),
                ForeColor = (currentAAP == 0 ? Color.FromArgb(255, 138, 128) : Color.FromArgb(129, 199, 132))
            };

            _btnOptimizeAAP = new Button
            {
                Text = (currentAAP == 0 ? "一键设为标准推荐 (AAP=2)" : "设为最灵敏 (AAP=0)"),
                Location = new Point(cardW - S(190), S(108)),
                Size = new Size(S(175), S(26)),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 8.2f),
                BackColor = Color.FromArgb(48, 52, 70),
                ForeColor = Color.White
            };
            _btnOptimizeAAP.Click += (s, e) =>
            {
                int nowAAP = GetTouchpadSensitivity();
                int targetAAP = (nowAAP == 0) ? 2 : 0;
                SetTouchpadSensitivity(targetAAP);
                int updatedAAP = GetTouchpadSensitivity();
                _lblTouchpadAAP.Text = string.Format("系统触摸板灵敏度: {0}", updatedAAP == 0 ? "最灵敏 (AAP=0, 极易误触)" : "标准推荐 (AAP=2)");
                _lblTouchpadAAP.ForeColor = (updatedAAP == 0 ? Color.FromArgb(255, 138, 128) : Color.FromArgb(129, 199, 132));
                _btnOptimizeAAP.Text = (updatedAAP == 0 ? "一键设为标准推荐 (AAP=2)" : "设为最灵敏 (AAP=0)");
                MessageBox.Show(
                    targetAAP == 2 
                        ? "已将 Windows 系统触摸板防误触优化为 [标准推荐 (AAP=2)]！\n注: 该项由系统触摸板驱动读取，注销或重启后系统级完全生效。"
                        : "已切换为 [最灵敏 (AAP=0)]。\n注: 最灵敏模式下手掌轻触或划过容易被系统驱动误判为轻击。",
                    "触摸板灵敏度设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            var lblAAPTip = new Label
            {
                Text = "说明: 极灵敏(AAP=0)易在滑动抬指时被系统驱动误判为轻击；配合本软件倍增已无需超敏。",
                Location = new Point(S(14), S(140)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 7.8f),
                ForeColor = Color.FromArgb(140, 140, 160)
            };

            var lblHotkeyHint = new Label
            {
                Text = "提示: 键盘随时按下 [ Ctrl + Alt + End ] 可一键暂停/恢复倍增",
                Location = new Point(S(14), S(175)),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.2f),
                ForeColor = Color.FromArgb(160, 160, 185)
            };

            grpOpt.Controls.Add(_chkClickProtect);
            grpOpt.Controls.Add(_chkSwipeTapFilter);
            grpOpt.Controls.Add(_chkPrecision);
            grpOpt.Controls.Add(_chkAutoStart);
            grpOpt.Controls.Add(_lblTouchpadAAP);
            grpOpt.Controls.Add(_btnOptimizeAAP);
            grpOpt.Controls.Add(lblAAPTip);
            grpOpt.Controls.Add(lblHotkeyHint);
            this.Controls.Add(grpOpt);

            curY += S(216);

            // 5. Bottom Status and Action Buttons
            _lblStatus = new Label
            {
                Text = "● 状态: 倍增中",
                Location = new Point(S(22), curY + S(6)),
                Size = new Size(S(160), S(30)),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(76, 175, 80)
            };
            this.Controls.Add(_lblStatus);

            _btnToggle = new Button
            {
                Text = "暂停",
                Location = new Point(S(245), curY + S(4)),
                Size = new Size(S(105), S(36)),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Bold),
                BackColor = Color.FromArgb(48, 50, 64),
                ForeColor = Color.White
            };
            _btnToggle.Click += (s, e) =>
            {
                IsEnabled = !IsEnabled;
                UpdateStatus();
                SaveConfig();
            };
            this.Controls.Add(_btnToggle);

            var btnMinimize = new Button
            {
                Text = "最小化到托盘",
                Location = new Point(S(360), curY + S(4)),
                Size = new Size(S(120), S(36)),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 150, 199),
                ForeColor = Color.White
            };
            btnMinimize.Click += (s, e) =>
            {
                this.Hide();
                _trayIcon.ShowBalloonTip(1500, "TouchpadTurbo 已常驻托盘", "双击托盘图标可随时调出此设置面板。", ToolTipIcon.Info);
            };
            this.Controls.Add(btnMinimize);
        }

        private Panel CreateCard(int x, int y, int w, int h)
        {
            return new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = Color.FromArgb(34, 36, 48)
            };
        }

        private void InitTray()
        {
            _trayIcon = new NotifyIcon();
            _trayIcon.Text = string.Format("TouchpadTurbo ({0:F1}x) - 触摸板倍增器", BaseMultiplier);

            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.FillEllipse(Brushes.DeepSkyBlue, 1, 1, 14, 14);
                using (Font font = new Font("Arial", 8, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.DrawString("⚡", font, Brushes.White, 2, 2);
                }
            }
            _trayIcon.Icon = Icon.FromHandle(bmp.GetHicon());
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.BringToFront();
            };

            var menu = new ContextMenuStrip();
            var titleItem = new ToolStripMenuItem("打开调节面板...", null, (s, e) =>
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.BringToFront();
            });
            titleItem.Font = new Font(titleItem.Font, FontStyle.Bold);
            menu.Items.Add(titleItem);
            menu.Items.Add(new ToolStripSeparator());

            double[] presets = new double[] { 1.5, 2.0, 2.5, 2.8, 3.2, 4.0 };
            foreach (var preset in presets)
            {
                double p = preset;
                var item = new ToolStripMenuItem(string.Format("{0:F1}x 快捷灵敏度", p), null, (s, e) =>
                {
                    BaseMultiplier = p;
                    _tbBase.Value = (int)Math.Round(p * 10);
                    _lblBaseVal.Text = string.Format("{0:F1}x", BaseMultiplier);
                    _lblBaseSub.Text = string.Format("手指轻移 1cm = 屏幕光标移动 {0:F1}cm 像素量 (支持 1.0x ~ 6.0x)", BaseMultiplier);
                    UpdateStatus();
                    SaveConfig();
                });
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());

            var protectItem = new ToolStripMenuItem("点击/拖拽原生保护 (防止Ditto等失效)", null, (s, e) =>
            {
                ClickProtect = !ClickProtect;
                ((ToolStripMenuItem)s).Checked = ClickProtect;
                if (_chkClickProtect != null) _chkClickProtect.Checked = ClickProtect;
                SaveConfig();
            }) { Checked = ClickProtect };
            menu.Items.Add(protectItem);

            var filterItem = new ToolStripMenuItem("滑动防误点击 (防止看视频等误暂停)", null, (s, e) =>
            {
                SwipeTapFilter = !SwipeTapFilter;
                ((ToolStripMenuItem)s).Checked = SwipeTapFilter;
                if (_chkSwipeTapFilter != null) _chkSwipeTapFilter.Checked = SwipeTapFilter;
                SaveConfig();
            }) { Checked = SwipeTapFilter };
            menu.Items.Add(filterItem);

            menu.Items.Add(new ToolStripSeparator());

            var toggleItem = new ToolStripMenuItem("启用 / 暂停倍增 (Ctrl+Alt+End)", null, (s, e) =>
            {
                IsEnabled = !IsEnabled;
                UpdateStatus();
                SaveConfig();
            });
            menu.Items.Add(toggleItem);

            var exitItem = new ToolStripMenuItem("退出", null, (s, e) =>
            {
                _trayIcon.Visible = false;
                Application.Exit();
            });
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
        }

        private void UpdateStatus()
        {
            if (IsEnabled)
            {
                _lblStatus.Text = string.Format("● 状态: 倍增中 ({0:F1}x)", BaseMultiplier);
                _lblStatus.ForeColor = Color.FromArgb(76, 175, 80);
                _btnToggle.Text = "暂停";
                _btnToggle.BackColor = Color.FromArgb(48, 50, 64);
                _trayIcon.Text = string.Format("TouchpadTurbo ({0:F1}x) - 运行中", BaseMultiplier);
            }
            else
            {
                _lblStatus.Text = "● 状态: 已暂停";
                _lblStatus.ForeColor = Color.FromArgb(244, 67, 54);
                _btnToggle.Text = "恢复";
                _btnToggle.BackColor = Color.FromArgb(76, 175, 80);
                _trayIcon.Text = "TouchpadTurbo (已暂停)";
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_TOGGLE_ID)
            {
                IsEnabled = !IsEnabled;
                UpdateStatus();
                SaveConfig();
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
                _trayIcon.ShowBalloonTip(1200, "TouchpadTurbo 后台常驻", "程序仍在后台生效。如需完全退出请右键托盘图标点击[退出]。", ToolTipIcon.Info);
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnregisterHotKey(this.Handle, HOTKEY_TOGGLE_ID);
                if (_hookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookID);
                    _hookID = IntPtr.Zero;
                }
                if (_gcHandle.IsAllocated)
                {
                    _gcHandle.Free();
                }
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        // --- Low-Level Hook Callback ---
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                int now = Environment.TickCount;

                // 1. Ghost-Tap Suppression on high-speed swipes
                if (SwipeTapFilter && msg == WM_LBUTTONDOWN)
                {
                    // If the cursor was in high-speed motion within 45ms (dist >= 7.0px):
                    // This is an accidental tap triggered by lifting finger at the end of a swipe stroke.
                    if (unchecked(now - _lastFastMoveTime) < 45 && _lastFastMoveDist >= 7.0)
                    {
                        _dropNextLButtonUp = true;
                        _dropTime = now;
                        return (IntPtr)1; // Drop the ghost click!
                    }
                }
                else if (msg == WM_LBUTTONUP)
                {
                    if (_dropNextLButtonUp && unchecked(now - _dropTime) < 350)
                    {
                        _dropNextLButtonUp = false;
                        return (IntPtr)1; // Drop the matching ghost release!
                    }
                    _dropNextLButtonUp = false;
                }

                // 2. Track Mouse Button States
                if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                {
                    _isButtonDown = true;
                }
                else if (msg == WM_LBUTTONUP || msg == WM_RBUTTONUP || msg == WM_MBUTTONUP)
                {
                    _isButtonDown = false;
                    _buttonUpTime = now;
                }

                // 3. Process WM_MOUSEMOVE
                if (msg == WM_MOUSEMOVE)
                {
                    var s = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));

                    // Ignore synthetic events injected by TouchpadTurbo
                    if ((s.flags & LLMHF_INJECTED) != 0 || ((uint)s.dwExtraInfo.ToUInt64() == MAGIC_ID))
                    {
                        _lastPt = s.pt;
                        return CallNextHookEx(_hookID, nCode, wParam, lParam);
                    }

                    if (!IsEnabled)
                    {
                        _lastPt = s.pt;
                        return CallNextHookEx(_hookID, nCode, wParam, lParam);
                    }

                    // Click & Drag Protection (Fixes Ditto paste, text selection, and window dragging)
                    if (ClickProtect)
                    {
                        bool isPhysicalButtonDown = _isButtonDown
                            || (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
                            || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
                            || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;

                        bool isDebouncing = unchecked(now - _buttonUpTime) < 80;

                        if (isPhysicalButtonDown || isDebouncing)
                        {
                            _lastPt = s.pt;
                            _hasPt = true;
                            return CallNextHookEx(_hookID, nCode, wParam, lParam);
                        }
                    }

                    if (_hasPt)
                    {
                        int dx = s.pt.x - _lastPt.x;
                        int dy = s.pt.y - _lastPt.y;

                        if (dx != 0 || dy != 0)
                        {
                            double dist = Math.Sqrt(dx * dx + dy * dy);

                            // Track high-speed movement for swipe tap filtering
                            if (dist >= 7.0)
                            {
                                _lastFastMoveTime = now;
                                _lastFastMoveDist = dist;
                            }

                            // Teleportation protection (SetCursorPos, SnapToDefaultButton, monitor wrapping)
                            if (dist > 120)
                            {
                                _lastPt = s.pt;
                                return CallNextHookEx(_hookID, nCode, wParam, lParam);
                            }

                            // Micro-jitter deadzone (Bluetooth trackpad sensor noise)
                            if (dist <= 1.2)
                            {
                                _lastPt = s.pt;
                                return CallNextHookEx(_hookID, nCode, wParam, lParam);
                            }

                            double mult = BaseMultiplier;

                            if (PrecisionMode && dist <= 2.2)
                            {
                                mult = 1.0 + (BaseMultiplier - 1.0) * 0.35; // Gentle precision mode
                            }
                            else
                            {
                                if (FlickBoost > 0 && dist > 2.0)
                                {
                                    mult += Math.Min(FlickBoost, (dist - 2.0) * 0.45);
                                }
                            }

                            int extra_x = (int)Math.Round(dx * (mult - 1.0));
                            int extra_y = (int)Math.Round(dy * (mult - 1.0));

                            if (extra_x != 0 || extra_y != 0)
                            {
                                mouse_event(0x0001, extra_x, extra_y, 0, (UIntPtr)MAGIC_ID);

                                // Clamp to virtual screen boundaries to prevent screen-edge coordinate overflow
                                int new_x = Math.Max(SystemInformation.VirtualScreen.Left,
                                            Math.Min(SystemInformation.VirtualScreen.Right - 1, s.pt.x + extra_x));
                                int new_y = Math.Max(SystemInformation.VirtualScreen.Top,
                                            Math.Min(SystemInformation.VirtualScreen.Bottom - 1, s.pt.y + extra_y));

                                _lastPt.x = new_x;
                                _lastPt.y = new_y;
                                return CallNextHookEx(_hookID, nCode, wParam, lParam);
                            }
                        }
                    }

                    _lastPt = s.pt;
                    _hasPt = true;
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // --- Config & Auto-start Persistence ---
        private static void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var lines = File.ReadAllLines(ConfigPath);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            var k = parts[0].Trim();
                            var v = parts[1].Trim();
                            if (k == "BaseMultiplier") double.TryParse(v, out BaseMultiplier);
                            if (k == "FlickBoost") double.TryParse(v, out FlickBoost);
                            if (k == "PrecisionMode") bool.TryParse(v, out PrecisionMode);
                            if (k == "ClickProtect") bool.TryParse(v, out ClickProtect);
                            if (k == "SwipeTapFilter") bool.TryParse(v, out SwipeTapFilter);
                            if (k == "IsEnabled") bool.TryParse(v, out IsEnabled);
                        }
                    }
                }
            }
            catch { }
        }

        private static void SaveConfig()
        {
            try
            {
                var content = string.Format(
                    "BaseMultiplier={0:F2}\r\nFlickBoost={1:F2}\r\nPrecisionMode={2}\r\nClickProtect={3}\r\nSwipeTapFilter={4}\r\nIsEnabled={5}\r\n",
                    BaseMultiplier, FlickBoost, PrecisionMode, ClickProtect, SwipeTapFilter, IsEnabled
                );
                File.WriteAllText(ConfigPath, content);
            }
            catch { }
        }

        private static void SetAutoStart(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            key.SetValue("TouchpadTurbo", "\"" + Application.ExecutablePath + "\"");
                        }
                        else
                        {
                            key.DeleteValue("TouchpadTurbo", false);
                        }
                    }
                }
            }
            catch { }
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        return key.GetValue("TouchpadTurbo") != null;
                    }
                }
            }
            catch { }
            return false;
        }

        private static int GetTouchpadSensitivity()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad", false))
                {
                    if (key != null)
                    {
                        object obj = key.GetValue("AAPThreshold");
                        if (obj is int) return (int)obj;
                    }
                }
            }
            catch { }
            return 2;
        }

        private static void SetTouchpadSensitivity(int val)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad", true))
                {
                    if (key != null)
                    {
                        key.SetValue("AAPThreshold", val, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }
    }

    static class Program
    {
        private static Mutex _singleMutex;

        [STAThread]
        static void Main()
        {
            try
            {
                bool isNewInstance = true;
                try
                {
                    _singleMutex = new Mutex(true, "TouchpadTurbo_SingleInstance_Mutex_Unique", out isNewInstance);
                }
                catch
                {
                    isNewInstance = true;
                }

                if (!isNewInstance)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());

                if (_singleMutex != null)
                {
                    GC.KeepAlive(_singleMutex);
                }
            }
            catch { }
        }
    }
}
