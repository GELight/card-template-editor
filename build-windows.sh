#!/bin/sh
set -eu

# .NET-Sandbox-Setup (dotnet ist nicht im System-PATH)
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

PROJECT_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$PROJECT_ROOT"

PUBLISH_DIR="src/CardTemplateEditor/bin/Release/net8.0/win-x64/publish"
EXE_PATH="$PUBLISH_DIR/CardTemplateEditor.exe"

echo "==> Tests laufen lassen"
dotnet test --nologo

echo
echo "==> Windows-EXE bauen"
dotnet publish src/CardTemplateEditor -c Release -r win-x64 \
  --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  --nologo

echo
echo "==> Fertig"
if [ -f "$EXE_PATH" ]; then
    SIZE=$(du -h "$EXE_PATH" | cut -f1)
    TIMESTAMP=$(date -r "$EXE_PATH" "+%Y-%m-%d %H:%M")
    echo "    EXE: $PROJECT_ROOT/$EXE_PATH"
    echo "    Größe: $SIZE   Stand: $TIMESTAMP"
    echo
    echo "    Diese Datei alleine versenden — alles Weitere ist in der EXE eingebettet."
else
    echo "    FEHLER: EXE wurde nicht erzeugt unter $EXE_PATH"
    exit 1
fi
