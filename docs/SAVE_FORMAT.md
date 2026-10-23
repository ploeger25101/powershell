# Speicherformat

Ablage: `%AppData%/StardewValley/ProjectAbyss/` (Linux/macOS: entsprechender `StardewValley`-Datenordner).
Stardews `Saves/`-Ordner wird nie gelesen oder geschrieben.

```
ProjectAbyss/
  profile/profile.json        dauerhaftes Profil (Freischaltungen, Statistik, Einstellungen)
  profile/identity.key        privater ECDSA-Schlüssel (PKCS#8) der Teilnehmeridentität
  runs/<runId>/run.json       Checkpoint an sicheren Übergängen
  runs/<runId>/recovery.json  Crash-Snapshot während des Kampfes (Run + Arena)
  runs/<runId>/journal.log    Transaktionsjournal (JSON-Zeilen)
  dev-profile/, dev-runs/     Entwicklermodus (strikt getrennt, keine Freischaltungen)
```

## Hülle (jede JSON-Datei außer dem Journal)
```json
{ "format": "ProjectAbyss", "kind": "run|recovery|profile", "schemaVersion": 1, "gameVersion": "0.1.0",
  "sequence": 12, "savedUtc": "…", "payloadSha256": "…", "payload": "<JSON als Zeichenkette>" }
```
- Schreiben: temporäre Datei → `Flush(true)` → `File.Replace` mit `.bak` der Vorversion.
- Laden: Format/Art/Version/Prüfsumme prüfen; bei Beschädigung `.bak`; neuere Schemaversion wird **abgelehnt**, nie falsch gelesen.
- Ein unlesbares Profil wird nicht überschrieben, sondern als `profile.json.unreadable-<Zeit>` beiseitegelegt.

## Journal
Eine Zeile je Transaktion: `{"seq":n,"sha":"SHA256(seq|tx)","tx":{…}}`, mit `FileOptions.WriteThrough` und fsync
geschrieben. Eine beim Absturz abgeschnittene letzte Zeile wird erkannt, verworfen (Transaktion galt nie als bestätigt)
und die Datei repariert, bevor weiter angehängt wird.

## Transaktionen und Idempotenz
Jede Transaktion hat eine deterministische ID, z. B. `kill:{run}:{versuch}:{entity}:{teilnehmer}`,
`life:{run}:{teilnehmer}:{verbleibendeLeben}`, `pact:{run}`, `req:{run}:{teilnehmer}:{requestId}`.
`RunState.AppliedTransactions` macht jede Anwendung idempotent.

## Wiederherstellung
1. Neuesten gültigen Zustand wählen (`recovery.json`, wenn er mindestens so aktuell ist wie `run.json`).
2. Journal-Einträge mit höherer Sequenz idempotent nachspielen.
3. Arena abgleichen: belohnte Kills bleiben tot, verlorene Leben bleiben verloren (Spieler wird in „Rückkehr“ versetzt).
4. Kampf fortsetzen; Clients verbinden sich neu.

Kritische Transaktionen (Lebensverlust, Pakt, Handel, Verkauf, Wiederbelebung) lösen sofort einen neuen Snapshot aus.
Hinweis: Lokale Dateien sind nicht manipulationssicher; Ziel ist Robustheit, kein Anti-Cheat.
