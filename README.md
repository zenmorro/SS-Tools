# ForensicKit

> Nome provvisorio — modificabile.

**ForensicKit** è un'applicazione desktop Windows **open source** che funge da
**launcher / aggregatore** per tool forensi e di analisi di sistema di terze parti.
Non ridistribuisce i tool: li scarica dalle rispettive fonti ufficiali, ne verifica
l'integrità, li esegue e tiene traccia di download ed esecuzioni.

![status](https://img.shields.io/badge/.NET-8.0-blueviolet) ![license](https://img.shields.io/badge/license-MIT-green)

---

## Caratteristiche

- 🗂️ **Catalogo a card/dashboard** (dark theme, Fluent Design) organizzato per categoria,
  con badge di stato (*Non scaricato / Scaricato / Aggiornamento disponibile*), ricerca e preferiti.
- ⬇️ **Downloader** asincrono con progress bar, **resume** (HTTP Range), **retry** con
  back-off e **verifica SHA-256**.
- 📦 **Estrazione** automatica degli archivi ZIP (con protezione *zip-slip*).
- ▶️ **Esecutore** dei tool con eventuale **elevazione UAC** (`runas`), argomenti
  personalizzabili e cattura opzionale di stdout/stderr per i tool a riga di comando.
- 🧾 **Script PowerShell integrati** (inclusi nell'app, **non** scaricati da remoto),
  con console integrata ed export in `.txt` / `.json`.
- 🔄 **Manifest aggiornabile**: il catalogo è un file JSON scaricabile da un repo GitHub,
  con **validazione** e **fallback** al manifest locale/incluso se offline o malformato.
- 🛡️ **Sicurezza & trasparenza**: dialog pre-avvio con **fonte, hash e firma digitale**,
  log **append-only** (chain-of-custody), nessuna manomissione di SmartScreen/Defender.

## Architettura

```
/src
  /ForensicKit.App        WPF (WPF-UI, MVVM con CommunityToolkit.Mvvm) — UI
  /ForensicKit.Core       Logica: download, esecuzione, manifest, hash, firma, log
  /ForensicKit.Scripts    Script .ps1 (embedded come risorse nell'app)
/tests
  /ForensicKit.Core.Tests xUnit — test della logica Core
/manifests
  tools.json              Catalogo ufficiale (PR-friendly)
/docs
  CONTRIBUTING.md         Come aggiungere un tool al manifest
README.md
LICENSE
```

Lo **stack**: C# / **.NET 8** (Windows Desktop) · **WPF** + **WPF-UI** (Fluent, dark/light) ·
**MVVM** (CommunityToolkit.Mvvm) · `HttpClient` con resume/progress ·
`System.Diagnostics.Process` (+ `Verb = "runas"`) · persistenza **JSON** in `%APPDATA%\ForensicKit`.

> La UI (`ForensicKit.App`) dipende dal Core ma il Core **non** dipende dalla UI: tutta la
> logica è isolata dietro interfacce e coperta da unit test.

## Requisiti

- Windows 10 / 11 (x64)
- Per lo sviluppo: **.NET SDK 8.0+** (il progetto usa `net8.0-windows`)

## Build

```powershell
# Ripristino e build dell'intera soluzione
dotnet build ForensicKit.slnx -c Release

# Esecuzione dei test
dotnet test tests/ForensicKit.Core.Tests/ForensicKit.Core.Tests.csproj

# Avvio in sviluppo
dotnet run --project src/ForensicKit.App/ForensicKit.App.csproj
```

## Pubblicazione (single-file self-contained)

```powershell
dotnet publish src/ForensicKit.App/ForensicKit.App.csproj -c Release -o publish
```

Produce un unico `publish/ForensicKit.exe` (~70 MB) che include il runtime .NET:
non richiede installazioni sulla macchina di destinazione.

## Il manifest (catalogo)

Il catalogo **non è hardcoded**: è definito in [`manifests/tools.json`](manifests/tools.json)
ed è aggiornabile da remoto (`raw.githubusercontent.com`). Schema di una voce:

```jsonc
{
  "id": "everything",
  "name": "Everything",
  "author": "voidtools",
  "category": "Ricerca file",
  "description": "Motore di ricerca file istantaneo basato su MFT.",
  "sourceType": "direct_url",          // direct_url | github_release
  "downloadUrl": "https://.../Everything-1.4.1.1026.x64.zip",
  "sha256": "opzionale",
  "packageType": "zip",                // zip | exe
  "executable": "Everything.exe",
  "requiresElevation": false,
  "args": "",
  "homepage": "https://www.voidtools.com/downloads/",
  "version": "1.4.1.1026"
}
```

Per i tool ospitati su GitHub usare `"sourceType": "github_release"` con `owner`/`repo`
(e opzionalmente `assetPattern`): l'app interroga
`/repos/{owner}/{repo}/releases/latest` e scarica sempre l'ultima release stabile.

Vedi [`docs/CONTRIBUTING.md`](docs/CONTRIBUTING.md) per aggiungere un nuovo tool.

## Dove vengono salvati i dati

Tutto sotto `%APPDATA%\ForensicKit`:

| Percorso | Contenuto |
|---|---|
| `settings.json` | Impostazioni utente |
| `tools.json` | Copia locale (cache) del manifest remoto |
| `Tools\{id}\` | File estratti di ogni tool + `.forensickit.json` (versione, hash) |
| `Logs\audit.log.jsonl` | Log **append-only** di download ed esecuzioni |

## Script PowerShell inclusi (proof of concept)

Tutti in `src/ForensicKit.Scripts/`, **inclusi nell'eseguibile** e ispezionabili:

| Script | Descrizione | Elevazione |
|---|---|---|
| `Collect-SystemInfo.ps1` | Snapshot OS, hardware, dischi, rete, utenti | No |
| `List-SuspiciousProcesses.ps1` | Processi in percorsi insoliti, immagini non firmate, servizi auto-start | No |
| `Get-UsbDeviceHistory.ps1` | Storico dispositivi USB (USBSTOR) | Consigliata |
| `Get-RecentSecurityEvents.ps1` | Logon/logon falliti/lockout/nuovi servizi | Sì |

## Sicurezza

- Prima del **primo avvio** di un tool scaricato, ForensicKit mostra un avviso con
  **fonte ufficiale, URL di download, SHA-256 e firma digitale** (Authenticode).
- L'app **non disabilita** SmartScreen/Windows Defender e **non modifica** le protezioni di sistema.
- Il log delle esecuzioni è **append-only** per preservare una semplice chain-of-custody.
- L'app gira come utente normale (`asInvoker`) ed **eleva i singoli tool on-demand**.

## ⚖️ Disclaimer legale

ForensicKit è un **launcher**: **non** ridistribuisce né include i binari dei tool di
terze parti. Ogni tool viene scaricato dalla sua fonte ufficiale ed è soggetto alla
**propria licenza/EULA** (NirSoft, voidtools, ShadowExplorer, ecc.): è responsabilità
dell'utente leggerle e rispettarle. I marchi citati appartengono ai rispettivi proprietari.

Il software è fornito "così com'è", **senza garanzie**. Gli strumenti forensi e di analisi
di sistema vanno usati **solo su sistemi di cui si è proprietari o per i quali si dispone di
autorizzazione esplicita**. Gli autori non sono responsabili di usi impropri.

## Licenza

Rilasciato sotto licenza **MIT** — vedi [LICENSE](LICENSE).
