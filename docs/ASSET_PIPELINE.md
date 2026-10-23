# Asset-Pipeline

## Grafik
- `tools/assetgen/generate_test_assets.py` zeichnet **eigene Test-Pixelart** (Platzhalter für Phase 7) nach
  `src/ProjectAbyss.Smapi/assets/` und schreibt `assets/sprites.json`.
- Spieler: 48×64-Frames, Zeile = Animation, Spalte = Richtung × 6 + Frame, Richtungen in der Reihenfolge
  Oben, Oben rechts, Rechts, Unten rechts, Unten, Unten links, Links, Oben links. Animationen: idle, walk, attack,
  special, dash, ability, ultimate, hit, downed, revive (alle 8 Richtungen).
- Waffen sind eine **eigene Ebene** (32×32, nach rechts zeigend, Griffpunkt in den Metadaten) und werden auf den
  exakten Zielwinkel gedreht; Schwung je Aktionsphase.
- Gegner 24–32 px, Boss 96 px; Kacheln 16×16 (Boden ×4, Wand, Hindernis, Gefahr mit farbunabhängigem Punktrahmen, Ausgang);
  Fähigkeits-Icons 48×48.
- Finale Kunst ersetzt die PNGs bei gleichem `sprites.json`-Format; die Darstellung skaliert ganzzahlig (PointClamp).

## Audio
- `tools/assetgen/generate_audio.py` komponiert **eigene düstere Chiptune-Musik** (Puls-/Dreieckswellen, Arpeggien,
  Rauschpercussion, Moll-Progressionen) und Retro-Soundeffekte, deterministisch.
- Soundeffekte als WAV (`assets/audio/sfx`), Musik als OGG-Loops (`assets/audio/music`, via `oggenc`).
- Dateiname = Cue-ID. Die Mod registriert alle Dateien über `Data/AudioChanges` (Stardew 1.6).
- Bossmusik: drei Phasen + Pakt-Variante je Boss (`bosses.json` → `phases[].music`).

## Fonts
Im Spiel werden Stardews Schriften verwendet. Für den Standalone-Host erzeugt `generate_dev_font.py` Bitmap-Fonts aus DejaVu Sans.
