# UbuntuDesktop — one-click WSL2 Ubuntu desktop launcher

A tiny portable Windows exe (~18 KB, no dependencies) that opens a **full Ubuntu desktop** running inside WSL2 with a single double-click. It boots WSL if it is down, waits for xrdp to come up, logs in automatically, and opens the desktop fullscreen.

<p align="center"><code>double-click → Ubuntu desktop, fullscreen, already logged in</code></p>

## Features

- 🚀 **One click**: no terminal, no login screen, no prompts
- 🔄 **Self-healing**: starts WSL and the xrdp service if they are not running, and keeps WSL alive in the background
- ⏱️ Waits up to 40 s for a cold boot before connecting
- 🔐 **No secrets in the binary**: credentials live in a `config.ini` next to the exe (git-ignored)
- 📦 Portable single exe, built with the C# compiler that ships with Windows — no SDK, no installer

## Requirements

- Windows 10/11 with WSL2 and an Ubuntu distro
- A desktop environment + xrdp inside Ubuntu:

```bash
sudo apt install xfce4 xfce4-goodies xrdp -y
sudo sed -i 's/port=3389/port=3390/' /etc/xrdp/xrdp.ini
sudo usermod -aG ssl-cert xrdp
echo startxfce4 > ~/.xsession
sudo systemctl enable --now xrdp
```

> Port is moved to **3390** so it never clashes with Windows' own Remote Desktop on 3389.

## Usage

1. Put `UbuntuDesktop.exe` in any folder
2. Run it once — it creates a `config.ini` template and opens it in Notepad
3. Fill in your WSL username and password:

```ini
user=your-wsl-username
pass=your-wsl-password
port=3390
distro=Ubuntu
```

4. Run it again. That's it.

## Build from source

No SDK needed — the .NET Framework compiler ships with every Windows installation:

```cmd
build.cmd
```

which simply runs:

```cmd
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe ^
  /r:System.Windows.Forms.dll /win32icon:icon.ico ^
  /out:UbuntuDesktop.exe UbuntuDesktop.cs
```

`make-icon.ps1` regenerates `icon.ico` (pure PowerShell + System.Drawing, multi-size PNG-in-ICO).

## How it works

1. Checks for the `vmmemWSL` process; if WSL is down it spawns a hidden `wsl.exe` holder process so the VM boots and stays up
2. Runs `systemctl start xrdp` inside the distro (no-op if already running)
3. Polls `127.0.0.1:<port>` until the RDP listener answers
4. Stores the credential with `cmdkey /generic:TERMSRV/localhost` so mstsc will not prompt
5. Writes a temporary `.rdp` profile (fullscreen, clipboard redirection, auto-reconnect) and launches `mstsc`

## Security notes

- The password is stored in plain text in `config.ini` and in Windows Credential Manager (`TERMSRV/localhost`). This is meant for a **local, single-user machine** — xrdp inside WSL2 NAT is not reachable from the network.
- `config.ini` is in `.gitignore`; only `config.example.ini` is committed.

---

## راهنمای فارسی

یک فایل exe کوچک و پرتابل که با **یک دابل‌کلیک** دسکتاپ کامل اوبونتوی داخل WSL2 را تمام‌صفحه باز می‌کند — اگر WSL خاموش باشد روشنش می‌کند، منتظر xrdp می‌ماند و خودش لاگین می‌کند.

**استفاده:** فایل exe را هر جا خواستید بگذارید، یک بار اجرا کنید تا `config.ini` ساخته شود، نام کاربری و پسورد WSL را داخلش بنویسید و دوباره اجرا کنید. پسورد داخل exe نیست و فایل کانفیگ هم در گیت آپلود نمی‌شود.

**پیش‌نیاز:** داخل اوبونتو باید xfce4 و xrdp نصب باشد (دستورات بالا در بخش Requirements).
