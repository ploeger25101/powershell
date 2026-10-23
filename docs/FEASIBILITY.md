# Phase 0 – Umgebung und technische Machbarkeit

Stand: 23.10.2026. Alle Aussagen unten sind entweder in dieser Umgebung ausgeführt/gemessen oder ausdrücklich als
„nicht prüfbar“ gekennzeichnet.

## 1. Vorgefundene Umgebung

| Prüfpunkt | Befund |
|---|---|
| Repository | leer (nur Initial-Commit) – keine bestehende Arbeit zu berücksichtigen |
| Betriebssystem | Linux-Container (Ubuntu 24.04), **kein Windows**, keine GPU |
| Stardew Valley | **nicht installiert** (proprietär, in der Cloud-Umgebung nicht verfügbar) |
| SMAPI (Laufzeit) | nicht installiert; Quelltext von github.com/Pathoschild/SMAPI geklont und ausgewertet |
| .NET | .NET SDK 8.0.132 per apt installiert (dot.net-Installer war vom Netzwerk-Proxy blockiert); baut `net6.0`-Ziele über das Targeting-Pack aus NuGet |
| NuGet | erreichbar |
| Grafik | Xvfb + Mesa (llvmpipe, Software-Rendering) für automatisierte Render-Tests |

## 2. Verifizierte Versionen

- **SMAPI 4.5.2** (laut `build/common.targets` und `docs/release-notes.md` im SMAPI-Repository, veröffentlicht
  14.03.2026, „for Stardew Valley 1.6.14 or later“). SMAPI selbst zielt auf **net6.0** (`src/SMAPI/SMAPI.csproj`).
- **Pathoschild.Stardew.ModBuildConfig 4.4.0** ist die neueste Version auf NuGet.
- Die Mod zielt daher auf **net6.0**; Core und Präsentation sind ebenfalls net6.0, damit sie im Spielprozess laden.

## 3. Im SMAPI-Quelltext verifizierte Schnittstellen

| API | Fundstelle | Verwendung |
|---|---|---|
| `IMultiplayerHelper.SendMessage<T>(msg, type, modIDs, playerIDs)` | `src/SMAPI/IMultiplayerHelper.cs` | (möglicher Stardew-Transport) |
| `IMultiplayerEvents.PeerConnected/PeerDisconnected/ModMessageReceived` | `src/SMAPI/Events/IMultiplayerEvents.cs` | (möglicher Stardew-Transport) |
| `IDisplayEvents.MenuChanged/RenderedActiveMenu` | `src/SMAPI/Events/IDisplayEvents.cs` | Titelbildschirm ersetzen, Rückweg-Knopf |
| `IGameLoopEvents.UpdateTicked/ReturnedToTitle` | `src/SMAPI/Events/IGameLoopEvents.cs` | 60-Hz-Takt |
| `IInputHelper.IsDown/GetCursorPosition/Suppress` | `src/SMAPI/IInputHelper.cs` | Eingabe |
| `IModContentHelper.Load<T>(path)` | `src/SMAPI/IModContentHelper.cs` | Texturen |
| `Constants.DataPath`, `Constants.SavesPath` | `src/SMAPI/Constants.cs` | eigener Datenordner **neben** `Saves` |
| `AssetRequestedEventArgs.Edit/NameWithoutLocale` | `src/SMAPI/Events/AssetRequestedEventArgs.cs` | Audio-Cues registrieren |
| `GameRunner.instance` | `SCore.cs` setzt `GameRunner.instance` | Spiel beenden (`Exit()`) |

**Wichtiger Befund zu Eingaben:** `SInputState` entfernt unterdrückte Tasten aus dem Zustand, `IsDown` liefert dann
`false`. Project Abyss unterdrückt deshalb keine Tasten, sondern fängt alle Eingaben über ein eigenes Vollbild-Menü ab.

**Kritischer Befund zum Multiplayer:** `SMultiplayer.BroadcastModMessage` sendet nur an `this.Peers` – also an
Spieler, die über Stardews eigenes Koop-System verbunden sind. Das setzt einen **geladenen Spielstand (Welt)** auf dem
Host und das Beitreten über Stardews Koop-Menü voraus. Mod-Nachrichten sind JSON (Newtonsoft) – für 20-Hz-Snapshots
nutzbar, aber nicht kompakt. Siehe Abschnitt 5.

## 4. Nicht verifizierbare Stardew-Valley-Member

Die Spiel-DLLs sind proprietär und lagen nicht vor. Folgende Member werden verwendet und müssen beim ersten Build auf
einem Rechner mit Spiel bestätigt werden (alle sind seit Jahren in Mods verbreitet, aber hier **nicht kompiliert**):
`Game1.activeClickableMenu`, `Game1.uiViewport`, `Game1.getMouseX()/getMouseY()`, `Game1.smallFont`,
`Game1.dialogueFont`, `Game1.graphics`, `Game1.staminaRect`, `Game1.playSound(string)`, `Game1.changeMusicTrack(string)`,
`TitleMenu`, `TitleMenu.subMenu`, `IClickableMenu`-Konstruktor und Overrides, `StardewValley.GameData.AudioCueData`
(Data/AudioChanges, Stardew 1.6), `GameRunner.instance.Exit()`.

## 5. Machbarkeitsnachweise (Abschnitt 3 der Vorgabe)

| # | Anforderung | Status | Nachweis |
|---|---|---|---|
| 1 | Start ohne sichtbare Farm | **Architektur steht, im Spiel nicht getestet** | Gesamtes Spiel läuft in `AbyssShellMenu` auf dem Titelbildschirm; Einzelspieler benötigt keinen Stardew-Spielstand. Im Standalone-Host vollständig spielbar. |
| 2 | Normales Stardew erreichbar | **Implementiert, im Spiel nicht getestet** | Menüpunkt öffnet `TitleMenu`; Rückweg-Knopf auf dem Vanilla-Titel. |
| 3 | Actionkampf mit Dash und freiem Zielen | **Nachgewiesen (Simulation + Rendering)** | Tests `CombatTests`, `MathTests`; Screenshots aus dem Standalone-Host. |
| 4 | Zwei Spieler in derselben Arena | **Nachgewiesen über In-Memory-Netz und echte TCP-Sockets** | `NetworkTests`, `TcpTests`; Bot-Koop im Entwicklermodus. |
| 5 | Host synchronisiert Gegner und Schaden | **Nachgewiesen** | Snapshots, Client nur Eingaben; Replik-Konvergenz bei 10 % Verlust. |
| 6 | Client-Reconnect mitten im Kampf | **Nachgewiesen** | `ReconnectMidCombatRestoresStateWithProtectionAndNoFreeHealing`, Entwicklermodus F7. |
| 7 | Konsistente Wiederherstellung nach Host-Absturz | **Nachgewiesen (simulierte Abstürze)** | `CrashRecoveryTests` (Hunderte Abstürze), `HostCrashAndRestartResumesRunAndClientRejoins`, F8. |
| 8 | Keine Duplikate durch Wiederherstellung | **Nachgewiesen** | Idempotente Transaktionen, Invarianten im Stresstest. |
| 9 | Performance | **Simulation nachgewiesen, Rendering nur in Software gemessen** | 32 Gegner + 4 Spieler: Ø 0,11 ms/Tick (Budget 16,7 ms); Rendering unter llvmpipe Ø 12–16 ms/Frame. |

**Offener Grundsatzpunkt (Zustimmung erforderlich):** Punkt 1 und „Private Koop-Verbindungen über tatsächlich
verfügbare Stardew-/Plattformfunktionen“ widersprechen sich technisch, weil Stardews Netzwerk einen geladenen Spielstand
braucht. Optionen und Empfehlung: [REQUIREMENTS_ANALYSIS.md](REQUIREMENTS_ANALYSIS.md#k1).
