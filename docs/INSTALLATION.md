# Installation und Build

## Spieler (sobald ein Release existiert)
1. Stardew Valley 1.6.14 oder neuer, SMAPI 4.x installieren.
2. Den Ordner `ProjectAbyss` in `Stardew Valley/Mods` entpacken.
3. Über SMAPI starten – statt des Titelbildschirms erscheint das Project-Abyss-Menü.
   „Normales Stardew Valley“ führt zum gewohnten Titelbildschirm; dort bringt „< Project Abyss“ zurück.
4. Koop: Host wählt „Mehrspieler → Lobby erstellen“; TCP-Port **24642** muss für Mitspieler erreichbar sein (LAN oder
   Portweiterleitung). Mitspieler geben die IP unter „Beitreten“ ein.

## Entwickler
Voraussetzungen: .NET SDK 6 oder neuer (getestet mit 8.0.132).
```bash
dotnet build ProjectAbyss.sln       # ohne Stardew möglich
dotnet test
```
Mod bauen (nur mit installiertem Spiel; Pathoschild.Stardew.ModBuildConfig findet den Spielordner automatisch oder
über `<GamePath>` in einer `stardewvalley.targets`):
```bash
dotnet build src/ProjectAbyss.Smapi -c Release   # kopiert die Mod nach Stardew Valley/Mods/ProjectAbyss
```
Assets neu erzeugen (Python 3 mit Pillow und NumPy, für Musik zusätzlich `oggenc`):
```bash
python3 tools/assetgen/generate_test_assets.py
python3 tools/assetgen/generate_audio.py
```
Konfiguration (`Mods/ProjectAbyss/config.json`, wird beim ersten Start erzeugt):
`ShowAbyssOnStartup` (Standard true), `DeveloperMode` (Standard false).
