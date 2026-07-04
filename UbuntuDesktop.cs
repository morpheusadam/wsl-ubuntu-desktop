using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

// One-click launcher for a WSL2 Ubuntu desktop over xrdp.
// Reads credentials from config.ini next to the exe - nothing sensitive is compiled in.
class UbuntuDesktop
{
    static void Main()
    {
        try
        {
            string dir = Path.GetDirectoryName(Application.ExecutablePath);
            string cfgPath = Path.Combine(dir, "config.ini");

            if (!File.Exists(cfgPath))
            {
                File.WriteAllText(cfgPath, string.Join("\r\n", new string[] {
                    "; UbuntuDesktop launcher configuration",
                    "user=your-wsl-username",
                    "pass=your-wsl-password",
                    "port=3390",
                    "distro=Ubuntu",
                    ""
                }));
                MessageBox.Show(
                    "config.ini was created next to the exe.\r\n\r\n" +
                    "Open it, set your WSL username and password, then run again.",
                    "Ubuntu Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Process.Start("notepad.exe", "\"" + cfgPath + "\"");
                return;
            }

            var cfg = ReadConfig(cfgPath);
            string user = Get(cfg, "user", "");
            string pass = Get(cfg, "pass", "");
            int port = int.Parse(Get(cfg, "port", "3390"));
            string distro = Get(cfg, "distro", "Ubuntu");

            if (user == "" || user == "your-wsl-username" || pass == "" || pass == "your-wsl-password")
            {
                MessageBox.Show("Please set user and pass in config.ini first.",
                    "Ubuntu Desktop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Process.Start("notepad.exe", "\"" + cfgPath + "\"");
                return;
            }

            // WSL running? if not, start a hidden holder so it boots and stays up
            if (Process.GetProcessesByName("vmmemWSL").Length == 0 &&
                Process.GetProcessesByName("vmmem").Length == 0)
            {
                StartHidden("wsl.exe", "-d " + distro + " -u root -e sh -c \"while true; do sleep 3600; done\"", false);
            }

            // make sure xrdp is up (this also boots the distro if needed)
            StartHidden("wsl.exe", "-d " + distro + " -u root -- systemctl start xrdp", true);

            // wait for the RDP port to answer (cold boot can take a while)
            if (!WaitPort("127.0.0.1", port, 40))
            {
                MessageBox.Show("WSL/xrdp did not come up on port " + port + " within 40 seconds.",
                    "Ubuntu Desktop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // store credentials so mstsc logs in without asking
            StartHidden("cmdkey.exe", "/generic:TERMSRV/localhost /user:" + user + " /pass:" + pass, true);

            // connection profile
            string rdp = Path.Combine(Path.GetTempPath(), "wsl-ubuntu.rdp");
            File.WriteAllText(rdp, string.Join("\r\n", new string[] {
                "full address:s:localhost:" + port,
                "username:s:" + user,
                "prompt for credentials:i:0",
                "authentication level:i:0",
                "enablecredsspsupport:i:0",
                "screen mode id:i:2",
                "use multimon:i:0",
                "session bpp:i:32",
                "smart sizing:i:1",
                "audiomode:i:0",
                "redirectclipboard:i:1",
                "autoreconnection enabled:i:1",
                ""
            }));

            Process.Start("mstsc.exe", "\"" + rdp + "\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ubuntu Desktop", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static Dictionary<string, string> ReadConfig(string path)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line == "" || line.StartsWith(";") || line.StartsWith("#")) continue;
            int i = line.IndexOf('=');
            if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
        }
        return d;
    }

    static string Get(Dictionary<string, string> d, string key, string fallback)
    {
        string v;
        return d.TryGetValue(key, out v) ? v : fallback;
    }

    static void StartHidden(string exe, string args, bool wait)
    {
        var p = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
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
            try
            {
                using (var c = new TcpClient())
                {
                    var r = c.BeginConnect(host, port, null, null);
                    if (r.AsyncWaitHandle.WaitOne(1000) && c.Connected) return true;
                }
            }
            catch { }
            Thread.Sleep(1000);
        }
        return false;
    }
}
