# Prima versione RON1N Mode

## Verifica sul PC da gioco

1. Installare il setup x64 con l'opzione **Avvia con Windows**. Verificare apertura a schermo intero dopo il login e riapertura dopo **Desktop** con Ctrl+Alt+F11.
2. Provare l'aggiunta di un exe diretto, due ROM gestite dallo stesso emulatore con argomenti diversi e la scansione di un disco disconnesso. Controllare il candidato in cartelle che contengono launcher e utility. Ripetere la scansione con Steam ed Epic installati, poi senza uno dei client.
3. Avviare un gioco per tipo. Tornare alla home con scorciatoia, riprendere la finestra senza avviare una seconda istanza e chiudere il gioco. Verificare che la home recuperi selezione e focus. Provare mouse e tastiera, Xbox, DualSense e Switch Pro, anche via Bluetooth. Il cambio ROM con un emulatore persistente può richiedere un'integrazione dedicata.
4. Verificare l'abbinamento automatico, la ricerca con un titolo alternativo, la copia di immagini locali e il funzionamento offline. Lo Steam Store risponde senza credenziali. La ricerca e i dettagli provengono da endpoint dello store senza un contratto API pubblico. Pokémon Pokopia è il caso di regressione fuori Steam. Provare sia `Pokemon Pokopia` sia `Pokémon Pokopia`: Wikipedia deve trovare il titolo e fornire la copertina se disponibile. Verificare che lo sfondo ufficiale venga aggiunto anche a una scheda già associata a Wikipedia. Le immagini impostate a mano hanno la precedenza sugli aggiornamenti automatici.
5. Controllare la leggibilità a distanza in 1080p e 4K, con DPI Windows diversi e movimento disattivato. Verificare il cambio schermo della home e il collegamento alle impostazioni audio Windows.

Per ripetere il test di integrazione del catalogo: `dotnet run --project tests/CatalogSmoke`. Richiede accesso ai cataloghi pubblici e verifica i due nomi e il download di copertina e sfondo.

## Versioni e continuità OTA

La numerazione propria di RON1N Mode parte da `0.1.0`. La `1.7.1.6` resta disponibile come build iniziale di compatibilità. Da `0.1.0` l’updater esclude quel tag storico e continua a ordinare le release della nuova serie per versione, rispettando il canale stabile o beta.

Chi ha già installato `1.7.1.6` può passare una volta sola al nuovo numero eseguendo il setup di `0.1.0` quando sarà disponibile. Il setup conserva l’AppId e il percorso dati. Il vecchio updater confronta direttamente i numeri di versione e considera `0.1.0` inferiore. Per il portatile, sostituire l’eseguibile conservando la cartella `ConsoleMode_Data`.

Per verificare il nuovo flusso OTA, installare `0.1.0` e aggiornare a una versione successiva della stessa serie. Controllare selezione, immagini e impostazioni dopo il riavvio. Se le release sono private, il client deve avere accesso agli asset.
