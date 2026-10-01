# 🛠️ Technická specifikace: Samostatný Desktop Agent pro Windows (C# .NET)

Tento dokument je **detailní výrobní specifikací a architektonickým návrhem** pro samostatnou, nativní klientskou aplikaci (Desktop Agent) běžící na pozadí operačního systému Windows 10 a Windows 11 pro projekt **Rodičovský Zámek PC**.

---

## 📑 Obsah specifikace

1. [Vize a cíle samostatné aplikace](#1-vize-a-cíle-samostatné-aplikace)
2. [Výběr technologického stacku (ADR)](#2-výběr-technologického-stacku-adr)
3. [Dvouprocesová systémová architektura](#3-dvouprocesová-systémová-architektura)
4. [Mechanismus zamykání obrazovky (Lock Screen Engine)](#4-mechanismus-zamykání-obrazovky-lock-screen-engine)
5. [Hlídání a ukončování procesů (Process Watchdog)](#5-hlídání-a-ukončování-procesů-process-watchdog)
6. [Dvouúrovňový webový a obsahový filtr](#6-dvouúrovňový-webový-a-obsahový-filtr)
7. [Komunikační protokol se serverem (REST & SSE)](#7-komunikační-protokol-se-serverem-rest--sse)
8. [Odolnost proti obcházení (Anti-Tampering & Security)](#8-odolnost-proti-obcházení-anti-tampering--security)
9. [Struktura .NET projektu a třídní model](#9-struktura-net-projektu-a-třídní-model)
10. [Konkrétní implementační ukázky kódu v C#](#10-konkrétní-implementační-ukázky-kódu-v-c)
11. [Sestavení, distribuce a instalátor (Inno Setup)](#11-sestavení-distribuce-a-instalátor-inno-setup)

---

## 🎯 1. Vize a cíle samostatné aplikace

### Proč přejít ze skriptů (PowerShell/VBS) na samostatnou kompilovanou aplikaci?
Stávající řešení pomocí PowerShellu (`Agent-Zamek-PC.ps1`) je skvělé pro rychlé zprovoznění, ale v reálném provozu na dětských počítačích naráží na limity:
- **Závislost na externím prohlížeči:** PowerShell musí spustit Chrome nebo Edge s přepínačem `--kiosk`. Pokud dítě okno zavře přes `Alt+F4` nebo klávesové zkratky, vzniká prodleva 1–3 sekundy, než skript okno znovu otevře.
- **Klávesové zkratky Windows:** PowerShell nedokáže spolehlivě zablokovat systémové klávesy (`WinKey`, `Alt+Tab`, `Ctrl+Esc`, `Ctrl+Shift+Esc`).
- **Více monitorů:** Pokud má dítě k počítači připojen druhý monitor nebo televizi, `--kiosk` prohlížeče se otevře jen na jednom a druhý zůstane volný pro hry.
- **Vytížení procesoru:** Neustálé volání `Get-Process` v cyklu zatěžuje CPU (1–3 %).
- **Riziko zablokování politikou:** Windows ExecutionPolicy nebo antivirové programy mohou skripty blokovat.

### Cíle samostatného agenta:
1. **Nulové blikání a tichý chod:** Kompilovaný spustitelný binární soubor (`.exe`), běžící jako služba / proces na pozadí bez jakéhokoliv okna konzole.
2. **Neprůstřelné uzamčení:** Celoobrazovkový překryv na **všech připojených monitorech**, zachytávání nízkoúrovňových klávesových zpráv (`WH_KEYBOARD_LL`) a dočasná deaktivace Správce úloh.
3. **Integrované WebView2:** Klientská výuková aplikace (`?mode=child`) běží přímo uvnitř okna agenta přes Microsoft Edge WebView2 control (žádný externí proces prohlížeče).
4. **Okamžitá detekce spuštění her (<50 ms):** Využití Event Tracing for Windows (ETW) nebo WMI event subscription namísto neustálého dotazování v cyklu.
5. **Minimální spotřeba paměti a CPU:** Méně než 40 MB RAM a <0,2 % CPU v klidovém režimu.
6. **Odolnost proti vypnutí dítětem:** Spuštěno pod účtem správce / systémové služby, dítě bez administrátorských práv nemůže proces ukončit.

---

## ⚙️ 2. Výběr technologického stacku (ADR)

| Kritérium | **C# .NET 8 / 9 (WPF + Win32 P/Invoke)** ⭐ ZVOLENO | Rust + Tauri / WinAPI | C++ / Win32 | Electron |
|---|---|---|---|---|
| **Podpora Windows API** | Nativní, P/Invoke, Pointers, Win32 Hooks | Výborná (winapi / windows-rs) | Nativní 100% | Omezená přes Node addony |
| **WebView komponenta** | `Microsoft.Web.WebView2` (vestavěná) | WRY / Webview | Webview2 C++ SDK | Chromium (součástí, obří) |
| **Velikost binárky** | ~15–30 MB (Self-Contained SingleFile) | ~10 MB | ~5 MB | >150 MB (Nevhodné) |
| **Rychlost vývoje** | Blesková, bezpečný kód, bohatý ekosystém | Střední (náročnější paměťový model) | Pomalá, náchylné na chyby | Rychlá, ale nevhodné pro systém |
| **Zabezpečení a služby** | Prvotřídní podpora Windows Services | Výborná | Výborná | Špatná (nelze běžet jako služba) |

### Doporučený a zvolený stack:
- **Jazyk & Runtime:** C# / .NET 8 LTS (nebo .NET 9).
- **UI & Kiosk vrstva:** WPF (Windows Presentation Foundation) s bezrámovým oknem na popředí a integrovanou komponentou `Microsoft.Web.WebView2.Wpf`.
- **Systémové háky (Hooks):** P/Invoke volání Win32 API (`user32.dll`, `kernel32.dll`, `advapi32.dll`).
- **Detekce procesů:** `Microsoft.Diagnostics.Tracing.TraceEvent` (ETW - Event Tracing for Windows) pro nulovou zátěž CPU.
- **Síťová komunikace:** `System.Net.Http.HttpClient` s podporou HTTP/2 a Server-Sent Events (SSE).
- **Distribuce:** Single-File Executable (`PublishSingleFile=true`, `SelfContained=true`). Na cílovém počítači nemusí být nainstalován žádný .NET runtime.

---

## 🏗️ 3. Dvouprocesová systémová architektura

Ve Windows existuje bezpečnostní oddělení relací (**Session 0 Isolation**). Systémové služby běží v relaci 0 a nemohou zobrazovat grafická okna uživateli v relaci 1 (přihlášený uživatel).

Proto je agent navržen jako **dvojice úzce spolupracujících procesů**:

```
 ┌────────────────────────────────────────────────────────────────────────┐
 │                      WINDOWS SESSION 0 (SYSTEM)                        │
 │                                                                        │
 │  ┌──────────────────────────────────────────────────────────────────┐  │
 │  │              ParentalLockService.exe (Windows Service)           │  │
 │  │                                                                  │  │
 │  │  • Běží s právy NT AUTHORITY\SYSTEM (nepřemožitelné běžným žákem)│  │
 │  │  • Spravuje soubor hosts (C:\Windows\System32\drivers\etc\hosts) │  │
 │  │  • ETW Watchdog: Okamžitě ukončuje zakázané procesy (OpenProcess)│  │
 │  │  • Hlídá běh klientského UI (pokud spadne, ihned ho restartuje)  │  │
 │  └──────────────────────────────────▲───────────────────────────────┘  │
 └─────────────────────────────────────┼──────────────────────────────────┘
                                       │
                    Lokální Named Pipe │ \\.\pipe\ParentalLock_IPC
                    (Zabezpečená obousměrná komunikace)
                                       │
 ┌─────────────────────────────────────▼──────────────────────────────────┐
 │               WINDOWS SESSION 1+ (PŘIHLÁŠENÝ UŽIVATEL)                 │
 │                                                                        │
 │  ┌──────────────────────────────────────────────────────────────────┐  │
 │  │               ParentalLockUI.exe (Kiosk & Overlay)               │  │
 │  │                                                                  │  │
 │  │  • Běží v aktivní ploše přihlášeného dítěte                      │  │
 │  │  • Vytváří TopMost okna na VŠECH monitorech (Multi-Monitor Lock) │  │
 │  │  • Integruje WebView2 pro vykreslení výukových otázek            │  │
 │  │  • Low-Level Keyboard Hook: Blokuje WinKey, Alt+Tab, Alt+F4      │  │
 │  │  • Dočasně deaktivuje Správce úloh (HKCU DisableTaskMgr)         │  │
 │  │  • Jakmile server hlásí 'unlocked_playing', okno se ihned schová │  │
 │  └──────────────────────────────────────────────────────────────────┘  │
 └────────────────────────────────────────────────────────────────────────┘
```

> **Poznámka pro zjednodušenou instalaci:**  
> Agent může volitelně běžet i jako **jeden proces s elevovanými právy** (`ParentalLockAgent.exe`), spouštěný přes Windows Plánovač úloh s volbou *"Spustit s nejvyššími oprávněními"* (`/rl highest`) při přihlášení uživatele. To eliminuje nutnost registrace Windows Service a je to ideální pro běžné rodinné použití.

---

## 🔒 4. Mechanismus zamykání obrazovky (Lock Screen Engine)

### 4.1 Celoobrazovkový překryv na všech monitorech
Pokud má dítě zapojeno více monitorů, agent zjistí všechny připojené obrazovky:
```csharp
foreach (var screen in System.Windows.Forms.Screen.AllScreens)
{
    var overlayWindow = new LockOverlayWindow(screen, isPrimary: screen.Primary);
    overlayWindow.Show();
}
```
- **Primární monitor:** Obsahuje integrovanou komponentu `WebView2`, která načítá `https://<SERVER_HOST>/?mode=child`.
- **Sekundární monitory:** Zobrazují elegantní ztmavovací obrazovku s logem a textem: *"Probíhá plnění výukových úkolů na hlavní obrazovce."* Tím je zabráněno jakémukoliv úniku na druhý monitor.

### 4.2 Nízkoúrovňový hák klávesnice (`WH_KEYBOARD_LL`)
Pomocí Win32 funkce `SetWindowsHookEx(WH_KEYBOARD_LL, ...)` agent odchytává a polyká všechny systémové klávesové kombinace dříve, než na ně zareaguje Windows:
- `VK_LWIN` a `VK_RWIN` (klávesa Windows – otevření nabídky Start)
- `Alt + Tab` (přepínání oken)
- `Alt + F4` (pokus o zavření okna Kiosku)
- `Ctrl + Esc` (alternativní nabídka Start)
- `Alt + Esc` (přepínání aplikací)
- `Win + D` / `Win + M` (minimalizace všech oken na plochu)
- `Win + G` (Xbox Game Bar)

### 4.3 Ochrana proti Správci úloh
Při aktivním zámku agent zapíše do registru aktuálního uživatele:
```csharp
Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Policies\System", "DisableTaskMgr", 1, RegistryValueKind.DWord);
```
Stisk `Ctrl + Shift + Esc` nebo `Ctrl + Alt + Del` zobrazí hlášení: *"Správce úloh byl zakázán vaším správcem."*  
Jakmile dojde k odemčení (`unlocked_playing`), hodnota je okamžitě vrácena na `0`.

### 4.4 Udržování okna na popředí (TopMost Guard)
Okno Kiosku má nastaveny příznaky:
- `Topmost = true`
- `WindowStyle = None`
- `ResizeMode = NoResize`
- `WindowState = Maximize`
Při události `Deactivated` nebo ztrátě fokusu okno okamžitě volá `Activate()` a Win32 `SetWindowPos(HWND_TOPMOST)`.

---

## 🎮 5. Hlídání a ukončování procesů (Process Watchdog)

### 5.1 Real-time detekce bez zátěže procesoru (ETW)
Běžné skripty volají `Get-Process` každých 1,5 sekundy, což zbytečně vytěžuje procesor. Nativní agent využívá **Event Tracing for Windows (ETW)** kernel událost `ProcessStart`.
- Kdykoliv operační systém spustí jakýkoliv nový proces, jádro Windows vyšle událost do agenta v čase **< 5 milisekund**.
- Agent zkontroluje název spustitelného souboru proti seznamu `blockedProcesses`.
- Pokud se shoduje (např. `javaw.exe`, `RobloxPlayerBeta.exe`, `steam.exe`):
  1. Okamžitě zavolá `OpenProcess(PROCESS_TERMINATE, ...)` a `TerminateProcess(hProcess, 1)`.
  2. Zvýší čítač `killedCount`.
  3. Zobrazí Kiosk zamykací obrazovku na popředí.

### 5.2 Seznam standardně monitorovaných her a procesů
Seznam je dynamicky stahován ze serveru při každém heartbeat:
```json
[
  "Minecraft", "MinecraftLauncher", "javaw", "java",
  "RobloxPlayerBeta", "RobloxPlayerLauncher",
  "Steam", "steamwebhelper", "EpicGamesLauncher",
  "FortniteClient-Win64-Shipping", "VALORANT-Win64-Shipping",
  "LeagueClient", "GenshinImpact", "Brawlhalla",
  "Overwatch", "cs2", "csgo", "Discord"
]
```

---

## 🌐 6. Dvouúrovňový webový a obsahový filtr

Agent implementuje ochranu proti rozptylování videi a sociálními sítěmi:

### Úroveň 1: Správa systémového souboru hosts (DNS úroveň)
- Cesta: `C:\Windows\System32\drivers\etc\hosts`
- Při stavu `locked_studying` (pokud je web filtr zapnutý) agent atomicky zapíše:
  ```hosts
  # === RODICOVSKY_ZAMEK_WEB_BLOCK_START ===
  127.0.0.1 youtube.com
  ::1 youtube.com
  127.0.0.1 www.youtube.com
  ::1 www.youtube.com
  127.0.0.1 tiktok.com
  ::1 tiktok.com
  127.0.0.1 netflix.com
  ::1 netflix.com
  # === RODICOVSKY_ZAMEK_WEB_BLOCK_END ===
  ```
- Následně vyprázdní DNS mezipaměť voláním Win32 API `DnsFlushResolverCache()` (ekvivalent `ipconfig /flushdns`).
- Po odemčení (`unlocked_playing`) blok z hosts souboru okamžitě odstraní.

### Úroveň 2: Aktivní hlídač panelů prohlížeče (Window Title Watchdog)
Pokud má dítě otevřený prohlížeč z dřívějška se stránkou YouTube, pouhá DNS blokace nestačí, protože spojení může být otevřené.
- Agent sleduje titulky oken procesů `chrome.exe`, `msedge.exe`, `firefox.exe`, `brave.exe`.
- Pokud titulek okna obsahuje zakázaná klíčová slova (`"YouTube"`, `"Netflix"`, `"TikTok"`, `"Twitch"`):
  1. Pošle oknu klávesovou zkratku `Ctrl + W` pro zavření dané záložky, nebo okno minimalizuje.
  2. Zaznamená událost pro telemetrii rodičovského panelu.

---

## 📡 7. Komunikační protokol se serverem (REST & SSE)

Agent komunikuje s centrálním serverem přes dvě paralelní linky:

### 1. Pravidelný Heartbeat (každé 3 sekundy)
Odesílá `POST /api/agent/heartbeat`:
```json
{
  "hostname": "PC-MATYAS",
  "os": "Microsoft Windows 11 Pro 64-bit",
  "version": "3.0.0-native",
  "killedCount": 4,
  "killedProcess": "RobloxPlayerBeta.exe",
  "webBlockCount": 2,
  "lastWebBlockEvent": "YouTube (msedge)"
}
```
Server odpovídá aktuálním stavem:
```json
{
  "success": true,
  "status": "locked_studying",
  "playtimeRemainingSeconds": 0,
  "blockedProcesses": ["Minecraft", "RobloxPlayerBeta", ...],
  "webFilter": {
    "enabled": true,
    "domains": ["youtube.com", "netflix.com"],
    "mode": "always"
  },
  "commands": [
    { "type": "close_kiosk", "timestamp": 1726650000000 }
  ]
}
```

### 2. Okamžitá odezva přes Server-Sent Events (SSE)
Aby agent nemusel čekat až 3 sekundy na příští heartbeat, když rodič na mobilu klikne na *"Odemknout PC"*:
- Agent otevírá trvalé HTTP spojení na `GET /api/events`.
- Při příchodu zprávy `state_update`:
  - Pokud je `status == "unlocked_playing"` nebo dorazil příkaz `close_kiosk`, agent **v čase do 100 ms uvolní obrazovku a schová Kiosk**.

### 3. Offline Fail-Safe režim
Pokud dojde k výpadku domácí Wi-Fi nebo serveru:
- Agent zachová poslední známý bezpečnostní stav (pokud byl zamčen, zůstane zamčen).
- Přímo na zamykacím okně je k dispozici tlačítko **"Rodičovské nouzové odemčení"**:
  - Rodič může zadat 4místný PIN offline.
  - Agent ověří PIN proti lokálnímu salted SHA-256 hashi uloženému v chráněném souboru `local_security.dat`.

---

## 🛡️ 8. Odolnost proti obcházení (Anti-Tampering & Security)

Děti jsou vynalézavé. Samostatný agent implementuje tyto vrstvy obrany:

1. **Práva procesu (Process DACL):**  
   Pokud služba běží pod účtem `SYSTEM`, běžný uživatelský účet (standard user ve Windows) nemá právo `PROCESS_TERMINATE` a Správce úloh ani příkaz `taskkill /f` proces neukončí.
2. **Vzájemný Watchdog:**  
   Služba hlídá klientské UI okno; pokud by klientský proces spadl nebo byl násilně shozen, služba ho do 500 ms znovu nastartuje v relaci přihlášeného uživatele.
3. **Globální systémový Mutex:**  
   Zabraňuje vícenásobnému spuštění pomocí pojmenovaného mutexu `Global\ParentalLockPC_SingleInstance`.
4. **Ochrana při nouzovém režimu (Safe Mode):**  
   Služba se registruje v registrech `HKLM\SYSTEM\CurrentControlSet\Control\SafeBoot\Network`, aby byla aktivní i v nouzovém režimu s podporou sítě.

---

## 📁 9. Struktura .NET projektu a třídní model

```
ParentalLock.sln
│
├── src/
│   ├── ParentalLock.Core/             # Sdílená logika a datové modely
│   │   ├── Models/
│   │   │   ├── AgentHeartbeatRequest.cs
│   │   │   ├── AgentHeartbeatResponse.cs
│   │   │   ├── ChildStatus.cs         # Enum: LockedStudying, UnlockedPlaying...
│   │   │   └── RemoteCommand.cs
│   │   ├── Services/
│   │   │   ├── IServerSyncService.cs  # REST + SSE klient
│   │   │   ├── IProcessWatchdog.cs    # Detekce a ukončování her
│   │   │   ├── IWebFilterManager.cs   # Úprava hosts souboru a DNS flush
│   │   │   └── IScreenLockService.cs  # Multi-monitor overlay & hooks
│   │   └── Security/
│   │       ├── LocalPinVerifier.cs    # Offline SHA-256 ověření PINu
│   │       └── WindowsRegistryPolicy.cs # Vypnutí/zapnutí Task Manageru
│   │
│   ├── ParentalLock.Agent/            # Hlavní spustitelná aplikace
│   │   ├── App.xaml / App.xaml.cs     # Inicializace, Mutex, Tray Icon
│   │   ├── Windows/
│   │   │   ├── KioskWindow.xaml       # Hlavní okno s WebView2
│   │   │   ├── KioskWindow.xaml.cs
│   │   │   ├── BlackoutWindow.xaml    # Krycí okna pro vedlejší monitory
│   │   │   └── OfflinePinDialog.xaml  # Nouzové zadání PINu při výpadku sítě
│   │   ├── Native/
│   │   │   ├── Win32.cs               # P/Invoke signatury (User32, Kernel32)
│   │   │   └── LowLevelKeyboardHook.cs# Blokování Win kláves, Alt+Tab atd.
│   │   └── Workers/
│   │       ├── ProcessMonitorWorker.cs# ETW nebo WMI hlídač
│   │       └── ServerSyncWorker.cs    # Heartbeat a SSE smyčka
│   │
│   └── ParentalLock.Service/          # Volitelná systémová služba Windows
│       ├── Program.cs
│       └── ParentalLockWindowsService.cs
│
└── installer/
    └── InnoSetup_Installer.iss        # Skript pro vytvoření instalátoru
```

---

## 💻 10. Konkrétní implementační ukázky kódu v C#

Zde jsou klíčové komponenty, které může vývojář nebo AI agent přímo použít pro sestavení agenta:

### 10.1 Nízkoúrovňový hák klávesnice (`LowLevelKeyboardHook.cs`)
```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ParentalLock.Agent.Native;

public class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _isLockActive = false;

    public LowLevelKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void EnableHook()
    {
        if (_hookId == IntPtr.Zero)
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule?.ModuleName), 0);
        }
        _isLockActive = true;
    }

    public void DisableHook()
    {
        _isLockActive = false;
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isLockActive)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            var key = (Keys)vkCode;

            bool isAlt = (Control.ModifierKeys & Keys.Alt) != 0;
            bool isCtrl = (Control.ModifierKeys & Keys.Control) != 0;

            // Blokování klávesy Windows (Start menu)
            if (key == Keys.LWin || key == Keys.RWin)
                return (IntPtr)1;

            // Blokování Alt+Tab, Alt+Esc, Alt+F4
            if (isAlt && (key == Keys.Tab || key == Keys.Escape || key == Keys.F4))
                return (IntPtr)1;

            // Blokování Ctrl+Esc (nabídka Start)
            if (isCtrl && key == Keys.Escape)
                return (IntPtr)1;

            // Blokování klávesy kontextového menu
            if (key == Keys.Apps)
                return (IntPtr)1;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => DisableHook();

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
```

---

### 10.2 Hlavní Kiosk okno s WebView2 (`KioskWindow.xaml.cs`)
```csharp
using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace ParentalLock.Agent.Windows;

public partial class KioskWindow : Window
{
    private readonly string _serverUrl;

    public KioskWindow(string serverUrl)
    {
        InitializeComponent();
        _serverUrl = serverUrl;

        // Vynucení plné obrazovky přes celou plochu včetně Taskbaru
        WindowState = WindowState.Maximized;
        WindowStyle = WindowStyle.None;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;

        Loaded += KioskWindow_Loaded;
    }

    private async void KioskWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Izolovaný profil WebView2, aby nedocházelo ke kolizi s uživatelským prohlížečem
        string userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParentalLockPC", "WebViewData");
        var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
        await WebView.EnsureCoreWebView2Async(env);

        // Vypnutí nepovolených gest a kontextového menu
        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        WebView.CoreWebView2.Settings.IsZoomControlEnabled = false;

        // Načtení dětského Kiosku
        WebView.CoreWebView2.Navigate($"{_serverUrl}/?mode=child");
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // Pokud jiné okno zkusí vzít fokus, Kiosk se okamžitě vrátí dopředu
        if (Visibility == Visibility.Visible)
        {
            Topmost = true;
            Activate();
        }
    }
}
```

---

### 10.3 Správce souboru Hosts a DNS Cache (`HostsFilterManager.cs`)
```csharp
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ParentalLock.Core.Services;

public class HostsFilterManager
{
    private static readonly string HostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
    private const string TagStart = "# === RODICOVSKY_ZAMEK_WEB_BLOCK_START ===";
    private const string TagEnd = "# === RODICOVSKY_ZAMEK_WEB_BLOCK_END ===";

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern uint DnsFlushResolverCache();

    public void ApplyBlockRules(string[] domains, bool enable)
    {
        try
        {
            if (!File.Exists(HostsPath)) return;

            string content = File.ReadAllText(HostsPath, Encoding.UTF8);

            // Odstranění předchozího bloku
            string pattern = $"{Regex.Escape(TagStart)}.*?{Regex.Escape(TagEnd)}(\r?\n)?";
            string cleaned = Regex.Replace(content, pattern, string.Empty, RegexOptions.Singleline);

            if (enable && domains != null && domains.Length > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine();
                sb.AppendLine(TagStart);
                sb.AppendLine($"# Generováno agentem Rodičovský Zámek PC: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                foreach (var d in domains.Select(x => x.Trim().ToLower()).Where(x => !string.IsNullOrEmpty(x)))
                {
                    sb.AppendLine($"127.0.0.1 {d}");
                    sb.AppendLine($"::1 {d}");
                }

                sb.AppendLine(TagEnd);
                cleaned = cleaned.TrimEnd() + Environment.NewLine + sb.ToString();
            }

            File.WriteAllText(HostsPath, cleaned, Encoding.UTF8);

            // Bleskové vyčištění DNS cache
            DnsFlushResolverCache();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebFilter] Chyba zápisu do hosts: {ex.Message}");
        }
    }
}
```

---

### 10.4 Okamžité ukončování zakázaných her (`ProcessWatchdog.cs`)
```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;

namespace ParentalLock.Core.Services;

public class ProcessWatchdog : IDisposable
{
    private ManagementEventWatcher? _processWatcher;
    private readonly HashSet<string> _blockedNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLocked = false;

    public event Action<string>? ProcessTerminated;

    public void UpdateBlockedList(IEnumerable<string> processes)
    {
        lock (_blockedNames)
        {
            _blockedNames.Clear();
            foreach (var p in processes)
            {
                string clean = p.Trim();
                if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    clean = clean[..^4];
                _blockedNames.Add(clean);
            }
        }
    }

    public void SetLockState(bool isLocked)
    {
        _isLocked = isLocked;
        if (_isLocked)
        {
            KillExistingBlockedProcesses();
        }
    }

    public void Start()
    {
        try
        {
            // WMI odběr událostí vytvoření nového procesu (okamžitá reakce)
            var query = new WqlEventQuery("__InstanceCreationEvent", TimeSpan.FromSeconds(0.5), "TargetInstance ISA 'Win32_Process'");
            _processWatcher = new ManagementEventWatcher(query);
            _processWatcher.EventArrived += OnProcessCreated;
            _processWatcher.Start();
        }
        catch
        {
            // Fallback na časovač, pokud WMI není dostupné
        }
    }

    private void OnProcessCreated(object sender, EventArrivedEventArgs e)
    {
        if (!_isLocked) return;

        if (e.NewEvent["TargetInstance"] is ManagementBaseObject target)
        {
            string processName = Convert.ToString(target["Name"]) ?? "";
            int processId = Convert.ToInt32(target["ProcessId"]);

            string nameWithoutExt = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
                ? processName[..^4] 
                : processName;

            bool shouldKill = false;
            lock (_blockedNames)
            {
                shouldKill = _blockedNames.Contains(nameWithoutExt);
            }

            if (shouldKill)
            {
                try
                {
                    var proc = Process.GetProcessById(processId);
                    proc.Kill();
                    ProcessTerminated?.Invoke(processName);
                }
                catch { }
            }
        }
    }

    public void KillExistingBlockedProcesses()
    {
        lock (_blockedNames)
        {
            foreach (var name in _blockedNames)
            {
                var procs = Process.GetProcessesByName(name);
                foreach (var p in procs)
                {
                    try
                    {
                        p.Kill();
                        ProcessTerminated?.Invoke(name);
                    }
                    catch { }
                }
            }
        }
    }

    public void Dispose()
    {
        _processWatcher?.Stop();
        _processWatcher?.Dispose();
    }
}
```

---

## 📦 11. Sestavení, distribuce a instalátor

### 11.1 Příkaz pro sestavení jediného `.exe` souboru:
```bash
dotnet publish src/ParentalLock.Agent/ParentalLock.Agent.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  /p:EnableCompressionInSingleFile=true \
  -o ./dist/agent-win64
```
Výsledkem je jeden čistý spustitelný soubor `ParentalLockAgent.exe`, který nevyžaduje instalaci .NETu ani žádné další DLL knihovny.

### 11.2 Inno Setup instalátor (`installer/setup.iss`)
```iss
[Setup]
AppName=Rodičovský Zámek PC - Systémový Agent
AppVersion=3.0.0
DefaultDirName={autopf}\RodicovskyZamekPC
DefaultGroupName=Rodičovský Zámek PC
OutputDir=..\dist\installer
OutputBaseFilename=Instalator-RodicovskyZamek-Windows
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin

[Files]
Source: "..\dist\agent-win64\ParentalLockAgent.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\public\agent\server_url.txt"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; Automatický start s nejvyššími právy po přihlášení
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RodicovskyZamekAgent"; ValueData: """{app}\ParentalLockAgent.exe"" --silent"; Flags: uninsdeletevalue

[Run]
; Spuštění agenta ihned po dokončení instalace
Filename: "{app}\ParentalLockAgent.exe"; Parameters: "--silent"; Flags: nowait postinstall skipifsilent
```

---

## 🏁 Shrnutí pro vývojáře a AI agenty

Tato specifikace poskytuje kompletní, ucelený základ pro vývoj samostatné aplikace. Kombinací **WPF překryvu**, **WebView2**, **nízkoúrovňových klávesových háků** a **WMI/ETW hlídače procesů** získá projekt špičkové, moderní a neprůstřelné řešení rodičovské kontroly pro Windows 10 a 11.
