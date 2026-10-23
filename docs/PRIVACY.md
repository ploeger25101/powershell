# Datenschutz (DSGVO-orientiert)

Dies ist eine technische Beschreibung, **keine rechtliche Konformitätsaussage**.

- Keine Konten, keine Telemetrie, keine externen Server. Alle Daten liegen lokal (siehe [SAVE_FORMAT.md](SAVE_FORMAT.md)).
- Gespeichert werden: selbst gewählter Anzeigename, Spielfortschritt, Statistik, Einstellungen, ein lokal erzeugtes
  Schlüsselpaar. Keine IP-Adressen werden gespeichert; Logs enthalten Teilnehmer-IDs (Hashes), keine IPs.
- Netzwerk: Im Koop werden Spieldaten nur direkt zwischen Host und Mitspielern übertragen (TCP, Port 24642). Die
  Gegenstelle sieht dabei technisch bedingt die IP-Adresse. Die Übertragung ist **nicht verschlüsselt**; es werden
  aber keine personenbezogenen Daten außer dem Anzeigenamen übertragen.
- Einstellungen → „Profildaten exportieren“ schreibt alle Profildaten als JSON; „Profildaten löschen“ entfernt Profil,
  Sicherungskopien und Schlüssel.
- Sollte später Stardews eigener Koop (Steam/GOG) genutzt werden (Option A, [REQUIREMENTS_ANALYSIS.md](REQUIREMENTS_ANALYSIS.md#k1)),
  gelten zusätzlich die Datenverarbeitungen dieser Plattformen; das ist gesondert zu prüfen.
