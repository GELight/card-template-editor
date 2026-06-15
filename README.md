# Entwicklungs-Umgebung vorbereiten

```bash
# dotnet finden, PATH/DOTNET_ROOT setzen, Pakete wiederherstellen.
# Mit `source`, damit die Variablen in der aktuellen Shell wirken:
source ./setup-env.sh
```

Das Projekt zielt auf **net8.0**. Fehlt eine .NET-8-Runtime (z. B. wenn nur
.NET 10 installiert ist), installiert `setup-env.sh` das .NET-8-SDK einmalig
lokal nach `~/.dotnet` — ohne root-Rechte.

# App in Linux starten

```bash
dotnet run --project src/CardTemplateEditor
```

# App für Windows als EXE bauen (Release-ZIP)

```bash
# Baut single-file, self-contained win-x64-EXE und packt sie als ZIP
# nach release/. Version wird aus git/Datum abgeleitet.
./build-release.sh

# Version explizit setzen:
./build-release.sh 1.2.0

# Tests überspringen:
SKIP_TESTS=1 ./build-release.sh
```

Ergebnis: `release/CardTemplateEditor-<version>-win-x64.zip` (EXE + LIESMICH.txt).