# RON1N Mode

Home Windows a schermo intero per avviare singoli giochi e app dal divano. Interfaccia WinUI 3 su .NET 8. Riutilizza l'installer, l'icona e il contratto di aggiornamento di Console Mode.

## Prima versione

- Libreria di eseguibili aggiunti a mano, cartella giochi configurabile e titoli Steam ed Epic installati.
- Ultimo gioco preselezionato, ritorno alla home con scorciatoia e ripresa della finestra esistente.
- Schede e immagini dal catalogo multipiattaforma LaunchBox. La ricerca per titolo mostra in anteprima copertina e sfondo prima di applicare l’abbinamento.
- Avvio a schermo intero all'accesso, impostazione dello schermo e accesso alle uscite audio di Windows.

## Build su Windows x64

Con .NET SDK 8 e Inno Setup 6, eseguire `./build/Publish-Ron1nMode.ps1` da PowerShell. Il workflow **Release** in modalità build genera gli stessi pacchetti. Per l'aggiornamento dal vecchio fork vedi la [guida di verifica e migrazione](docs/first-version.md).
