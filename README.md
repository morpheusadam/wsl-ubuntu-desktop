# UbuntuDesktop — One-Click WSL2 Ubuntu Desktop Launcher for Windows

**Open a full Ubuntu Linux desktop (GUI) from Windows with a single double-click.** UbuntuDesktop is a tiny, portable Windows `.exe` that **installs and configures** the Ubuntu desktop inside WSL2 for you, then boots WSL, starts the xrdp server, **auto-logs in**, and opens your Ubuntu XFCE desktop **fullscreen** — no terminal, no login screen, no per-launch setup.

> Keywords: WSL2 GUI, Ubuntu desktop on Windows, WSL xrdp, WSLg alternative, run Linux GUI on Windows 11, XFCE remote desktop, one-click WSL desktop, WSL2 RDP auto-login, install Linux desktop on Windows without terminal.

![Ubuntu XFCE desktop opened from Windows](docs/desktop.png)

---

## Why this exists

WSL2 runs Linux GUI apps one window at a time (WSLg), but there is no simple, built-in way to open a **full Linux desktop** — panels, dock, file manager, the works — as one clean fullscreen window. The usual xrdp route means editing configs in the terminal, typing a username and password into an ugly login box every time, and fighting black screens and wrong resolutions.

UbuntuDesktop turns all of that into **one double-click** — and it even does the Linux-side installation for you.

## Features

- 🧰 **Installs the desktop for you** — one button installs XFCE + xrdp inside WSL, sets the port, fixes the `ssl-cert` permission, writes the session, and enables the service. **No terminal commands.**
- 🖱️ **True one-click launch** — after setup, double-click the exe and the desktop opens fullscreen
- 🔓 **Auto-login** — the native xrdp login screen never appears
- 🖥️ **Real fullscreen** at your monitor's native resolution; resize the window and the desktop scales like an image (**no scrollbars**). Windowed mode is one checkbox away.
- 🔎 **Auto-detects** your WSL distributions and Linux username, and shows a live status (installed / not installed / running)
- 🔄 **Self-healing** — starts WSL and xrdp automatically, with a `systemctl → service → direct` fallback for distros without systemd
- 🔐 **Password sync** — optionally set your WSL/Linux password straight from the app, so Linux and remote passwords never drift apart
- 🆕 **Side virtual desktop** option — open Ubuntu on the desktop to the right of yours
- 🚀 **Launch at Windows startup** option
- 🔑 **No secrets in the binary** — credentials live in a git-ignored `config.ini`
- 📦 **Portable & dependency-free** — a single exe built with the C# compiler that ships inside Windows

## Screenshots

| Settings & one-click installer | Ubuntu desktop |
|---|---|
| ![Settings window](docs/settings.png) | ![Ubuntu XFCE desktop](docs/desktop.png) |

---

## Requirements

- Windows 10 or 11 with **WSL2** installed and an **Ubuntu** distro
  (`wsl --install -d Ubuntu` in an admin PowerShell, then reboot)

That's it. The desktop environment itself is installed by the app.

## Quick start

1. **Download** `UbuntuDesktop.exe` (see [Releases](../../releases)) and put it in any folder.
2. **Run it.** On first launch the Settings window opens:
   - Pick your **WSL distribution** (auto-detected).
   - Your **username** is filled in automatically; type your Linux **password**.
   - Click **"Install / Repair Ubuntu desktop"** — a progress window installs XFCE + xrdp and configures everything. Wait for *"Desktop is ready."*
   - Click **Save & Connect.**
3. Your Ubuntu desktop opens **fullscreen, already logged in.** From now on, one double-click is all it takes.

> First launch shows a one-time Windows *"Unknown publisher → Connect"* prompt for the generated `.rdp` file. Click **Connect**. To remove it permanently, see below.

## Settings

Open any time with `UbuntuDesktop.exe /settings`.

| Option | What it does |
|---|---|
| **WSL distribution** | Which distro to launch (dropdown, auto-detected) |
| **Username / Password** | Your Linux credentials — xrdp signs you in against the real Linux user |
| **xrdp port** | Default `3390` (avoids clashing with Windows RDP on 3389) |
| **Install / Repair Ubuntu desktop** | Installs or re-runs the XFCE + xrdp setup inside WSL |
| **Also set this as the WSL/Linux password** | Pushes the typed password into Linux (`chpasswd`) so both stay in sync |
| **Remember password** | Caches it for prompt-free auto-login |
| **Open in a window instead of fullscreen** | Resizable window mode |
| **Open on the side virtual desktop** | Moves one desktop right (Win+Ctrl+Right) and opens there |
| **Launch at Windows startup** | Adds the launcher to your Windows startup |

### Username & password = your Linux credentials

xrdp authenticates against the **real Linux user**, so the remote password *is* your Linux password. Change it in Ubuntu (`passwd`) and update it here, or tick **"Also set this as the WSL/Linux password"** and the app keeps them in sync for you.

## Removing the one-time "Connect" prompt (optional)

Windows shows a one-time *"Unknown publisher"* prompt for any unsigned `.rdp` file. To remove it completely — and make **startup fully hands-free** — run the included script once (it needs your consent because it adjusts your personal certificate trust):

```powershell
powershell -ExecutionPolicy Bypass -File trust-cert.ps1
```

It creates a self-signed code-signing certificate in **your own** (current-user) Trusted Publishers store; the launcher then signs its `.rdp` so mstsc connects silently. Undo with `trust-cert.ps1 -Remove`.

## How it works

1. Detects WSL and your distros (`wsl -l -q`, `WSL_UTF8=1`)
2. If the desktop isn't installed, runs the XFCE + xrdp setup inside WSL with a live log
3. If WSL is down, spawns a hidden holder so the VM boots and stays up
4. Starts xrdp (`systemctl` → `service` → direct daemon fallback)
5. Waits for the xrdp port (resolves `localhost` to IPv4 **and** IPv6 — the WSL proxy often binds `::1` only)
6. Caches the credential with `cmdkey`, writes a temporary `.rdp` (fullscreen, native res, smart-sizing, clipboard), optionally signs it, and launches `mstsc`

## Build from source

No SDK required — the .NET Framework compiler ships with every Windows install:

```cmd
build.cmd
```

`make-icon.ps1` regenerates the app icon.

## FAQ

**How do I run a full Ubuntu desktop from Windows?** Install `wsl --install -d Ubuntu`, then run UbuntuDesktop.exe and click "Install / Repair Ubuntu desktop." No terminal needed.

**Do I have to set up xrdp myself?** No — the app installs and configures XFCE + xrdp for you.

**How do I skip the WSL / xrdp login screen?** The launcher caches your credential and passes it in the `.rdp`, so xrdp logs you in automatically.

**Why was my desktop not fullscreen / showing scrollbars before?** The launcher opens at your monitor's native resolution with smart-sizing on, so it fills the screen and scales like an image.

**Is this different from WSLg?** Yes — WSLg shows individual Linux app windows; UbuntuDesktop opens the whole desktop as one fullscreen session.

**Does it work on Windows 10?** Yes — Windows 10 and 11 with WSL2.

## Security notes

- The password is stored in plain text in `config.ini` and in Windows Credential Manager (`TERMSRV/localhost`). Intended for a **local, single-user machine** — xrdp inside WSL2's NAT is not reachable from the network.
- `config.ini` is git-ignored; only `config.example.ini` is committed.
- `trust-cert.ps1` only touches your **current-user** certificate stores and is fully reversible.

## License

[MIT](LICENSE) — free for personal and commercial use.

---

<sub>Tags: wsl2, wsl, ubuntu, linux-desktop, xrdp, remote-desktop, rdp, xfce, windows, wslg, gui, one-click, auto-login, installer, csharp, portable</sub>
