# Entwicklerwerkzeuge

## Entwicklermodus (im Spiel)
Aktivieren über `config.json` der Mod: `"DeveloperMode": true`. Verwendet `dev-profile/` und `dev-runs/`; Entwicklerruns
werden als solche markiert und erzeugen keine Erfolge oder Freischaltungen.

Menü „Entwicklermodus“: Startebene (1–25, Bossebenen markiert), simulierte Mitspieler (0–3 Bots über echte
Client-Sitzungen auf einem In-Memory-Netz), Schwierigkeit, Seed, simulierte Latenz. Im Kampf:
F5 volle HP · F6 alle Gegner besiegen · F7 Reconnect eines Bots · F8 Host-Absturz (Wiederherstellung von der Platte) ·
F9 Gegnerwelle beschwören. Tick, Simulationszeit, Gegner-/Projektilzahl und Arena-Seed werden eingeblendet.

## Headless-Runner (`tools/ProjectAbyss.Headless`)
`arena <spieler> <seed>` · `boss <spieler> <seed>` · `load <gegner>` – Bot-Simulation ohne Grafik für Balancing und Last.

## Standalone-Host (`tools/ProjectAbyss.Standalone`)
Rendert die echte Präsentationsschicht mit MonoGame, ohne Stardew. Optionen: `--script`, `--out`, `--size`, `--data`,
`--dev`, `--uncapped`. Skriptschritte klicken Knöpfe über ihre Beschriftung, halten Tasten, setzen die Maus und
erzeugen Screenshots; am Ende werden FPS sowie Update-/Draw-Zeiten ausgegeben. Nur Entwicklungswerkzeug – das Spiel
wird über Stardew Valley + SMAPI gestartet.
