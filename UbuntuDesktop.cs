using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// ============================================================================
//  UbuntuDesktop - one-click WSL2 Ubuntu desktop launcher for Windows
//  https://github.com/morpheusadam/wsl-ubuntu-desktop
//
//  Boots WSL, (optionally installs and) starts the xrdp desktop server,
//  auto-logs in, and opens the Ubuntu XFCE desktop fullscreen. Credentials
//  live in config.ini next to the exe - nothing sensitive is compiled in.
//
//  CLI flags:  /settings  open the settings window
//              /setup     force the first-run setup path
// ============================================================================

static class App
{
    public const string Version = "2.0.0";
    public const string Repo = "https://github.com/morpheusadam/wsl-ubuntu-desktop";
}

static class Brand
{
    public static readonly Color Teal = Color.FromArgb(0, 62, 87);
    public static readonly Color Gold = Color.FromArgb(255, 200, 61);
    public static readonly Color Ink = Color.FromArgb(3, 3, 3);
    public static readonly Color Surface = Color.FromArgb(247, 247, 247);
    public static readonly Color Ok = Color.FromArgb(30, 140, 70);
    public static readonly Color Warn = Color.FromArgb(190, 70, 30);
    public static readonly Color Muted = Color.FromArgb(109, 109, 109);
}

class AppConfig
{
    public string User = "";
    public string Pass = "";
    public int Port = 3390;
    public string Distro = "Ubuntu";
    public bool SavePass = true;
    public bool NewDesktop = false;
    public bool Windowed = false;
    public bool Startup = false;

    public static string PathFor()
    {
        return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "config.ini");
    }

    public bool IsConfigured { get { return User.Length > 0 && Pass.Length > 0; } }

    public static AppConfig Load()
    {
        var c = new AppConfig();
        c.Startup = AutoStart.IsEnabled();
        string p = PathFor();
        if (!File.Exists(p)) return c;
        foreach (string raw in File.ReadAllLines(p))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            int i = line.IndexOf('=');
            if (i <= 0) continue;
            string k = line.Substring(0, i).Trim().ToLowerInvariant();
            string v = line.Substring(i + 1).Trim();
            if (k == "user") c.User = v;
            else if (k == "pass") c.Pass = v;
            else if (k == "port") { int t; if (int.TryParse(v, out t)) c.Port = t; }
            else if (k == "distro") c.Distro = v;
            else if (k == "savepass") c.SavePass = v == "true";
            else if (k == "newdesktop") c.NewDesktop = v == "true";
            else if (k == "windowed") c.Windowed = v == "true";
        }
        return c;
    }

    public void Save()
    {
        var b = new StringBuilder();
        b.AppendLine("; ============================================================================");
        b.AppendLine(";   UbuntuDesktop - configuration file (config.ini)");
        b.AppendLine(";   Lines starting with ';' are comments. Format is  key=value.");
        b.AppendLine(";   Edit here or use the Settings window:  UbuntuDesktop.exe /settings");
        b.AppendLine(";   This file is git-ignored - your password is never uploaded.");
        b.AppendLine("; ============================================================================");
        b.AppendLine();
        b.AppendLine("; Your WSL/Linux username (run 'whoami' in Ubuntu). REQUIRED.");
        b.AppendLine("user=" + User);
        b.AppendLine();
        b.AppendLine("; Your Linux password - xrdp authenticates against the real Linux user, so");
        b.AppendLine("; this must equal your Ubuntu password. Stored only when savepass=true.");
        b.AppendLine("pass=" + (SavePass ? Pass : ""));
        b.AppendLine();
        b.AppendLine("; xrdp port inside WSL (3390 avoids clashing with Windows RDP on 3389).");
        b.AppendLine("port=" + Port);
        b.AppendLine();
        b.AppendLine("; WSL distribution to launch (see 'wsl -l -v').");
        b.AppendLine("distro=" + Distro);
        b.AppendLine();
        b.AppendLine("; true = cache the password for prompt-free auto-login; false = ask each time.");
        b.AppendLine("savepass=" + (SavePass ? "true" : "false"));
        b.AppendLine();
        b.AppendLine("; true = move one virtual desktop right (Win+Ctrl+Right) and open there.");
        b.AppendLine("newdesktop=" + (NewDesktop ? "true" : "false"));
        b.AppendLine();
        b.AppendLine("; true = open in a resizable window instead of fullscreen.");
        b.AppendLine("windowed=" + (Windowed ? "true" : "false"));
        File.WriteAllText(PathFor(), b.ToString());
    }
}

// --------------------------------------------------------------------------
//  WSL helper - all wsl.exe interaction. WSL_UTF8=1 forces UTF-8 output so
//  distro lists and version strings parse cleanly.
// --------------------------------------------------------------------------
static class Wsl
{
    public static string Run(string args, int timeoutMs, out int exit)
    {
        exit = -1;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["WSL_UTF8"] = "1";
            using (var p = Process.Start(psi))
            {
                string outp = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return ""; }
                exit = p.ExitCode;
                return (outp + err).Trim();
            }
        }
        catch { return ""; }
    }

    public static bool Installed()
    {
        int e;
        string s = Run("--status", 8000, out e);
        return e == 0 || s.Length > 0;
    }

    public static List<string> Distros()
    {
        var list = new List<string>();
        int e;
        string s = Run("-l -q", 8000, out e);
        foreach (string line in s.Split('\n'))
        {
            string d = line.Trim();
            if (d.Length > 0 && !d.StartsWith("Windows Subsystem")) list.Add(d);
        }
        return list;
    }

    public static bool Running(string distro)
    {
        int e;
        string s = Run("-l -v", 8000, out e);
        foreach (string line in s.Split('\n'))
            if (line.Contains(distro) && line.IndexOf("Running", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    public static string DefaultUser(string distro)
    {
        int e;
        string s = Run("-d " + distro + " -- whoami", 12000, out e);
        s = s.Trim();
        return (e == 0 && s.Length > 0 && !s.Contains(" ")) ? s : "";
    }

    // xrdp + a session starter both present?
    public static bool DesktopReady(string distro)
    {
        int e;
        string s = Run("-d " + distro + " -- bash -lc \"command -v xrdp >/dev/null && command -v startxfce4 >/dev/null && echo READY\"", 15000, out e);
        return s.Contains("READY");
    }

    public static bool HasSystemd(string distro)
    {
        int e;
        string s = Run("-d " + distro + " -u root -- ps -p 1 -o comm=", 10000, out e);
        return s.Contains("systemd");
    }

    // Best-effort xrdp start that works with or without systemd.
    public static void StartXrdp(string distro)
    {
        string cmd = "pgrep -x xrdp >/dev/null || systemctl start xrdp 2>/dev/null || service xrdp start 2>/dev/null || " +
                     "{ /usr/sbin/xrdp-sesman 2>/dev/null; /usr/sbin/xrdp 2>/dev/null; }";
        int e;
        Run("-d " + distro + " -u root -- bash -lc \"" + cmd + "\"", 25000, out e);
    }

    // Keep the VM alive with a hidden holder if nothing else is running.
    public static void EnsureAlive(string distro)
    {
        if (Process.GetProcessesByName("vmmemWSL").Length > 0 ||
            Process.GetProcessesByName("vmmem").Length > 0) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "-d " + distro + " -u root -e sh -c \"while true; do sleep 3600; done\"",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch { }
    }
}

// --------------------------------------------------------------------------
//  Windows startup entry (per-user, no admin needed).
// --------------------------------------------------------------------------
static class AutoStart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Val = "UbuntuDesktop";

    public static bool IsEnabled()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(Key))
            return k != null && k.GetValue(Val) != null;
    }

    public static void Set(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(Key))
        {
            if (on) k.SetValue(Val, "\"" + Application.ExecutablePath + "\"");
            else if (k.GetValue(Val) != null) k.DeleteValue(Val);
        }
    }
}

// --------------------------------------------------------------------------
//  Streaming install/repair window - runs the WSL-side setup with live output.
// --------------------------------------------------------------------------
class InstallForm : Form
{
    TextBox _log;
    Button _close;
    ProgressBar _bar;
    readonly string _distro, _user;
    readonly int _port;
    public bool Success;

    public InstallForm(string distro, string user, int port)
    {
        _distro = distro; _user = user; _port = port;
        Text = "Installing Ubuntu desktop";
        ClientSize = new Size(620, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.White;
        MinimizeBox = false;

        var head = new Panel { Bounds = new Rectangle(0, 0, 620, 6), BackColor = Brand.Teal };
        Controls.Add(head);

        var title = new Label
        {
            Text = "Setting up XFCE + xrdp inside " + distro + " ...",
            Bounds = new Rectangle(16, 16, 588, 22),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
        Controls.Add(title);

        _bar = new ProgressBar { Bounds = new Rectangle(16, 44, 588, 10), Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
        Controls.Add(_bar);

        _log = new TextBox
        {
            Bounds = new Rectangle(16, 64, 588, 300),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.Gainsboro,
            Font = new Font("Consolas", 9f),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        Controls.Add(_log);

        _close = new Button
        {
            Text = "Please wait...",
            Bounds = new Rectangle(508, 374, 96, 32),
            Enabled = false,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _close.Click += (s, e) => Close();
        Controls.Add(_close);

        Shown += (s, e) => new Thread(RunInstall) { IsBackground = true }.Start();
    }

    void Append(string line)
    {
        if (IsDisposed) return;
        try { BeginInvoke((Action)(() => { _log.AppendText(line + "\r\n"); })); } catch { }
    }

    void RunInstall()
    {
        Wsl.EnsureAlive(_distro);

        // systemd is required for the enable/start step; warn but continue.
        if (!Wsl.HasSystemd(_distro))
        {
            Append(">>> NOTE: systemd is not active in this distro.");
            Append(">>> Enabling it in /etc/wsl.conf; you must run 'wsl --shutdown' once after this.");
            int se;
            Wsl.Run("-d " + _distro + " -u root -- bash -lc \"grep -q '\\[boot\\]' /etc/wsl.conf 2>/dev/null || printf '[boot]\\nsystemd=true\\n' >> /etc/wsl.conf\"", 10000, out se);
        }

        string home = "/home/" + _user;
        string script = string.Join("\n", new string[] {
            "set -e",
            "export DEBIAN_FRONTEND=noninteractive",
            "echo '>>> Updating package lists...'",
            "apt-get update -y",
            "echo '>>> Installing XFCE + xrdp (a few minutes)...'",
            "apt-get install -y xfce4 xfce4-goodies xrdp dbus-x11",
            "echo '>>> Configuring xrdp on port " + _port + "...'",
            "sed -i 's/^port=3389/port=" + _port + "/' /etc/xrdp/xrdp.ini || true",
            "grep -q '^port=" + _port + "' /etc/xrdp/xrdp.ini || sed -i '/^\\[Globals\\]/a port=" + _port + "' /etc/xrdp/xrdp.ini",
            "usermod -aG ssl-cert xrdp 2>/dev/null || true",
            "HOME_DIR=$(getent passwd " + _user + " | cut -d: -f6); [ -z \"$HOME_DIR\" ] && HOME_DIR=" + home,
            "echo startxfce4 > \"$HOME_DIR/.xsession\"",
            "chown " + _user + ":" + _user + " \"$HOME_DIR/.xsession\" 2>/dev/null || true",
            "echo '>>> Enabling and starting xrdp...'",
            "systemctl enable xrdp >/dev/null 2>&1 || true",
            "systemctl restart xrdp 2>/dev/null || service xrdp restart 2>/dev/null || true",
            "sleep 1",
            "(systemctl is-active xrdp 2>/dev/null || echo unknown) | sed 's/^/xrdp status: /'",
            "echo SETUP-DONE"
        });

        bool done = false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "-d " + _distro + " -u root -e bash",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["WSL_UTF8"] = "1";
            psi.EnvironmentVariables["DEBIAN_FRONTEND"] = "noninteractive";
            using (var p = Process.Start(psi))
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) { Append(e.Data); if (e.Data.Contains("SETUP-DONE")) done = true; } };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) Append(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.StandardInput.NewLine = "\n";
                p.StandardInput.Write(script + "\n");
                p.StandardInput.Close();
                p.WaitForExit(600000);
            }
        }
        catch (Exception ex) { Append("ERROR: " + ex.Message); }

        Success = done;
        try
        {
            BeginInvoke((Action)(() =>
            {
                _bar.Style = ProgressBarStyle.Continuous;
                _bar.Value = _bar.Maximum;
                _close.Enabled = true;
                _close.Text = Success ? "Done" : "Close";
                Append(Success ? "\r\n>>> Desktop is ready. You can close this window." :
                                 "\r\n>>> Setup did not complete cleanly - review the log above.");
            }));
        }
        catch { }
    }
}

// --------------------------------------------------------------------------
//  Settings + first-run setup, all in one branded window.
// --------------------------------------------------------------------------
class SettingsForm : Form
{
    ComboBox _distro;
    TextBox _user, _pass, _port;
    CheckBox _cbApply, _cbSave, _cbDesktop, _cbWindowed, _cbStartup;
    Label _status;
    Button _install;
    public AppConfig Result;
    public bool ApplyLinuxPassword;

    public SettingsForm(AppConfig cfg)
    {
        Text = "Ubuntu Desktop - Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(430, 470);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        BackColor = Brand.Surface;

        Controls.Add(new Panel { Bounds = new Rectangle(0, 0, 430, 8), BackColor = Brand.Teal });

        var h = new Label
        {
            Text = "Ubuntu Desktop",
            Bounds = new Rectangle(18, 16, 300, 26),
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Brand.Teal
        };
        Controls.Add(h);
        var ver = new LinkLabel
        {
            Text = "v" + App.Version + "  -  Help",
            Bounds = new Rectangle(300, 22, 112, 18),
            TextAlign = ContentAlignment.MiddleRight,
            LinkColor = Brand.Muted
        };
        ver.Click += (s, e) => { try { Process.Start(App.Repo); } catch { } };
        Controls.Add(ver);

        int y = 52;
        _distro = new ComboBox { Bounds = new Rectangle(150, y, 262, 24), DropDownStyle = ComboBoxStyle.DropDown };
        AddLabeled("WSL distribution", _distro, ref y);
        _distro.SelectedIndexChanged += (s, e) => DetectAsync();
        _distro.LostFocus += (s, e) => DetectAsync();

        _user = new TextBox { Bounds = new Rectangle(150, y, 262, 24), Text = cfg.User };
        AddLabeled("Username", _user, ref y);

        _pass = new TextBox { Bounds = new Rectangle(150, y, 262, 24), Text = cfg.Pass, UseSystemPasswordChar = true };
        AddLabeled("Password", _pass, ref y);

        _port = new TextBox { Bounds = new Rectangle(150, y, 262, 24), Text = cfg.Port.ToString() };
        AddLabeled("xrdp port", _port, ref y);

        // status line
        _status = new Label
        {
            Bounds = new Rectangle(18, y, 394, 20),
            ForeColor = Brand.Muted,
            Text = "Checking WSL..."
        };
        Controls.Add(_status);
        y += 26;

        _install = new Button
        {
            Text = "Install / Repair Ubuntu desktop",
            Bounds = new Rectangle(18, y, 394, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White
        };
        _install.Click += OnInstall;
        Controls.Add(_install);
        y += 42;

        _cbApply = AddCheck("Also set this as the WSL/Linux password", false, ref y);
        _cbSave = AddCheck("Remember password (prompt-free auto-login)", cfg.SavePass, ref y);
        _cbWindowed = AddCheck("Open in a window instead of fullscreen", cfg.Windowed, ref y);
        _cbDesktop = AddCheck("Open on the side virtual desktop (move one right)", cfg.NewDesktop, ref y);
        _cbStartup = AddCheck("Launch at Windows startup", cfg.Startup, ref y);

        var ok = new Button
        {
            Text = "Save && Connect",
            Bounds = new Rectangle(264, y + 8, 148, 34),
            BackColor = Brand.Gold,
            ForeColor = Brand.Ink,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.Click += OnOk;
        Controls.Add(ok);

        var save = new Button { Text = "Save only", Bounds = new Rectangle(160, y + 8, 96, 34), FlatStyle = FlatStyle.Flat };
        save.Click += OnSaveOnly;
        Controls.Add(save);

        AcceptButton = ok;

        Shown += (s, e) => PopulateDistros(cfg.Distro);
    }

    void AddLabeled(string label, Control c, ref int y)
    {
        Controls.Add(new Label { Text = label, Bounds = new Rectangle(18, y + 4, 128, 20) });
        Controls.Add(c);
        y += 34;
    }

    CheckBox AddCheck(string label, bool val, ref int y)
    {
        var c = new CheckBox { Text = label, Bounds = new Rectangle(20, y, 398, 22), Checked = val };
        Controls.Add(c);
        y += 26;
        return c;
    }

    void PopulateDistros(string prefer)
    {
        new Thread(() =>
        {
            var distros = Wsl.Distros();
            try
            {
                BeginInvoke((Action)(() =>
                {
                    _distro.Items.Clear();
                    foreach (var d in distros) _distro.Items.Add(d);
                    if (prefer.Length > 0 && distros.Contains(prefer)) _distro.Text = prefer;
                    else if (distros.Count > 0) _distro.SelectedIndex = 0;
                    else _distro.Text = prefer.Length > 0 ? prefer : "Ubuntu";
                    DetectAsync();
                }));
            }
            catch { }
        }) { IsBackground = true }.Start();
    }

    void DetectAsync()
    {
        string distro = _distro.Text.Trim();
        if (distro.Length == 0) return;
        SetStatus("Checking " + distro + "...", Brand.Muted);
        new Thread(() =>
        {
            if (!Wsl.Installed()) { SetStatus("WSL is not installed. See Help.", Brand.Warn); return; }
            bool ready = Wsl.DesktopReady(distro);
            string user = _user.Text.Trim().Length > 0 ? _user.Text.Trim() : Wsl.DefaultUser(distro);
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (user.Length > 0 && _user.Text.Trim().Length == 0) _user.Text = user;
                    if (ready)
                    {
                        SetStatus("Desktop is installed and ready.", Brand.Ok);
                        _install.Text = "Reinstall / Repair Ubuntu desktop";
                    }
                    else
                    {
                        SetStatus("Desktop not installed yet - click Install below.", Brand.Warn);
                        _install.Text = "Install Ubuntu desktop (one-time)";
                    }
                }));
            }
            catch { }
        }) { IsBackground = true }.Start();
    }

    void SetStatus(string text, Color color)
    {
        if (IsDisposed) return;
        try { BeginInvoke((Action)(() => { _status.Text = text; _status.ForeColor = color; })); } catch { }
    }

    void OnInstall(object s, EventArgs e)
    {
        string distro = _distro.Text.Trim();
        string user = _user.Text.Trim();
        int port;
        if (distro.Length == 0) { MessageBox.Show("Pick a WSL distribution first."); return; }
        if (!int.TryParse(_port.Text.Trim(), out port)) port = 3390;
        if (user.Length == 0) user = Wsl.DefaultUser(distro);
        if (user.Length == 0) { MessageBox.Show("Enter your WSL username first."); return; }

        using (var f = new InstallForm(distro, user, port)) f.ShowDialog(this);
        DetectAsync();
    }

    bool Collect()
    {
        int port;
        if (_user.Text.Trim().Length == 0 || _pass.Text.Length == 0 || !int.TryParse(_port.Text.Trim(), out port))
        {
            MessageBox.Show("Please fill username, password and a numeric port.", "Ubuntu Desktop",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        Result = new AppConfig
        {
            User = _user.Text.Trim(),
            Pass = _pass.Text,
            Port = port,
            Distro = _distro.Text.Trim().Length > 0 ? _distro.Text.Trim() : "Ubuntu",
            SavePass = _cbSave.Checked,
            NewDesktop = _cbDesktop.Checked,
            Windowed = _cbWindowed.Checked
        };
        Result.Save();
        ApplyLinuxPassword = _cbApply.Checked;
        AutoStart.Set(_cbStartup.Checked);
        return true;
    }

    void OnOk(object s, EventArgs e) { if (Collect()) { DialogResult = DialogResult.OK; Close(); } }
    void OnSaveOnly(object s, EventArgs e) { if (Collect()) { DialogResult = DialogResult.Cancel; Close(); } }
}

// --------------------------------------------------------------------------
//  Main launcher.
// --------------------------------------------------------------------------
static class Launcher
{
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    const byte VK_LWIN = 0x5B, VK_LCONTROL = 0xA2, VK_RIGHT = 0x27;
    const uint KEYUP = 0x2;

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Run(args); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ubuntu Desktop", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    static void Run(string[] args)
    {
        bool wantSettings = Array.IndexOf(args, "/settings") >= 0 || Array.IndexOf(args, "/setup") >= 0;

        if (!Wsl.Installed())
        {
            var r = MessageBox.Show(
                "WSL2 is not installed on this PC.\r\n\r\n" +
                "Install it (admin PowerShell):  wsl --install -d Ubuntu\r\n\r\n" +
                "Open the setup guide now?",
                "Ubuntu Desktop", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r == DialogResult.Yes) { try { Process.Start(App.Repo); } catch { } }
            return;
        }

        var cfg = AppConfig.Load();
        bool applyLinux = false;

        if (wantSettings || !cfg.IsConfigured || !Wsl.DesktopReady(cfg.Distro))
        {
            var f = new SettingsForm(cfg);
            if (f.ShowDialog() != DialogResult.OK) return;
            cfg = f.Result;
            applyLinux = f.ApplyLinuxPassword;
        }

        Connect(cfg, applyLinux);
    }

    static void Connect(AppConfig cfg, bool applyLinux)
    {
        Wsl.EnsureAlive(cfg.Distro);

        if (applyLinux)
        {
            string esc = cfg.Pass.Replace("\\", "\\\\").Replace("'", "'\\''");
            int e;
            Wsl.Run("-d " + cfg.Distro + " -u root -- bash -lc \"echo '" + cfg.User + ":" + esc + "' | chpasswd\"", 15000, out e);
        }

        Wsl.StartXrdp(cfg.Distro);

        if (!WaitPort("localhost", cfg.Port, 45))
        {
            MessageBox.Show(
                "Could not reach xrdp on port " + cfg.Port + " within 45 seconds.\r\n\r\n" +
                "Open Settings and click \"Install / Repair Ubuntu desktop\", or check that xrdp is enabled in " + cfg.Distro + ".",
                "Ubuntu Desktop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SuppressDeviceWarning();
        StoreCredential(cfg);

        Thread.Sleep(400);

        var b = Screen.PrimaryScreen.Bounds;
        var lines = new List<string>
        {
            "full address:s:localhost:" + cfg.Port,
            "username:s:" + cfg.User,
            "prompt for credentials:i:0",
            "authentication level:i:0",
            "enablecredsspsupport:i:0",
            "use multimon:i:0",
            "session bpp:i:32",
            "compression:i:1",
            "audiomode:i:0",
            "redirectclipboard:i:1",
            "redirectprinters:i:0",
            "redirectcomports:i:0",
            "redirectsmartcards:i:0",
            "redirectdrives:i:0",
            "devicestoredirect:s:",
            "autoreconnection enabled:i:1",
            "smart sizing:i:1"
        };
        if (cfg.Windowed)
        {
            lines.Add("screen mode id:i:1");
            lines.Add("desktopwidth:i:" + (int)(b.Width * 0.8));
            lines.Add("desktopheight:i:" + (int)(b.Height * 0.8));
        }
        else
        {
            lines.Add("screen mode id:i:2");
            lines.Add("desktopwidth:i:" + b.Width);
            lines.Add("desktopheight:i:" + b.Height);
        }
        lines.Add("");
        string rdp = Path.Combine(Path.GetTempPath(), "wsl-ubuntu.rdp");
        File.WriteAllText(rdp, string.Join("\r\n", lines.ToArray()));

        SignRdp(rdp);

        if (cfg.NewDesktop) MoveDesktopRight();

        Process.Start("mstsc.exe", "\"" + rdp + "\"");

        if (!cfg.SavePass)
        {
            Thread.Sleep(8000);
            StartHidden("cmdkey.exe", "/delete:TERMSRV/localhost", true);
        }
    }

    static void StoreCredential(AppConfig cfg)
    {
        StartHidden("cmdkey.exe", "/generic:TERMSRV/localhost /user:" + cfg.User + " /pass:" + cfg.Pass, true);
    }

    static void MoveDesktopRight()
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_LCONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_RIGHT, 0, 0, UIntPtr.Zero);
        keybd_event(VK_RIGHT, 0, KEYUP, UIntPtr.Zero);
        keybd_event(VK_LCONTROL, 0, KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYUP, UIntPtr.Zero);
        Thread.Sleep(700);
    }

    static void SuppressDeviceWarning()
    {
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Terminal Server Client\LocalDevices"))
                k.SetValue("localhost", 0x4C, RegistryValueKind.DWord);
        }
        catch { }
    }

    // Sign the .rdp with the local "UbuntuDesktop Launcher" cert if the user
    // installed it via trust-cert.ps1; otherwise a harmless no-op.
    static void SignRdp(string rdpPath)
    {
        try
        {
            string thumb = null;
            var store = new System.Security.Cryptography.X509Certificates.X509Store(
                System.Security.Cryptography.X509Certificates.StoreName.My,
                System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
            store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
            foreach (var c in store.Certificates)
                if (c.Subject.IndexOf("UbuntuDesktop Launcher", StringComparison.OrdinalIgnoreCase) >= 0) { thumb = c.Thumbprint; break; }
            store.Close();
            if (thumb == null) return;
            string rdpsign = Path.Combine(Environment.SystemDirectory, "rdpsign.exe");
            if (File.Exists(rdpsign)) StartHidden(rdpsign, "/sha256 " + thumb + " \"" + rdpPath + "\"", true);
        }
        catch { }
    }

    static void StartHidden(string exe, string arg, bool wait)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arg,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            });
            if (wait && p != null) p.WaitForExit(30000);
        }
        catch { }
    }

    static bool WaitPort(string host, int port, int seconds)
    {
        for (int i = 0; i < seconds; i++)
        {
            System.Net.IPAddress[] addrs;
            try { addrs = System.Net.Dns.GetHostAddresses(host); }
            catch { addrs = new System.Net.IPAddress[0]; }
            foreach (var a in addrs)
            {
                try
                {
                    using (var c = new TcpClient(a.AddressFamily))
                    {
                        var r = c.BeginConnect(a, port, null, null);
                        if (r.AsyncWaitHandle.WaitOne(800)) { c.EndConnect(r); return true; }
                    }
                }
                catch { }
            }
            Thread.Sleep(700);
        }
        return false;
    }
}
