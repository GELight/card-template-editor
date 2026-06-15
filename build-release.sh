#!/usr/bin/env bash
#
# Baut ein Windows-Release und packt die ausführbare EXE samt benötigter
# Dateien in ein ZIP.
#
#   - Single-File, self-contained (.NET-Runtime ist eingebettet)
#   - Ziel: win-x64
#   - Ergebnis: release/CardTemplateEditor-<version>-win-x64.zip
#
# Nutzung:
#
#   ./build-release.sh              # Version = Zeitstempel (+git), Tests laufen mit
#   ./build-release.sh 1.2.0        # Version explizit setzen
#   SKIP_TESTS=1 ./build-release.sh # Tests überspringen
#
set -eu

PROJECT_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$PROJECT_ROOT"

# --- dotnet mit .NET-8-Runtime finden ----------------------------------------
# Das Projekt zielt auf net8.0; `dotnet test` braucht die .NET-8-Runtime.
# Reihenfolge: lokales ~/.dotnet > PATH > bekannte System-Orte. Bevorzugt wird
# ein dotnet, das tatsächlich eine .NET-8-Runtime mitbringt.
_has_net8() {
    [ -x "$1/dotnet" ] || return 1
    "$1/dotnet" --list-runtimes 2>/dev/null | grep -q 'Microsoft\.NETCore\.App 8\.'
}

_dotnet_dir=""
_candidates="$HOME/.dotnet"
if command -v dotnet >/dev/null 2>&1; then
    _candidates="$_candidates $(dirname "$(command -v dotnet)")"
fi
_candidates="$_candidates /usr/share/dotnet /usr/lib/dotnet /opt/dotnet $HOME/dotnet"

for _p in $_candidates; do
    if _has_net8 "$_p"; then
        _dotnet_dir="$_p"
        break
    fi
done

if [ -n "$_dotnet_dir" ]; then
    export PATH="$_dotnet_dir:$PATH"
    export DOTNET_ROOT="$_dotnet_dir"
elif ! command -v dotnet >/dev/null 2>&1; then
    for _p in "$HOME/.dotnet" "$HOME/dotnet" /usr/share/dotnet /usr/lib/dotnet /opt/dotnet; do
        if [ -x "$_p/dotnet" ]; then
            export PATH="$_p:$PATH"
            export DOTNET_ROOT="$_p"
            break
        fi
    done
fi
if ! command -v dotnet >/dev/null 2>&1; then
    echo "FEHLER: Keine dotnet-Installation gefunden. Bitte zuerst ./setup-env.sh ausführen." >&2
    exit 1
fi
if ! dotnet --list-runtimes 2>/dev/null | grep -q 'Microsoft\.NETCore\.App 8\.'; then
    echo "WARNUNG: Keine .NET-8-Runtime gefunden. 'dotnet test' kann fehlschlagen." >&2
    echo "         Tipp: zuerst 'source ./setup-env.sh' ausführen (installiert .NET 8)." >&2
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# --- Version bestimmen -------------------------------------------------------
VERSION="${1:-}"
if [ -z "$VERSION" ]; then
    # Ohne Versionsangabe: Zeitstempel (YYYYMMDD-HHMM), damit man die
    # zeitliche Reihenfolge der ZIP-Dateien direkt am Namen ablesen kann.
    # Optional an einen git-Commit angehängt, falls ein Repo vorliegt.
    VERSION="$(date +%Y%m%d-%H%M)"
    if git -C "$PROJECT_ROOT" rev-parse --git-dir >/dev/null 2>&1; then
        GIT_REF="$(git -C "$PROJECT_ROOT" describe --tags --always --dirty 2>/dev/null || true)"
        [ -n "$GIT_REF" ] && VERSION="$VERSION-$GIT_REF"
    fi
fi

RID="win-x64"
PROJECT="src/CardTemplateEditor"
PUBLISH_DIR="$PROJECT/bin/Release/net8.0/$RID/publish"
EXE_NAME="CardTemplateEditor.exe"
EXE_PATH="$PUBLISH_DIR/$EXE_NAME"

RELEASE_DIR="$PROJECT_ROOT/release"
STAGE_NAME="CardTemplateEditor-$VERSION-$RID"
STAGE_DIR="$RELEASE_DIR/$STAGE_NAME"
ZIP_PATH="$RELEASE_DIR/$STAGE_NAME.zip"

echo "==> Release-Version: $VERSION"

# --- Tests -------------------------------------------------------------------
if [ "${SKIP_TESTS:-0}" = "1" ]; then
    echo "==> Tests übersprungen (SKIP_TESTS=1)"
else
    echo "==> Tests laufen lassen"
    dotnet test --nologo
fi

# --- Bauen -------------------------------------------------------------------
echo
echo "==> Windows-EXE bauen ($RID, single-file, self-contained)"
rm -rf "$PUBLISH_DIR"
dotnet publish "$PROJECT" -c Release -r "$RID" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    --nologo

if [ ! -f "$EXE_PATH" ]; then
    echo "FEHLER: EXE wurde nicht erzeugt unter $EXE_PATH" >&2
    exit 1
fi

# --- Stage zusammenstellen ---------------------------------------------------
echo
echo "==> Release-Verzeichnis zusammenstellen"
rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR"

# Alles aus publish außer Debug-Symbolen in die Stage kopieren.
# (Bei single-file ist das praktisch nur die EXE.)
find "$PUBLISH_DIR" -maxdepth 1 -type f ! -name '*.pdb' -exec cp {} "$STAGE_DIR/" \;

# Kurze Anleitung für Windows-Nutzer beilegen.
cat > "$STAGE_DIR/LIESMICH.txt" <<EOF
Card Template Editor – $VERSION ($RID)

Starten:
  CardTemplateEditor.exe doppelklicken.

Hinweise:
  - Es wird keine Installation und kein .NET benötigt; die Runtime ist
    in der EXE enthalten.
  - Beim ersten Start kann Windows SmartScreen warnen
    ("Mehr Infos" -> "Trotzdem ausführen").
EOF

# --- ZIP packen --------------------------------------------------------------
echo "==> ZIP packen"
rm -f "$ZIP_PATH"
if command -v zip >/dev/null 2>&1; then
    ( cd "$RELEASE_DIR" && zip -r -q "$STAGE_NAME.zip" "$STAGE_NAME" )
else
    # Fallback ohne zip-CLI
    ( cd "$RELEASE_DIR" && dotnet --version >/dev/null 2>&1; \
      python3 -c "import shutil,sys; shutil.make_archive('$STAGE_NAME','zip','$RELEASE_DIR','$STAGE_NAME')" )
fi

if [ ! -f "$ZIP_PATH" ]; then
    echo "FEHLER: ZIP wurde nicht erzeugt unter $ZIP_PATH" >&2
    exit 1
fi

# --- Zusammenfassung ---------------------------------------------------------
EXE_SIZE=$(du -h "$STAGE_DIR/$EXE_NAME" | cut -f1)
ZIP_SIZE=$(du -h "$ZIP_PATH" | cut -f1)
echo
echo "==> Fertig"
echo "    EXE:  $STAGE_DIR/$EXE_NAME   ($EXE_SIZE)"
echo "    ZIP:  $ZIP_PATH   ($ZIP_SIZE)"
echo
echo "    Das ZIP enthält die EXE und eine kurze LIESMICH.txt."
