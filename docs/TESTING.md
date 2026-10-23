# Tests

## Ausführen
```bash
dotnet test                                                    # 77 automatisierte Tests
dotnet run --project tools/ProjectAbyss.Headless -- load 32    # Lasttest (4 Spieler, 32 Gegner)
dotnet run --project tools/ProjectAbyss.Headless -- boss 2 1   # Bosskampf: Spieler, Seed
cd tools/ProjectAbyss.Standalone && xvfb-run -a dotnet run -- --script scripts/smoke.json --out shots
cd tools/ProjectAbyss.Standalone && xvfb-run -a dotnet run -- --dev --script scripts/dev_boss.json --out shots
```

## Abdeckung (Stand dieses Commits)
| Bereich | Tests |
|---|---|
| Daten | Laden/Validierung, Querverweise, Content-Hash, Upgrade-Kosten (5 230 Gold) |
| Mathematik | 8 Richtungen, exakte Kegeltreffer, RNG-Reproduzierbarkeit und -Gleichverteilung, Winkelquantisierung |
| Arena | **20 000 generierte Arenen** gültig, Determinismus, Ablehnung von Gefahren am Start/unerreichbarem Ausgang, Rückfall-Arena, kein Durchtunneln |
| Kampf | Dash-Ladungen/Distanz/I-Frames, Treffer nur in Zielrichtung, Schadensformel, Parade, abnehmende Betäubung, Solo-Tod→Respawn→Ausscheiden, Wiederbelebung ohne Lebensverlust, Bossphasen, Boss-HP-Skalierung, Stagger-Immunität, Reconnect-Schutz, verwundbar während Trennung, Snapshot→Wiederherstellung identisch, zwei Bots säubern Arena |
| Persistenz | Hülle/Prüfsumme/Manipulation, Backup-Rückfall, neuere Version abgelehnt, Journal-Reparatur, Idempotenz, Handel ohne Duplikat, volles Inventar, Laden-Wiederbelebung, EP-Kurve (19 Aufstiege), Journal-Nachspielen + Abgleich |
| Absturz-Stresstest | 3 Seeds, je ~140–300 simulierte Host-Abstürze (30 % mitten im Journal-Schreiben); Invarianten: Gold = Summe bestätigter Transaktionen, Leben = 3 − bestätigte Verluste, alle bestätigten IDs angewendet, Item-IDs eindeutig |
| Pakt | Einstimmigkeit, Pause bei fehlender Stimme über Host-Neustart, Fremde abgelehnt, 1 Leben, Gold 0, volle Boss-HP, Pakt-Angriff, kein zweiter Pakt, Laden setzt nicht zurück, Nein beendet Run |
| Profil | Sofortiges Speichern, Einmaligkeit, Entwicklerprofil ohne Freischaltungen, Export/Löschen, unlesbares Profil wird nicht überschrieben, Signaturen |
| Netzwerk | Lobby-Beitritt, Versions-/Asset-Ablehnung, max. 4, kein Beitritt in laufenden Run, Koop über verlustbehaftetes Netz, Vorhersage-Konvergenz, Reconnect mitten im Kampf, Identitätsdiebstahl abgelehnt, Timeout, defekte Pakete, Host-Absturz + Neustart + Rejoin, idempotente Aktionen, echte TCP-Sockets |
| Audio | Bossmusik aus Zustand (Reconnect), Rate-Limit mit Vorrang für Warnungen, jede angeforderte Cue-ID existiert als Datei |

## Nicht automatisiert / nicht durchgeführt
- Integrationstests **im echten Stardew Valley mit SMAPI** (keine Installation verfügbar).
- Mehrere echte Stardew-Clients, Windows-spezifische Tests, Controller.
- Rendering-Performance auf echter GPU (gemessen nur mit Software-Renderer llvmpipe).
