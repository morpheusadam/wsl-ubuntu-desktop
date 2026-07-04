# Changelog

All notable changes to UbuntuDesktop are documented here.
This project adheres to [Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-07-04

The "zero-terminal" release. You no longer need to touch the Linux command line.

### Added
- **In-app installer** — a new "Install / Repair Ubuntu desktop" button installs
  XFCE + xrdp inside WSL, sets the port, adds xrdp to the `ssl-cert` group, writes
  `~/.xsession`, and enables the service, all with a live streaming log window.
- **Auto-detection** — the Settings window lists your WSL distributions in a
  dropdown, auto-fills your Linux username, and shows a live status line
  (green "ready" / amber "not installed yet").
- **First-run flow** — if WSL is missing, the distro isn't set up, or no
  credentials exist, the launcher opens Settings automatically.
- **Windowed mode** — optional resizable window instead of fullscreen.
- **systemd-less fallback** — xrdp is started via `systemctl`, then `service`,
  then the daemons directly, so it works on distros without systemd.
- `WSL_UTF8=1` for clean parsing of `wsl` output.
- `LICENSE` (MIT) and this changelog.
- Help link and version number in the Settings window.

### Changed
- README rewritten around the one-click auto-setup flow, with an FAQ and SEO tags.
- `config.ini` is now self-documenting (wp-config-style comments for every key).

## [1.1.0] - 2026-07-04

### Fixed
- Port-readiness check now resolves `localhost` to both IPv4 and IPv6 and calls
  `EndConnect` — the WSL localhost proxy often binds `::1` only, which caused a
  false "did not come up in 40 seconds" error.

### Changed
- "Open on a new virtual desktop" now moves **one** desktop to the right
  (Win+Ctrl+Right) instead of creating a new desktop on every launch.

## [1.0.0] - 2026-07-04

### Added
- One-click launcher: boots WSL, starts xrdp, auto-logs in, opens the Ubuntu
  XFCE desktop fullscreen.
- Auto-login (no xrdp login screen), fullscreen at native resolution with
  smart-sizing, password sync, settings window, optional `.rdp` signing +
  `trust-cert.ps1` to remove the unsigned-file prompt.
