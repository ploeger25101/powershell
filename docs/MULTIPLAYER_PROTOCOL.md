# Multiplayer-Protokoll (Version 1)

Binär, little-endian, ein Byte Nachrichtentyp. Jede Abweichung (Länge, Zahlenbereich, NaN, unbekannter Typ, falsche
Phase) führt zur Trennung **nur dieses** Absenders (`ProtocolException`).

## Handshake
1. Client → `Hello` (Protokollversion, Spielversion, Content-Hash, Asset-Hash, öffentlicher Schlüssel, Name, Freischaltungen).
2. Host prüft Versionen/Hashes → `Challenge` (32 Byte Zufall) oder `Reject(Grund)`.
3. Client → `ChallengeResponse` (ECDSA-Signatur über die Nonce).
4. Host verifiziert, Teilnehmer-ID = `pa-` + SHA-256(öffentlicher Schlüssel)[0..32].
   - Lobby offen: Aufnahme (max. 4), `Welcome`, `LobbyState`.
   - Run läuft: nur ursprüngliche Teilnehmer (`Welcome(reconnect)`), sonst `Reject`.
   - Eine ältere Verbindung derselben Identität wird ersetzt.

## Laufender Kampf
| Richtung | Nachricht | Kanal | Inhalt |
|---|---|---|---|
| C→H | `InputBatch` | unzuverlässig, 60 Hz | letzte 4 Eingaben: Sequenz, Bewegung (2×int8), Ziel (uint16-Winkel), gehaltene/gedrückte Tasten |
| H→C | `Snapshot` | unzuverlässig, 20 Hz | Tick, bestätigte Eingabe-Sequenz, quantisierte Zustände (Spieler, Gegner, Projektile, Boss, Encounter), Ereignisse |
| H→C | `FullState` | zuverlässig | gzip-JSON: kompletter `WorldState` + `RunState` (Beitritt, Arenastart, Reconnect) |
| C→H | `FullStateAck` | zuverlässig | Client hat den Zustand übernommen → Steuerung frei, ggf. Reconnect-Schutz |
| C→H | `ActionRequest` | zuverlässig | Request-ID + Aktion (Klasse, Bereit, Pakt-Stimme, Verkaufen, Handeln, Truhe …) |
| H→C | `ActionResult` | zuverlässig | Ergebnis; doppelte Request-IDs werden aus dem Cache beantwortet (idempotent) |
| H→C | `RunNotice` | zuverlässig | Phasenwechsel, aktualisierter Run, ausstehende Pakt-Stimmen |

- **Clients senden nie Schaden, Gold oder Gegenstände.** Handels-/Verkaufsanfragen werden als Transaktion mit der ID
  `req:{run}:{teilnehmer}:{requestId}` verbucht und sind dadurch auch über Host-Neustarts hinweg idempotent.
- **Vorhersage:** Der Client sagt nur die eigene Bewegung (inkl. Dash-Start) voraus und spielt nach jedem Snapshot die
  unbestätigten Eingaben neu ab. Gemessen bei 4 Ticks Latenz, 3 Ticks Jitter, 10 % Verlust, 5 % Duplikaten:
  Korrektur Median 0,03 px, p95 0,12 px, Maximum 5,4 px.
- **Kompaktheit:** Inhalts-IDs werden als 16-Bit-Index übertragen (gültig, weil der Content-Hash übereinstimmen muss).

## Trennung und Reconnect
- Erkennung: Transport-Ereignis oder 6 s ohne Paket. Die Figur bleibt im Spiel und ist verwundbar.
- Reconnect: Identitätsprüfung → `FullState` → `FullStateAck` → 2 s Schutz (kein Schaden, keine Angriffe/Fähigkeiten,
  keine Interaktionen/Wiederbelebungen). HP, Leben, Ressourcen und Cooldowns bleiben unverändert.
- Eingabesequenz wird nach dem Reconnect beim letzten vom Host verarbeiteten Wert fortgesetzt.
