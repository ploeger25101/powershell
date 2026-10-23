# Balancing (Ausgangswerte und Messungen)

Zentrale Datei: `content/data/balance.json` (Dash, Leben, Reconnect, Kampf, Skalierung, Schwierigkeitsgrade, Tränke,
Progression, Wirtschaft). Gegner-, Boss- und Waffenwerte in den jeweiligen JSON-Dateien.

| Wert | Ausgangswert |
|---|---|
| Dash | 2 Ladungen, 3 s Aufladung, 0,22 s Bewegung (64 px), 0,14 s I-Frames |
| Leben | 3; Wiederbeleben 12 s Fenster, 2,5 s Kanalisieren, 30 % HP; Respawn 50 % HP |
| Boss-HP-Skalierung | 100 / 175 / 250 / 325 % |
| Gegner (Budget / HP / Schaden) | ×1,0/1,5/2,0/2,5 · ×1,0/1,1/1,2/1,3 · ×1,0/1,05/1,1/1,15 |
| Schwierigkeit HP/Schaden/Budget/Elite | Normal 1/1/1/5 % · Schwer 1,35/1,25/1,2/10 % · Albtraum 1,8/1,5/1,4/16 % · Höllisch 2,4/1,85/1,6/22 % |
| Aufwertung +1…+10 | 100, 150, 220, 300, 380, 480, 600, 750, 950, 1 300 = **5 230 Gold** |
| EP-Kurve | 100·1,2^(Stufe−1), gerundet; 19 Aufstiege |

## Messungen (Headless, Bot-Spieler ohne Ausrüstung/Skills, Seeds 1–3)
| Szenario | Ergebnis |
|---|---|
| Ebene-1-Arena, 1 Spieler | 14–24 s |
| Wächter des Waldes (4 400 HP), 1 Spieler Stufe 5 | 150–165 s; in 2 von 3 Seeds verliert der Bot alle Leben |
| … 2 Spieler | 121–146 s |
| … 4 Spieler | 110–115 s |
| Lasttest 4 Spieler + 32 Gegner | Ø 0,11 ms, p99 0,24 ms pro Tick |
| Lasttest 4 Spieler + 64 Gegner | Ø 0,25 ms, p99 0,51 ms pro Tick |

Die Bosszielzeit (Ebene 5: 2–3 min) wird solo erreicht; mit mehr Spielern sinkt die Zeit leicht – die
Skalierung muss mit echten Spielern nachjustiert werden. Die Goldziele (12 000–15 000 pro Run) können erst mit allen
25 Ebenen gemessen werden.
