# RON1N Mode

Home Windows a schermo intero per avviare singoli giochi e app dal divano. Interfaccia WinUI 3 su .NET 8. Riutilizza l'installer, l'icona e il contratto di aggiornamento di Console Mode.

## Prima versione

- Libreria di eseguibili aggiunti a mano, cartella giochi configurabile e titoli Steam ed Epic installati.
- Ultimo gioco preselezionato, ritorno alla home con scorciatoia e ripresa della finestra esistente.
- Schede da Steam e Wikipedia, ricerca con titolo alternativo e immagini locali. Gli sfondi dipendono dalla fonte.
- Avvio a schermo intero all'accesso, impostazione dello schermo e accesso alle uscite audio di Windows.

## Build su Windows x64

Con .NET SDK 8 e Inno Setup 6, eseguire `./build/Publish-Ron1nMode.ps1` da PowerShell. Il workflow **Release** in modalità build genera gli stessi pacchetti. Per l'aggiornamento dal vecchio fork vedi la [guida di verifica e migrazione](docs/first-version.md).
