# Architektur

## Schichten

```
┌──────────────────────────── Stardew Valley + SMAPI (Prozess) ────────────────────────────┐
│  ProjectAbyss.Smapi  (ModEntry, AbyssShellMenu, SmapiPlatform, AudioRegistry)             │
│        │ implementiert IAbyssPlatform (Texturen, Fonts, Eingabe, Audio, Datenordner)      │
│        ▼                                                                                   │
│  ProjectAbyss.Client (MonoGame-API): AbyssApp → Screens, WorldRenderer, Hud, Ui, Sessions │
│        │ liest WorldState/RunState, sendet Eingaben                                       │
│        ▼                                                                                   │
│  ProjectAbyss.Core (keine Engine-Abhängigkeit, net6.0)                                     │
│     Simulation · AI · Arena · Combat · Run · Items · Persistence · Net · Profile · Audio  │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

- **Core** enthält alle Regeln. Er kennt weder Stardew noch MonoGame und läuft identisch in Tests, im Headless-Runner,
  im Standalone-Host und im Spiel.
- **Client** ist reine Darstellung und Bedienung. Er verändert den Spielzustand nie direkt, sondern über Sitzungen
  (`IGameSession`) – lokal als Host oder entfernt als Client.
- **Smapi** ist eine dünne Brücke. Dieselben Client-Quelltexte werden einmal gegen MonoGame aus NuGet (Prüf-Build,
  Standalone-Host) und einmal gegen die MonoGame-DLL des Spiels (Mod-Build) kompiliert. Es werden keine
  MonoGame-Binärdateien mit der Mod ausgeliefert.

## Simulation

- Fester Tick (60 Hz, `SimTime`). Aktionen haben Windup/Active/Recovery in Ticks; Animationen visualisieren nur.
- `WorldState` ist vollständig serialisierbar (inkl. RNG-Zustand). `CombatWorld` hält nur abgeleitete Caches
  (Flow-Field, Angriffsregister). Snapshot → Wiederherstellung → identischer Verlauf ist getestet.
- Zufall ausschließlich über `DeterministicRng` (PCG32) mit speicherbarem Zustand.
- KI: Zustandsautomat je Gegner mit Rollenlogik und Reaktionsverzögerung; gemeinsames BFS-Flow-Field zu allen Spielern
  (Kosten unabhängig von Gegnerzahl), lokales Ausweichen an Hindernisecken.

## Run und Persistenz

- `RunController` (nur Host) übersetzt Simulationsereignisse in Transaktionen (`RunLedger`: validieren → Journal mit
  fsync → anwenden). Nur bestätigte Transaktionen werden angezeigt/übertragen.
- Checkpoints an sicheren Übergängen, Crash-Snapshots alle 3 s und nach kritischen Ereignissen.
  Details: [SAVE_FORMAT.md](SAVE_FORMAT.md).

## Netzwerk

- `HostSession` (autoritativ) und `ClientSession` über das Interface `INetTransport`. Implementierungen: In-Memory
  (Tests, mit Latenz/Verlust/Duplikaten), TCP (Direktverbindung), Null (Einzelspieler). Ein Stardew-Transport ist
  vorbereitet (gleiche Schnittstelle), hängt aber an Entscheidung K1. Details: [MULTIPLAYER_PROTOCOL.md](MULTIPLAYER_PROTOCOL.md).

## Datengetriebene Inhalte

Alle Werte in `content/data/*.json`, beim Laden validiert (`GameDataValidator`); ein SHA-256 über alle Datendateien
(`ContentHash`) muss im Multiplayer bei allen Teilnehmern übereinstimmen.
