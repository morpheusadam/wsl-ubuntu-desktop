using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// One-click launcher for a WSL2 Ubuntu desktop over xrdp.
// - Auto-logs in (no xrdp login screen)
// - Opens fullscreen at the monitor's native resolution
// - Smart-sizing on, so resizing the window scales the desktop like an image (no scrollbars)
// Credentials live in config.ini next to the exe - nothing sensitive is compiled in.
// Run with /settings to reopen the settings window at any time.

class AppConfig
{
    public string User = "";
    public string Pass = "";
    public int Port = 3390;
    public string Distro = "Ubuntu";
    public bool SavePass = true;
    public bool NewDesktop = false;

    public static string PathFor()
    {
        return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "config.ini");
    }

    public static AppConfig Load()
    {
        var c = new AppConfig();
        string p = PathFor();
        if (!File.Exists(p)) return c;
        foreach (string raw in File.ReadAllLines(p))
        {
            string line = raw.Trim();
            if (line == "" || line.StartsWith(";") || line.StartsWith("#")) continue;
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
        }
        return c;
    }

    public void Save()
    {
        var lines = new List<string>();
        lines.Add("; UbuntuDesktop launcher configuration");
        lines.Add("; pass must match your WSL/Linux password - that is what xrdp authenticates against.");
        lines.Add("user=" + User);
        lines.Add("pass=" + (SavePass ? Pass : ""));
        lines.Add("port=" + Port);
        lines.Add("distro=" + Distro);
        lines.Add("savepass=" + (SavePass ? "true" : "false"));
        lines.Add("newdesktop=" + (NewDesktop ? "true" : "false"));
        File.WriteAllLines(PathFor(), lines.ToArray());
    }
}

class SettingsForm : Form
{
    TextBox tbUser, tbPass, tbPort, tbDistro;
    CheckBox cbSave, cbDesktop, cbStartup, cbApplyLinux;
    public AppConfig Result;
    public bool ApplyLinuxPassword;

    public SettingsForm(AppConfig cfg)
    {
        Text = "Ubuntu Desktop - Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(380, 372);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.FromArgb(247, 247, 247);

        var header = new Panel();
        header.Bounds = new Rectangle(0, 0, 380, 8);
        header.BackColor = Color.FromArgb(0, 62, 87);   // brand deep teal
        Controls.Add(header);

        int y = 24;
        tbUser = AddRow("WSL username", cfg.User, ref y, false);
        tbPass = AddRow("WSL password", cfg.Pass, ref y, true);
        tbPort = AddRow("xrdp port", cfg.Port.ToString(), ref y, false);
        tbDistro = AddRow("Distro name", cfg.Distro, ref y, false);

        cbApplyLinux = AddCheck("Also set this as the WSL/Linux password", false, ref y);
        cbSave = AddCheck("Remember password (stored in config.ini)", cfg.SavePass, ref y);
        cbDesktop = AddCheck("Open on a new virtual desktop", cfg.NewDesktop, ref y);
        cbStartup = AddCheck("Launch at Windows startup", Launcher.IsStartupEnabled(), ref y);

        var ok = new Button();
        ok.Text = "Save && Connect";
        ok.Bounds = new Rectangle(216, y + 10, 148, 34);
        ok.BackColor = Color.FromArgb(255, 200, 61);    // brand gold
        ok.ForeColor = Color.FromArgb(3, 3, 3);
        ok.FlatStyle = FlatStyle.Flat;
        ok.FlatAppearance.BorderSize = 0;
        ok.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        ok.Click += OnOk;
        Controls.Add(ok);

        var save = new Button();
        save.Text = "Save only";
        save.Bounds = new Rectangle(112, y + 10, 96, 34);
        save.FlatStyle = FlatStyle.Flat;
        save.Click += OnSaveOnly;
        Controls.Add(save);

        AcceptButton = ok;
    }

    TextBox AddRow(string label, string val, ref int y, bool password)
    {
        var l = new Label();
        l.Text = label;
        l.Bounds = new Rectangle(20, y + 4, 120, 20);
        Controls.Add(l);
        var t = new TextBox();
        t.Bounds = new Rectangle(150, y, 208, 24);
        t.Text = val;
        if (password) t.UseSystemPasswordChar = true;
        Controls.Add(t);
        y += 36;
        return t;
    }

    CheckBox AddCheck(string label, bool val, ref int y)
    {
        var c = new CheckBox();
        c.Text = label;
        c.Bounds = new Rectangle(22, y, 348, 24);
        c.Checked = val;
        Controls.Add(c);
        y += 27;
        return c;
    }

    bool Collect()
    {
        int port;
        if (tbUser.Text.Trim() == "" || tbPass.Text == "" || !int.TryParse(tbPort.Text.Trim(), out port))
        {
            MessageBox.Show("Please fill username, password and a numeric port.", "Ubuntu Desktop",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        Result = new AppConfig();
        Result.User = tbUser.Text.Trim();
        Result.Pass = tbPass.Text;
        Result.Port = port;
        Result.Distro = tbDistro.Text.Trim() == "" ? "Ubuntu" : tbDistro.Text.Trim();
        Result.SavePass = cbSave.Checked;
        Result.NewDesktop = cbDesktop.Checked;
        Result.Save();
        ApplyLinuxPassword = cbApplyLinux.Checked;
        Launcher.SetStartup(cbStartup.Checked);
        return true;
    }

    void OnOk(object s, EventArgs e)
    {
        if (!Collect()) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    void OnSaveOnly(object s, EventArgs e)
    {
        if (!Collect()) return;
        DialogResult = DialogResult.Cancel;
        Close();
    }
}

static class Launcher
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "UbuntuDesktop";

    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    const byte VK_LWIN = 0x5B, VK_LCONTROL = 0xA2, VK_D = 0x44;
    const uint KEYEVENTF_KEYUP = 0x2;

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        try { Run(args); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ubuntu Desktop", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void Run(string[] args)
    {
        bool forceSettings = Array.IndexOf(args, "/settings") >= 0;
        var cfg = AppConfig.Load();
        bool applyLinux = false;

        if (forceSettings || cfg.User == "" || cfg.Pass == "")
        {
            var f = new SettingsForm(cfg);
            if (f.ShowDialog() != DialogResult.OK) return;
            cfg = f.Result;
            applyLinux = f.ApplyLinuxPassword;
        }

        Connect(cfg, applyLinux);
    }

    public static bool IsStartupEnabled()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            return k != null && k.GetValue(RunValue) != null;
    }

    public static void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
            else if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue);
        }
    }

    // Tell mstsc the user already accepted device redirection for localhost,
    // so the "unknown remote connection" warning never appears.
    static void SuppressWarning()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Terminal Server Client\LocalDevices"))
            k.SetValue("localhost", 0x4C, RegistryValueKind.DWord);
    }

    static void Connect(AppConfig cfg, bool applyLinux)
    {
        // WSL running? if not, start a hidden holder so it boots and stays up
        if (Process.GetProcessesByName("vmmemWSL").Length == 0 &&
            Process.GetProcessesByName("vmmem").Length == 0)
        {
            StartHidden("wsl.exe", "-d " + cfg.Distro + " -u root -e sh -c \"while true; do sleep 3600; done\"", false);
        }

        // optionally set the Linux password to match what the user typed, so WSL and
        // remote credentials always stay in sync
        if (applyLinux)
        {
            string esc = cfg.Pass.Replace("\\", "\\\\").Replace("'", "'\\''");
            StartHidden("wsl.exe", "-d " + cfg.Distro + " -u root -- bash -c \"echo '" +
                cfg.User + ":" + esc + "' | chpasswd\"", true);
        }

        // make sure xrdp is up (this also boots the distro if needed)
        StartHidden("wsl.exe", "-d " + cfg.Distro + " -u root -- systemctl start xrdp", true);

        // wait for the RDP port to answer (cold boot can take a while).
        // WSL's localhost proxy often binds IPv6 (::1) only, so resolve localhost
        // to every address family and accept whichever answers.
        if (!WaitPort("localhost", cfg.Port, 40))
        {
            MessageBox.Show("WSL/xrdp did not come up on port " + cfg.Port + " within 40 seconds.",
                "Ubuntu Desktop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SuppressWarning();

        // store credentials so xrdp auto-logs in without showing its login dialog
        StartHidden("cmdkey.exe", "/generic:TERMSRV/localhost /user:" + cfg.User + " /pass:" + cfg.Pass, true);

        // brief settle so the WSL localhost proxy finishes binding after xrdp start
        Thread.Sleep(500);

        // fit the session to the primary monitor so fullscreen is pixel-perfect,
        // and smart-sizing scales it like an image (no scrollbars) when windowed
        var b = Screen.PrimaryScreen.Bounds;

        string rdp = Path.Combine(Path.GetTempPath(), "wsl-ubuntu.rdp");
        File.WriteAllText(rdp, string.Join("\r\n", new string[] {
            "full address:s:localhost:" + cfg.Port,
            "username:s:" + cfg.User,
            "prompt for credentials:i:0",
            "authentication level:i:0",
            "enablecredsspsupport:i:0",
            "screen mode id:i:2",
            "use multimon:i:0",
            "desktopwidth:i:" + b.Width,
            "desktopheight:i:" + b.Height,
            "smart sizing:i:1",
            "dynamic resolution:i:0",
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
            ""
        }));

        // if the user installed the signing cert (via trust-cert.ps1), sign the .rdp so
        // mstsc shows a verified publisher and never prompts. optional - safe to skip.
        SignRdp(rdp);

        // optionally jump to a fresh virtual desktop (Win+Ctrl+D) so Ubuntu gets its own space
        if (cfg.NewDesktop)
        {
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_LCONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_D, 0, 0, UIntPtr.Zero);
            keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LCONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(700);
        }

        Process.Start("mstsc.exe", "\"" + rdp + "\"");

        // if the user chose not to keep the password, remove the stored credential
        // after mstsc has had time to read it
        if (!cfg.SavePass)
        {
            Thread.Sleep(8000);
            StartHidden("cmdkey.exe", "/delete:TERMSRV/localhost", true);
        }
    }

    // Sign the .rdp with the local "UbuntuDesktop Launcher" code-signing cert if the
    // user installed it. Without the cert this is a no-op and mstsc shows its one-time
    // "Connect" prompt instead. Never installs or trusts anything itself.
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
            {
                if (c.Subject.IndexOf("UbuntuDesktop Launcher", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    thumb = c.Thumbprint;
                    break;
                }
            }
            store.Close();
            if (thumb == null) return;

            string rdpsign = Path.Combine(Environment.SystemDirectory, "rdpsign.exe");
            if (!File.Exists(rdpsign)) return;
            StartHidden(rdpsign, "/sha256 " + thumb + " \"" + rdpPath + "\"", true);
        }
        catch { }
    }

    static void StartHidden(string exe, string arg, bool wait)
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
                        if (r.AsyncWaitHandle.WaitOne(800))
                        {
                            c.EndConnect(r);   // throws if the connection actually failed
                            return true;
                        }
                    }
                }
                catch { }
            }
            Thread.Sleep(700);
        }
        return false;
    }
}
