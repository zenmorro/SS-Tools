# Contribuire a ForensicKit

Grazie per l'interesse! Il modo più semplice per contribuire è **aggiungere un tool al
catalogo** tramite una Pull Request su [`manifests/tools.json`](../manifests/tools.json).
Non serve ricompilare l'app: il catalogo è dati, non codice.

## Aggiungere un tool al manifest

1. Fai il fork del repo e modifica `manifests/tools.json`.
2. Aggiungi una voce all'array `tools` seguendo lo schema qui sotto.
3. Verifica che il JSON sia valido e apri una PR descrivendo il tool e la sua licenza/EULA.

### Schema di una voce

| Campo | Obblig. | Descrizione |
|---|:---:|---|
| `id` | ✅ | Identificatore univoco, minuscolo, senza spazi (es. `everything`). Diventa il nome della cartella in `Tools\`. |
| `name` | ✅ | Nome visualizzato. |
| `author` | | Autore/vendor. |
| `category` | | Categoria per la sidebar (es. `Ricerca file`, `Journal NTFS`). |
| `description` | | Breve descrizione mostrata nella card. |
| `icon` | | Nome file icona (opzionale). |
| `sourceType` | ✅ | `direct_url` oppure `github_release`. |
| `downloadUrl` | ▲ | **Solo** per `direct_url`: link diretto all'archivio/exe. |
| `owner` / `repo` | ▲ | **Solo** per `github_release`: owner e repository GitHub. |
| `assetPattern` | | **Solo** `github_release`: regex per scegliere l'asset (es. `(?i)win.*x64.*\\.zip$`). |
| `sha256` | | Hash SHA-256 atteso dell'archivio, se disponibile: se presente viene **verificato**. |
| `packageType` | ✅ | `zip` (estratto) oppure `exe` (usato così com'è). |
| `executable` | ▲ | Eseguibile da lanciare (obbligatorio per `zip`). |
| `requiresElevation` | | `true` se il tool va avviato con UAC. |
| `args` | | Argomenti predefiniti (l'utente può modificarli prima del lancio). |
| `homepage` | | Pagina ufficiale del tool. |
| `version` | | Versione dichiarata; per i `direct_url` serve al controllo aggiornamenti. |

▲ = condizionale (dipende da `sourceType` / `packageType`).

### Esempio `direct_url` (pagina di download statica)

```json
{
  "id": "winprefetchview",
  "name": "WinPrefetchView",
  "author": "NirSoft",
  "category": "Sistema/Registro",
  "description": "Legge i file Prefetch di Windows.",
  "sourceType": "direct_url",
  "downloadUrl": "https://www.nirsoft.net/utils/winprefetchview-x64.zip",
  "packageType": "zip",
  "executable": "WinPrefetchView.exe",
  "requiresElevation": true,
  "homepage": "https://www.nirsoft.net/utils/win_prefetch_view.html",
  "version": "1.37"
}
```

### Esempio `github_release` (ultima release automatica)

```json
{
  "id": "journaltrace",
  "name": "JournalTrace",
  "category": "Journal NTFS",
  "sourceType": "github_release",
  "owner": "orlikoski",
  "repo": "JournalTrace",
  "assetPattern": "(?i).*\\.zip$",
  "packageType": "zip",
  "executable": "JournalTrace.exe",
  "requiresElevation": true,
  "homepage": "https://github.com/orlikoski/JournalTrace"
}
```

## Regole per i tool

- Aggiungi **solo tool leciti** e scaricati dalla **fonte ufficiale** del vendor.
- Non aggirare paywall, licenze o meccanismi di distribuzione.
- Indica nella PR la **licenza/EULA** del tool. ForensicKit non ridistribuisce i binari.
- Preferisci `github_release` quando possibile: i link diretti si "rompono" ad ogni versione.
- Se il vendor pubblica un hash, inseriscilo in `sha256` per abilitare la verifica d'integrità.

## Contribuire al codice

- La logica va in `ForensicKit.Core` (con interfacce + test), la UI in `ForensicKit.App`.
- Aggiungi/aggiorna gli unit test in `tests/ForensicKit.Core.Tests`.
- Prima della PR:
  ```powershell
  dotnet build ForensicKit.slnx -c Release
  dotnet test tests/ForensicKit.Core.Tests/ForensicKit.Core.Tests.csproj
  ```
- Nuovi script PowerShell: aggiungili in `src/ForensicKit.Scripts/` e registrali in
  `EmbeddedResources.GetScriptCatalog()`. Gli script devono essere **sola lettura**
  e non modificare il sistema.
