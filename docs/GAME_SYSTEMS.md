# Spielsysteme (Implementierungsstand)

Alle Zahlenwerte stehen in `content/data/*.json` und sind ohne Codeänderung anpassbar.

## Kampf
- **Aktionen:** Grundangriff (Linksklick halten), Spezialangriff (Rechtsklick, Waffen-Cooldown), Fähigkeit 1/2 (Q/E),
  Ultimate (R, kostet Klassenressource), Dash (Leertaste), Heiltrank (F), Interagieren/Wiederbeleben (G).
- **Formen:** Kegel, Kreis (auch am Boden-Zielpunkt), Kapsel (Ansturm), Projektile (Anzahl, Streuung, Durchschlag).
- **Schaden:** Basis × Angreifer-Multiplikatoren → Krit (×1,5) → Rüstung (`a/(a+100)`, max. 75 %) bzw. Resistenz
  (−100 % … +75 %) → Schadenserhöhung (Schock, Stagger ×1,25) → Schild.
- **Status:** Blutung (3 Stapel), Gift (5), Brennen, Frost (Verlangsamung), Schock (+Schaden), Betäubung (unterbricht Windups).
- **Spieler-I-Frames:** Dash 0,14 s, nach Treffer 0,25 s, Respawn 2 s, Reconnect-Schutz 2 s.
- **Parade (Schwert):** Treffer im aktiven Fenster werden aufgehoben, Gegenstoß + Entschlossenheit.

## Krieger (Vertical Slice)
- Ressource **Entschlossenheit**: +3 je Treffer, +5 je erlittenem Treffer, +15 je Parade, verfällt nach 5 s ohne Kampf.
- Waffen: Schwert (Parade), Axt (Spaltender Hieb mit Blutung), Streitkolben (Erdstoß mit Betäubung/Stagger).
- Fähigkeiten: Wirbelsturm, Schildansturm (Betäubung); Ultimate Schlachtruf (Gruppenbuff +30 % Schaden, −20 % Schaden).

## Gegner und Boss (Biom 1)
- Moderghul (Nahkampf), Dornenschütze (Fernkampf, hält Abstand, braucht Sichtlinie), Fäulnisschamane (heilt
  Verbündete, flieht bei niedriger HP, Giftgeschoss), Dornenspross (nur beschworen).
- Wächter des Waldes: Phasen bei 100/70/35 %, Angriffe Schwung, Wurzelschlag (Bodenziel), Dornenring, Beschwörung,
  Ansturm; Koop-Angriff Dornensalve ab 2 Spielern; Pakt-Angriff „Grüner Zorn“ (großer Kreis, langer Windup).

## Arenen
- Vorlage + Seed → zufällige Hindernisse/Gefahren, gemischte Spawnpunkte → Validierung → bis zu 12 Versuche → Rückfall-Arena.
- Arenatypen implementiert: Wellen, Überleben, Elitejagd, Boss, Training (Entwickler). Fehlend: Verteidigung, Risikoarena.

## Run
- 25 Ebenen/5 Biome (Struktur in `RunPlan`), Bosse auf 5/10/15/20/25, Händler nach 4/9/14/19/24 (Händler-UI fehlt).
- Bossauswahl je Biom zu Runbeginn fest (aktuell nur ein Boss in Biom 1 vorhanden).
- Persönliche Belohnungen; Inventar 24 Plätze, Run-Truhe 16; Überlauf wird als „wartende Belohnung“ aufbewahrt.
- EP-Kurve: 19 Stufenaufstiege bis Stufe 20, je ein Skillpunkt, +1 je Boss-Meilenstein (Skillbaum fehlt noch).
