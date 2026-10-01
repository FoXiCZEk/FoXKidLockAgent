# FoXKidLockAgent – Windows agent

Nativní WPF agent pro Windows 10/11. Při stavu `locked_studying` zobrazí výukový kiosk na hlavní obrazovce, zakryje vedlejší monitory a zablokuje běžné systémové klávesové zkratky. Stav a seznam blokovaných procesů načítá z API.

## Nastavení

Před instalací upravte `installer/agentsettings.json` a nastavte `serverUrl`, například `https://rodina.example.cz`. Agent volá každé 3 sekundy `POST {serverUrl}/api/agent/heartbeat`. Odpověď musí odpovídat modelu ve specifikaci, zejména obsahovat `status` (`locked_studying` nebo `unlocked_playing`) a volitelně `blockedProcesses`.

Používejte jej pouze na počítačích, které spravujete, a vždy se samostatným administrátorským účtem pro rodiče. Běžný dětský účet nesmí mít práva správce.

## Lokální sestavení

Je vyžadován [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) a pro instalátor [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
dotnet restore FoXKidLockAgent.sln
dotnet publish src/FoXKidLockAgent/FoXKidLockAgent.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true -o dist/agent-win64
iscc installer/setup.iss
```

Výsledkem je `dist/installer/Instalator-RodicovskyZamek-Windows.exe`. Instalátor vyžaduje administrátorská práva, zkopíruje agent do Program Files a vytvoří úlohu Windows pro start při přihlášení s nejvyššími oprávněními.

## GitHub release

Workflow `.github/workflows/release.yml` lze spustit ručně z Actions; uloží instalátor jako artifact. Tag `v3.0.0` navíc založí GitHub Release a přiloží k němu instalační `.exe`.

```powershell
git init
git add .
git commit -m "Initial Windows agent"
git tag v3.0.0
git remote add origin https://github.com/FoXiCZEk/FoXKidLockAgent.git
git push -u origin main --tags
```

Poznámka: Windows nedovoluje aplikaci zachytit `Ctrl+Alt+Del`; to je záměr bezpečnostního modelu systému. WebView2 Runtime je součástí aktuálních Windows 10/11, ale na starších instalacích může být nutné jej nainstalovat zvlášť.
