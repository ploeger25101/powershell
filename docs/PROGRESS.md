# Entwicklungsfortschritt

Legende: ✅ umgesetzt und automatisiert getestet · 🟡 umgesetzt, nur teilweise oder nicht im echten Spiel getestet ·
❌ fehlt.

## Phase 0 – Bestandsaufnahme und Architektur ✅
Umgebung geprüft, SMAPI 4.5.2 im Quelltext verifiziert, Architektur, Datenmodelle, Teststrategie, Git. → [FEASIBILITY.md](FEASIBILITY.md)

## Phase 1 – Technischer Vertical Slice
| Punkt | Stand |
|---|---|
| Hauptmenü (alle Einträge funktional) | 🟡 im Standalone-Host gerendert/bedient; im Spiel ungetestet |
| Eigener Spielmodus ohne Farm | 🟡 Architektur (Vollbild-Menü auf dem Titel), im Spiel ungetestet |
| Krieger, 3 Waffenarten, Grund-/Spezialangriff, 2 Fähigkeiten, Ultimate, Dash | ✅ |
| Acht Richtungsanimationen | ✅ (Testassets, 10 Animationen × 8 Richtungen) |
| Arena, 3 Gegnertypen, Boss mit 3 Phasen | ✅ |
| Leben, HP, Wiederbelebung | ✅ |
| Zweiter Spieler, hostautoritativ | ✅ (In-Memory-Netz, TCP); 🟡 nicht mit zwei echten Stardew-Clients |
| Speichern/Laden, Reconnect, Crash-Recovery | ✅ |
| Einfaches Inventar | 🟡 Datenmodell + Transaktionen; Oberfläche nur Zusammenfassung |

## Phase 2 – Multiplayer und Persistenz ✅ (bis auf Stardew-Transport, Entscheidung K1)
Hostautorität, Synchronisierung, persönliche Inventare, Reconnect, Crash-Recovery, Journal, Wiederbelebung, Pakt.

## Phase 3 – RPG-Kernsysteme 🟡/❌
Loot-Generator mit Seltenheit/Affixen/Sockeln ✅ · Gold/Materialien/Handel/Verkauf/Truhe als Transaktionen ✅ ·
Ausrüstung wirksam ❌ · Shops, Zerlegen, Schmiede, Runen, Flüche, Relikte ❌ · Skillbäume ❌ · Klassenressource Krieger ✅

## Phase 4 – Klassen ❌ (1/6 Klassen, 0/180 Skillknoten, 3/18 Waffenarten)
## Phase 5 – Welten und Gegner 🟡 (Biom 1: 1/8 Arena-Vorlagen, 3/6 Gegner + Beschwörung; Encounter Director ✅; Arena-Generator/Validator ✅)
## Phase 6 – Bosse und Balance 🟡 (1/10 Bosse; 4 Schwierigkeitsgrade in Daten und Freischaltung ✅; erste Messungen)
## Phase 7 – Grafik und Audio 🟡 (eigene Testassets; eigene Chiptune-Musik/SFX generiert; finale Kunst ❌)
## Phase 8 – Qualitätssicherung 🟡 (77 Tests, Last-/Absturz-/Netztests; Spieltests im echten Spiel ❌)
## Phase 9 – Release ❌

## Nächste logische Schritte
1. Entscheidung K1 einholen.
2. Mod auf einem Windows-Rechner mit Stardew 1.6.14+/SMAPI 4.5 bauen und die Annahmen aus KNOWN_LIMITATIONS prüfen.
3. Phase 3: Ausrüstungswerte → Kampf, Inventar-Oberfläche, Händler/Schmiede/Runen/Flüche, Skillbaum-Engine + Krieger-Knoten.
4. Phase 4/5: Magier als zweite Startklasse, restliche Biom-1-Inhalte, dann Biome 2–5.
