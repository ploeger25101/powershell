# Project Abyss

Ein düsteres Action-Roguelite mit RPG-Elementen, das über **Stardew Valley + SMAPI** gestartet wird, sich aber wie ein
eigenständiges Spiel verhält: eigenes Hauptmenü, eigener Spielmodus ohne Farm, eigener hostautoritativer Koop,
eigene Speicherstände, eigene Grafik und eigene Chiptune-Musik.

> **Entwicklungsstand: technische Machbarkeitsphase (Phase 0–2 weitgehend, Phase 1 Vertical Slice spielbar im
> Entwicklungs-Host).** Der genaue, ehrliche Stand steht in [docs/PROGRESS.md](docs/PROGRESS.md); was fehlt oder nicht
> getestet werden konnte, in [docs/KNOWN_LIMITATIONS.md](docs/KNOWN_LIMITATIONS.md).

## Was heute funktioniert (getestet)

- Engine-unabhängige Kampfsimulation mit festem 60-Hz-Tick: Krieger mit Schwert/Axt/Streitkolben, Grund- und
  Spezialangriff (inkl. Parade), zwei aktive Fähigkeiten, Ultimate „Schlachtruf“, Dash mit 2 Ladungen und I-Frames,
  Statuseffekte, Rüstung/Resistenzen/Krits, Schilde.
- Acht-Richtungs-Animationen für die Darstellung; Trefferprüfung immer mit dem exakten Zielwinkel.
- Drei Gegnertypen mit Rollen-KI (Nahkämpfer, Fernkämpfer, Unterstützer) + Beschwörung, Boss „Wächter des Waldes“
  mit drei Phasen, Stagger-Leiste mit Immunitätsfenster (kein Stunlock), Koop- und Pakt-Mechanik.
- Seed-basierte Arena-Generierung mit Validierung (20 000 generierte Arenen im Test) und Rückfall-Arena.
- Leben, Kampfunfähigkeit, Wiederbelebung, Respawn, Ausscheiden, Pakt der letzten Chance (einstimmig, einmalig,
  absturzsicher).
- Persistenz: atomare, prüfsummengesicherte Saves, Transaktionsjournal, Crash-Snapshots, Wiederherstellung ohne
  doppelte Belohnungen (Stresstest mit Hunderten simulierten Host-Abstürzen).
- Multiplayer-Protokoll: Handshake mit Signatur-Challenge, Versions-/Inhalts-/Asset-Prüfung, Lobby, hostautoritativer
  Kampf, Client-Vorhersage, Reconnect mitten im Kampf mit 2 s Schutz, Timeout-Erkennung, TCP-Transport.
- Präsentation: Hauptmenü, Neuer Run, Fortsetzen, Mehrspieler/Lobby, Kampf-HUD, Zwischen-Arena-, Pakt- und
  Run-Ende-Bildschirme, Einstellungen (Tastenbelegung, Barrierefreiheit, Datenexport/-löschung), Klassen, Bestiarium,
  Statistik, Entwicklermodus — automatisiert gerendert und per Screenshot geprüft (MonoGame unter Xvfb).

## Was ausdrücklich noch fehlt

Fünf weitere Klassen, Skillbäume (0 von 180 Knoten), 15 weitere Waffenarten, Biome 2–5, 9 weitere Bosse, Händler/
Schmiede/Runen/Flüche, Ausrüstungswerte im Kampf, Risiko- und Verteidigungsarenen, Erfolge/Herausforderungen,
finale Pixelart. Die SMAPI-Mod selbst wurde **nicht** im echten Spiel gebaut oder gestartet (keine Stardew-Installation
in der Entwicklungsumgebung). Details: [docs/PROGRESS.md](docs/PROGRESS.md).

## Projektstruktur

| Pfad | Inhalt |
|---|---|
| `src/ProjectAbyss.Core` | Spiellogik ohne Engine-Bezug (net6.0): Simulation, KI, Arena, Run, Persistenz, Netzwerk |
| `src/ProjectAbyss.Client` | Darstellung, HUD, Menüs, Sitzungen — nur gegen die MonoGame-API geschrieben |
| `src/ProjectAbyss.Smapi` | SMAPI-Mod: Einstieg, Titelbildschirm-Ersatz, Plattformbrücke, Assets |
| `content/data` | Datengetriebene Inhalte (JSON): Balancing, Klassen, Waffen, Fähigkeiten, Gegner, Bosse, Arenen, Items |
| `tests/ProjectAbyss.Core.Tests` | xUnit-Tests (77) |
| `tools/ProjectAbyss.Headless` | Headless-Simulation für Balancing und Lasttests |
| `tools/ProjectAbyss.Standalone` | Entwicklungs-Host (MonoGame) für UI-Smoke-Tests/Screenshots ohne Stardew |
| `tools/assetgen` | Generatoren für Test-Pixelart, Chiptune-Audio und Entwicklungs-Fonts |
| `docs/` | Architektur, Protokoll, Speicherformat, Tests, Balancing, Einschränkungen, Fortschritt |

## Schnellstart (Entwicklung)

```bash
dotnet build ProjectAbyss.sln          # Core, Client, Tests, Tools (ohne Stardew-Installation)
dotnet test                            # 77 Tests
dotnet run --project tools/ProjectAbyss.Headless -- boss 2 1   # Bosskampf mit 2 Bots simulieren
```

Mod bauen und installieren (Windows, mit Stardew Valley 1.6.14+ und SMAPI 4.x): siehe
[docs/INSTALLATION.md](docs/INSTALLATION.md).

## Dokumentation

[Machbarkeit & Umgebung](docs/FEASIBILITY.md) · [Architektur](docs/ARCHITECTURE.md) ·
[Anforderungsanalyse](docs/REQUIREMENTS_ANALYSIS.md) · [Spielsysteme](docs/GAME_SYSTEMS.md) ·
[Multiplayer-Protokoll](docs/MULTIPLAYER_PROTOCOL.md) · [Speicherformat](docs/SAVE_FORMAT.md) ·
[Tests](docs/TESTING.md) · [Entwicklerwerkzeuge](docs/DEVELOPER_TOOLS.md) · [Asset-Pipeline](docs/ASSET_PIPELINE.md) ·
[Balancing](docs/BALANCING.md) · [Datenschutz](docs/PRIVACY.md) · [Bekannte Einschränkungen](docs/KNOWN_LIMITATIONS.md) ·
[Fortschritt](docs/PROGRESS.md) · [Changelog](CHANGELOG.md)
