#!/usr/bin/env bash
#
# Bereitet die Entwicklungs-Umgebung vor, damit die App gestartet oder
# bearbeitet werden kann.
#
#   - Findet die dotnet-Installation (System-PATH oder ~/.dotnet-Sandbox)
#   - Setzt PATH und DOTNET_ROOT
#   - Stellt die NuGet-Pakete wieder her (dotnet restore)
#
# Nutzung:
#
#   # In der aktuellen Shell die Umgebung setzen (empfohlen):
#   source ./setup-env.sh
#
#   # Danach z.B.:
#   dotnet run --project src/CardTemplateEditor
#
# Wird das Script direkt ausgeführt (./setup-env.sh statt source), wirken
# die Umgebungs-Variablen nur innerhalb des Scripts. Es wird dann nur der
# Restore durchgeführt und ein Hinweis zum source-Aufruf ausgegeben.

# --- sourced oder direkt ausgeführt? -----------------------------------------
_cte_sourced=0
if [ -n "${BASH_SOURCE:-}" ] && [ "${BASH_SOURCE[0]}" != "${0}" ]; then
    _cte_sourced=1
fi

_cte_die() {
    echo "FEHLER: $*" >&2
    if [ "$_cte_sourced" = "1" ]; then
        return 1
    else
        exit 1
    fi
}

# --- Projekt-Wurzel bestimmen ------------------------------------------------
if [ "$_cte_sourced" = "1" ]; then
    _cte_script="${BASH_SOURCE[0]}"
else
    _cte_script="$0"
fi
PROJECT_ROOT="$(cd "$(dirname "$_cte_script")" && pwd)"

# Das Projekt zielt auf net8.0 — es wird zwingend eine .NET-8-Runtime benötigt.
# Eine reine .NET-10-Installation (wie sie hier im System liegt) kann die App
# NICHT starten ("You must install or update .NET to run this application").
CTE_DOTNET_CHANNEL="8.0"
CTE_LOCAL_DOTNET="$HOME/.dotnet"

# Liefert das dotnet in $1 eine .NET-8-Runtime? (Microsoft.NETCore.App 8.x)
_cte_has_net8() {
    [ -x "$1/dotnet" ] || return 1
    "$1/dotnet" --list-runtimes 2>/dev/null \
        | grep -q 'Microsoft\.NETCore\.App 8\.'
}

# Installiert das .NET-8-SDK lokal nach ~/.dotnet (kein root nötig).
_cte_install_net8() {
    local installer
    installer="$(mktemp)"
    echo "==> Lade .NET-8-Installer (dot.net/v1/dotnet-install.sh)"
    if ! curl -fsSL --max-time 30 https://dot.net/v1/dotnet-install.sh -o "$installer"; then
        rm -f "$installer"
        _cte_die "Download des .NET-Installers fehlgeschlagen (kein Netz?). Manuell: https://dotnet.microsoft.com/download/dotnet/8.0"
        return 1
    fi
    chmod +x "$installer"
    echo "==> Installiere .NET-$CTE_DOTNET_CHANNEL-SDK nach $CTE_LOCAL_DOTNET (einmalig, ~210 MB)"
    if ! "$installer" --channel "$CTE_DOTNET_CHANNEL" --install-dir "$CTE_LOCAL_DOTNET" --no-path; then
        rm -f "$installer"
        _cte_die "Installation des .NET-8-SDK fehlgeschlagen."
        return 1
    fi
    rm -f "$installer"
}

# --- dotnet mit .NET-8-Runtime finden ----------------------------------------
# Reihenfolge der Kandidaten: lokales ~/.dotnet > PATH > bekannte System-Orte.
_cte_dotnet_dir=""
_cte_candidates="$CTE_LOCAL_DOTNET"
if command -v dotnet >/dev/null 2>&1; then
    _cte_candidates="$_cte_candidates $(dirname "$(command -v dotnet)")"
fi
_cte_candidates="$_cte_candidates /usr/share/dotnet /usr/lib/dotnet /opt/dotnet $HOME/dotnet"

for _p in $_cte_candidates; do
    if _cte_has_net8 "$_p"; then
        _cte_dotnet_dir="$_p"
        break
    fi
done

# Keine .NET-8-Runtime vorhanden -> lokal nach ~/.dotnet installieren.
if [ -z "$_cte_dotnet_dir" ]; then
    echo "==> Keine .NET-8-Runtime gefunden (Projekt zielt auf net8.0)."
    _cte_install_net8 || { unset _cte_sourced _cte_script _cte_dotnet_dir _p _cte_candidates; return 1 2>/dev/null || exit 1; }
    if _cte_has_net8 "$CTE_LOCAL_DOTNET"; then
        _cte_dotnet_dir="$CTE_LOCAL_DOTNET"
    else
        _cte_die "Nach der Installation ist weiterhin keine .NET-8-Runtime auffindbar."
        unset _cte_sourced _cte_script _cte_dotnet_dir _p _cte_candidates
        return 1 2>/dev/null || exit 1
    fi
fi

export PATH="$_cte_dotnet_dir:$PATH"
export DOTNET_ROOT="$_cte_dotnet_dir"
# Telemetrie aus, schnellerer/leiserer Start.
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

echo "==> dotnet: $_cte_dotnet_dir/dotnet  (SDK $(dotnet --version 2>/dev/null), .NET-8-Runtime vorhanden)"

# --- Pakete wiederherstellen -------------------------------------------------
echo "==> NuGet-Pakete wiederherstellen (dotnet restore)"
if dotnet restore "$PROJECT_ROOT/CardTemplateEditor.sln" --nologo; then
    echo "==> Umgebung bereit."
else
    _cte_die "dotnet restore fehlgeschlagen."
fi

# --- Hinweise ----------------------------------------------------------------
if [ "$_cte_sourced" = "1" ]; then
    cat <<'EOF'

Bereit. Nützliche Befehle:
  dotnet run   --project src/CardTemplateEditor      # App starten
  dotnet build                                        # Bauen
  dotnet test                                         # Tests
EOF
else
    cat <<'EOF'

Hinweis: Dieses Script wurde direkt ausgeführt. Die gesetzten Umgebungs-
Variablen (PATH, DOTNET_ROOT) gelten NUR innerhalb des Scripts.

Damit dotnet auch in deiner Shell verfügbar ist, source das Script:

  source ./setup-env.sh
EOF
fi

# --- Aufräumen (nur relevant beim source) ------------------------------------
unset _cte_sourced _cte_script _cte_dotnet_dir _p _cte_candidates
unset CTE_DOTNET_CHANNEL CTE_LOCAL_DOTNET
unset -f _cte_die _cte_has_net8 _cte_install_net8 2>/dev/null || true
