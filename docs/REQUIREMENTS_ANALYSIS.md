# Anforderungsanalyse – Widersprüche, Lücken, getroffene Entscheidungen

Legende: **E** = eigenständig getroffene, risikoarme Entscheidung · **K** = grundlegender Konflikt, Zustimmung nötig.

## K1 – Koop über Stardew-Netzwerk vs. „keine sichtbare Farm“ <a id="k1"></a>

**Befund (SMAPI-Quelltext):** SMAPI-Mod-Nachrichten gehen nur an Peers aus Stardews eigenem Koop. Das erfordert, dass der
Host einen Stardew-Spielstand lädt und Mitspieler über Stardews Koop-Menü (Steam/GOG-Einladung oder IP) einer Farm
beitreten. Die Vorgaben „keine sichtbare Farm / kein Farm-Spielstand“ und „private Koop-Verbindungen über tatsächlich
verfügbare Stardew-/Plattformfunktionen“ lassen sich daher nicht gleichzeitig vollständig erfüllen.

| Option | Vorteile | Nachteile |
|---|---|---|
| **A. Technischer Hintergrund-Spielstand** | Steam/GOG-Einladungen, NAT-Traversal der Plattform, Plattform-Identität | Spielstand muss programmatisch erzeugt/geladen werden; erscheint in Stardews Ladeliste; Tageswechsel/Speichern der Farm müssen unterdrückt werden; Beitritt über Stardews Koop-Menü (sichtbar); höheres Risiko, normale Spielstände zu berühren (Priorität 1!) |
| **B. Direkte TCP-Verbindung (implementiert)** | Kein Stardew-Spielstand, keine Farm, volle Kontrolle, getestet | Keine Plattform-Einladungen; Host braucht LAN oder Portweiterleitung (Port 24642); Identität über eigenes Schlüsselpaar statt Plattformkonto |
| C. Steamworks direkt nutzen | Einladungen ohne Farm | undokumentiert über Stardews gebündelte Bibliothek, nur Steam (nicht GOG), hohes Bruchrisiko bei Updates |

**Empfehlung:** B für Version 1 (umgesetzt), A optional später als zusätzlicher Transport (Schnittstelle vorhanden).
**Bitte bestätigen oder eine andere Option wählen.**

## K2 – Identität ohne Plattformkonto

Die Vorgabe verlangt eine „von der Plattform überprüfbare Teilnehmeridentität“. Bei Option B gibt es keine Plattform.
**E:** Jeder Spieler hat ein lokal erzeugtes ECDSA-P-256-Schlüsselpaar; die Teilnehmer-ID ist der Hash des öffentlichen
Schlüssels; beim (Re-)Connect signiert der Client eine zufällige Host-Nonce. Namen oder frei gewählte IDs werden nie
vertraut. Bei Option A käme die Stardew-Spieler-ID als zweiter Faktor hinzu.

## Eigenständige Entscheidungen

| # | Thema | Entscheidung |
|---|---|---|
| E1 | Ziel-Framework | net6.0 (SMAPI 4.5.2 / Stardew 1.6) |
| E2 | Tickrate | feste 60 Hz Simulation; Snapshots mit 20 Hz |
| E3 | Gescheiterte Wiederbelebung | Leben −1, Rückkehr nach 3 s am sichersten Startpunkt mit 50 % HP und 2 s Unverwundbarkeit |
| E4 | Kein möglicher Wiederbeleber (z. B. Solo) | Leben wird sofort abgezogen statt nutzlos zu warten |
| E5 | Getrennte Spieler | Figur bleibt in der Welt, steht still und kann getroffen werden (Disconnect ist kein Ausweichen); gleiche Regel für absichtliches und unabsichtliches Trennen |
| E6 | Gold/Loot | persönlich; jeder kampffähige Teilnehmer erhält für jeden Kill eigenes Gold, eigene EP und einen eigenen Loot-Wurf; Ausgeschiedene erhalten nichts, behalten aber ihren Zustand |
| E7 | Pakt: „Gruppe verliert Gold“ | Gold **aller** Teilnehmer wird auf 0 gesetzt; alle ursprünglichen Teilnehmer kehren mit genau 1 Leben zurück |
| E8 | Pakt-Ablehnung | eine Nein-Stimme beendet den Run sofort; fehlende Stimmen pausieren (Zustand gespeichert) |
| E9 | Boss-Stuns | Bosse sind nicht direkt betäubbar; Betäubungen füllen die Stagger-Leiste; nach Stagger 8 s Immunität |
| E10 | Normale Gegner | Betäubungen innerhalb von 6 s: 100 % → 50 % → immun |
| E11 | Skalierung | Spielerzahl wird beim Arenastart festgelegt und während der Arena nicht verändert |
| E12 | Händlerstationen | nach dem Kampf der Ebenen 4/9/14/19/24 (zwischen den Arenen) |
| E13 | Eingaben im Koop | Client sendet die letzten 4 Eingaben redundant (unzuverlässiger Kanal); Host sammelt Rückstand zusammen, ohne Tastendrücke zu verlieren |
| E14 | Freischaltungsprüfung in der Lobby | Clients melden ihre Freischaltungen; lokale Profile sind nicht manipulationssicher (laut Vorgabe §29 akzeptiert) |
| E15 | Datenablage | `%AppData%/StardewValley/ProjectAbyss/` – neben, nie in `Saves/`; Entwicklerprofil und -runs getrennt |
| E16 | Schriften | Spielschriften könnten Sonderzeichen fehlen; Texte werden auf vorhandene Glyphen abgebildet |
| E17 | Audio-IDs | alle Cues mit Präfix `abyss_` (keine Kollision mit anderen Mods) |

## Unvollständig spezifiziert (Vorschläge, noch nicht umgesetzt)

- Exakte Inhalte der 180 Skillknoten, 120 Gegenstände, 30 Gegner, 10 Bosse – werden in Phase 3–6 als Daten entworfen.
- Risikoarena: Abstimmungszeitpunkt (vor Betreten der Folgeebene) und Zeitlimit für die Abstimmung.
- Run-Truhe vs. Händler: Zugriff nur an Händlerstationen (so geplant).
- Musik-Überblendung: Stardews Musik-API bietet keine garantierte Kreuzblende – ggf. eigene Audiowiedergabe nötig.
