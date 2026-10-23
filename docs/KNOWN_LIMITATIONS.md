# Bekannte Einschränkungen und offene Risiken

## Nicht im echten Spiel geprüft
- Die SMAPI-Mod (`src/ProjectAbyss.Smapi`) wurde **nicht kompiliert und nicht gestartet**: In der Entwicklungsumgebung
  gibt es weder Windows noch Stardew Valley. Die Präsentationsschicht wurde gegen MonoGame 3.8.1.303 kompiliert und
  unter Xvfb gerendert; die Stardew-spezifischen Member aus [FEASIBILITY.md §4](FEASIBILITY.md) sind unverifiziert.
- Annahmen, die beim ersten Spielstart zu prüfen sind: Der Titelbildschirm zeichnet ein beliebiges
  `activeClickableMenu` im UI-Koordinatensystem; `Game1.getMouseX/Y()` liefern UI-Koordinaten; Stardews Schriften
  (fehlende Glyphen werden abgefangen); Audio-Registrierung über `Data/AudioChanges` mit absoluten Pfaden.
- Stardews Lautstärkeregler gelten zusätzlich zu den eigenen; eine Lautstärke pro Effekt wird über `Game1.playSound`
  nicht unterstützt. Musikwechsel nutzen `Game1.changeMusicTrack` – eine saubere Kreuzblende ist nicht garantiert.
- Die Simulation läuft mit einem Tick pro Spiel-Update. Fällt Stardew unter 60 Updates/s, läuft der Kampf langsamer.

## Offene Entscheidung
- **K1:** Koop über direkte TCP-Verbindung (umgesetzt) statt über Stardews Netzwerk; keine Steam/GOG-Einladungen.
  Die TCP-Verbindung ist unverschlüsselt. Siehe [REQUIREMENTS_ANALYSIS.md](REQUIREMENTS_ANALYSIS.md#k1).

## Funktional unvollständig (Vertical Slice)
- Klassen: nur Krieger. Skillbäume fehlen vollständig (Skillpunkte werden gesammelt, aber nicht verwendbar).
- Ausrüstung beeinflusst Kampfwerte noch nicht; Händler, Schmiede, Runen, Flüche, Relikte, Zerlegen, Skill-Reset fehlen
  (die Transaktionsgrundlagen für Verkauf/Handel/Truhe/Laden-Wiederbelebung existieren).
- Nur Biom 1 mit einer zufälligen Arena-Vorlage (+ Boss- und Rückfall-Arena). Ebenen 6–25 sind spielbar, verwenden
  aber mangels Inhalten die Rückfall-Arena und Biom-1-Gegner (mit steigendem Bedrohungsbudget); Bossebenen ab 10
  haben noch keinen Boss und werden als normale Arena gespielt.
- Keine Risiko- oder Verteidigungsarenen, keine Erfolge/Herausforderungen, keine Klassenfreischaltung.
- Inventar wird nur zusammengefasst angezeigt (keine Inventar-Oberfläche).
- Gegner haben nur Blickrichtung links/rechts (acht Richtungen sind laut Vorgabe für Spielfiguren verpflichtend).
- Kein Controller-Support.
- Grafik und Audio sind generierte **Testassets**.

## Technische Hinweise
- Snapshots übertragen den kompletten (quantisierten) Zustand, keine Delta-Kompression (≈1 KB bei 32 Gegnern, 20 Hz).
- Lokale Speicherstände und Profile sind nicht manipulationssicher (laut Vorgabe akzeptiert).
