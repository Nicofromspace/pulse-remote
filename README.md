# Pulse Remote

Your HWiNFO readings, live on your iPhone. A local Windows companion serves a mobile dashboard over your home network. No App Store, Apple developer account, cloud hosting, or subscription to Pulse is needed.

## Start here

1. First-time HWiNFO setup: in its main Settings → General / User Interface, enable **Shared Memory Support**. If you just enabled sharing and Pulse still waits, fully quit and reopen HWiNFO.
2. Open **Start Pulse Remote.cmd**, or **app/Pulse.Remote.exe**. Pulse automatically starts HWiNFO when it is closed. Approve the Windows administrator prompt if shown. It uses Sensors-only mode and minimizes Sensors to the tray. If HWiNFO is already running, Pulse reuses it. Automatic startup can be disabled in the companion window.
3. Run **Allow phone access.cmd** once (also once after upgrading from 0.1) and approve Windows' administrator prompt. This creates Windows Firewall rules limited to this executable, TCP port 48261, UDP port 5353 for local name discovery, and your local subnet. The companion itself runs without administrator privileges. Both Private and Public network profiles are supported because Windows sometimes labels home Ethernet Public.
4. Connect your iPhone to the same home network. Your PC can use Ethernet.
5. Scan the QR code for the **first address** in the companion with your iPhone camera. This is your permanent `pulse-xxxxxxxx.local` address. Open it in Safari.
6. Safari → Share → **Add to Home Screen**. Reuse that icon on future launches; it retains your pairing. A Safari bookmark works too.

The permanent address and pairing key survive app and PC restarts. Local name discovery automatically answers with your PC's current IP address. If your network blocks multicast discovery, choose your home-network IP address from the dropdown as a fallback. On those networks, reserving that IP address in your router makes the fallback stable too.

You only need to move your old IP-based bookmark to the new permanent address once. An IP-based bookmark cannot follow a changed PC address by itself. Phone layouts saved under the old address are separate from those saved under the new name.

HWiNFO startup uses a short-lived elevated helper, which backs up its INI file alongside the original as `HWiNFO64.INI.pulse-backup` and changes only Sensors-only/start-minimized options. It preserves your Shared Memory Support setting and does not reset or bypass the free edition's 12-hour limit. No scheduled task or permanent administrator service is installed. Quit HWiNFO separately if you want to stop it after quitting Pulse. For portable installs, click **Choose HWiNFO…** once to save its location.

## Use it

- **Customize:** select any HWiNFO reading, remove cards, and move them with the arrows. Choices are saved in that browser on that device.
- **Tap a card:** current, minimum, average, maximum, and recent history.
- **All channels:** search all readings.
- **Skins:** Vector (futuristic instrument panel), Carbon (quiet dark dashboard), and Paper (light cream). Choose a skin and accent in Settings; appearance is saved on that device.
- **More skins:** Arcade (phosphor terminal with inset screens), Pitlane (bold motorsport timing cards), and Aurora (soft purple panels). All six support the existing accent and layout options. Arcade uses static texture, without flicker or continuous animation.
- **Settings:** Celsius/Fahrenheit, wide or two-column cards, compact cards, graph/statistics visibility, and restore suggestions. Mobile bottom navigation provides an additional Settings button.
- **Refresh:** choose 1, 2, 5, or 10 seconds. This changes the PC reader and connected dashboards, survives a PC restart, and takes effect within the previous refresh interval. HWiNFO controls when its sensors produce new readings; faster Pulse refresh cannot create extra samples.
- Graph history can show 1, 2, or 5 minutes received while the page is active. HWiNFO's own statistics cover its current monitoring session.
- The companion reads shared memory once per configured interval, independent of the number of connected phones. Phone requests pause while the page is hidden.
- Closing the companion window hides it to the system tray. Right-click the tray icon → **Quit Pulse Remote** to stop sharing.

## HWiNFO Free

HWiNFO is a separately installed proprietary application. Its free edition allows shared-memory access for 12 hours at a time. When sharing expires, enable **Shared Memory Support** again on the PC. Pulse does not automate or bypass that limit. HWiNFO Pro allows uninterrupted shared-memory access.

Pulse reads HWiNFO's published readings. It installs no driver, does not use PawnIO or LibreHardwareMonitor, and does not control clocks, fans, or hardware. HWiNFO itself still uses its own hardware-access mechanisms. This does not constitute an anti-cheat compatibility guarantee.

## Connection help

- The Home Screen layout reserves space for the iPhone status bar and bottom home indicator. Close and reopen your saved app after updating. If iOS retains the old status-bar appearance, remove its Home Screen shortcut and add it again from the same permanent link in Safari. Bottom navigation also provides Settings access.
- Keep the PC awake and both HWiNFO Sensors and Pulse Remote running.
- The QR code contains the pairing key. Treat the link like a local access key. The key is stored in `%LOCALAPPDATA%\PulseRemote\pairing-key.txt`; the permanent hostname and startup preference are stored in `companion.json` alongside it. Browser preferences remain on the phone. Keep these files to retain your connection identity across updates.
- If Safari asks to pair again, paste the full phone link from the companion.
- If Safari cannot open the page, run **Allow phone access.cmd**, check the selected PC address, and check Wi-Fi. Guest networks and VPNs may isolate devices.
- This version is for a trusted home LAN. Traffic uses local HTTP, not encrypted HTTPS. Do not port-forward port 48261. Remote internet access is not implemented.
- If HWiNFO readings stop advancing for 15 seconds, Pulse marks the feed unavailable rather than present old values as live.
- If a sensor disappears, its card shows unavailable. Zero values remain zero.
- No readings? Check the **main** HWiNFO general settings, not just the Sensors window's gear dialog.
- To revoke all paired browsers: quit Pulse, delete only `%LOCALAPPDATA%\PulseRemote\pairing-key.txt`, and reopen Pulse. Scan the new QR code.
- To remove network access, run **Remove-phone-access.ps1** with PowerShell and approve its administrator prompt.

## Build

Requires the .NET 10 SDK for Windows. From the `src` folder:

```powershell
dotnet publish Pulse.Remote.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded -o ../app
```

The HTML/CSS/JavaScript assets are embedded in the executable. The single-file release is large because it includes the Windows desktop and ASP.NET runtimes; download size is not its running memory use. No SDK or separate .NET installation is needed to run the published app.

Useful diagnostics:

```powershell
.\app\Pulse.Remote.exe --probe C:\path\to\readings.json
.\app\Pulse.Remote.exe --self-test C:\path\to\test-results.txt
```

The probe reads real HWiNFO data. The self-test uses an isolated in-memory fixture and does not publish fabricated readings.

## Dependencies and sources

- .NET 10 / ASP.NET Core / Windows Forms — MIT-licensed Microsoft components.
- [QRCoder 1.8.1](https://www.nuget.org/packages/QRCoder/1.8.1) — MIT, local QR generation; see `licenses/QRCoder-LICENSE.txt`.
- [Makaretu.Dns.Multicast 0.27.0](https://www.nuget.org/packages/Makaretu.Dns.Multicast/0.27.0) — MIT, local multicast DNS; see `licenses/` for dependency notices.
- [HWiNFO shared memory](https://www.hwinfo.com/forum/threads/shared-memory-support.18/) — external data interface.
- [HWiNFO licensing](https://www.hwinfo.com/licenses/) — free personal/non-commercial use and shared-memory limitations.

HWiNFO is not bundled. This personal-use prototype is not affiliated with HWiNFO. Review HWiNFO's SDK/commercial terms before distributing or selling an integration.
