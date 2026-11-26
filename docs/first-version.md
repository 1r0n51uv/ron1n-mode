# Prima versione 1.7.1.6

## Verifica sul PC da gioco

1. Installare il setup x64 con l'opzione **Avvia con Windows**. Verificare apertura a schermo intero dopo il login e riapertura dopo **Desktop** con Ctrl+Alt+F11.
2. Provare l'aggiunta di un exe diretto, due ROM gestite dallo stesso emulatore con argomenti diversi e la scansione di un disco disconnesso. Controllare il candidato in cartelle che contengono launcher e utility. Ripetere la scansione con Steam ed Epic installati, poi senza uno dei client.
3. Avviare un gioco per tipo. Tornare alla home con scorciatoia, riprendere la finestra senza avviare una seconda istanza e chiudere il gioco. Verificare che la home recuperi selezione e focus. Provare mouse e tastiera, Xbox, DualSense e Switch Pro, anche via Bluetooth. Il cambio ROM con un emulatore persistente può richiedere un'integrazione dedicata.
4. Verificare l'abbinamento automatico, la ricerca con un titolo alternativo, la copia di immagini locali e il funzionamento offline. Lo Steam Store risponde senza credenziali. La ricerca e i dettagli provengono da endpoint dello store senza un contratto API pubblico. Per titoli fuori catalogo usare artwork locali.
5. Controllare la leggibilità a distanza in 1080p e 4K, con DPI Windows diversi e movimento disattivato. Verificare il cambio schermo della home e il collegamento alle impostazioni audio Windows.

## Continuità OTA

1. Eseguire **Release** senza pubblicazione per ottenere `ConsoleMode-Setup-x64.exe` e `ConsoleMode-Portable-x64.exe`.
2. Per raggiungere i client esistenti, caricare questi stessi file nella release ponte nella vecchia repo `1r0n51uv/consolemode`. Versione superiore a quella installata, nomi originali e digest SHA-256 da GitHub. L'AppId di Inno Setup rimane quello esistente.
3. Una volta installata, la nuova app cerca gli OTA in `1r0n51uv/ron1n-mode`. Se le release sono private, serve un modo per permettere al client di leggerle.
4. Verificare due upgrade in successione partendo dal fork, sia setup sia portatile. Controllare l'installazione esistente, avvio automatico, configurazioni delle app manuali, scorciatoia del controller e immagini. Le impostazioni sono lette dal vecchio file e salvate separatamente in `ron1n.json`.
