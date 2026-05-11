# Card Template Editor

Avalonia 12.0.2 / .NET 8 Desktop-App. Wird primär unter Linux entwickelt, soll als Single-File-EXE unter Windows/WINE laufen.

**Single Source of Truth:** `~/.claude/plans/planung-eines-kleinen-nativen-wise-sky.md`
Architektur, Iterations-Reihenfolge, Test-Strategie und Fortschritts-Tracking stehen dort. Bei jedem Sessionstart kurz öffnen, das Fortschritts-Tracking am Ende sagt, wo wir stehen.

## Projekt-Layout

- `src/CardTemplateEditor/` — Avalonia-App (Models / Views / ViewModels / Services)
- `tests/CardTemplateEditor.Tests/` — xUnit. Ab Iteration 4 zusätzlich `Avalonia.Headless.XUnit` für UI-Tests, ab Iteration 7 Pixel-Sampling für ExportService.

## Sandbox-Eigenheiten

`dotnet` ist **nicht** im System-PATH. Vor jedem `dotnet`-Aufruf:

```bash
export PATH="$HOME/.dotnet:$PATH" && export DOTNET_ROOT="$HOME/.dotnet"
```

SDK-Version: 8.0.420, user-lokal in `~/.dotnet`.

Die App selbst kann ich in dieser Sandbox **nicht interaktiv ausführen** (keine Display-Server-Garantie, kein User-Input). Verifikation läuft ausschließlich über `dotnet test`.

## Definition of Done pro Iteration

Eine Iteration im Plan-Tracking darf erst angehakt werden, wenn:

1. Die in der Plan-Tabelle "Welche Tests in welcher Iteration" benannten Tests existieren
2. `dotnet test` grün läuft (komplette Suite, nicht nur die neuen)
3. Im Fortschritts-Tracking ist `Tests: X/X grün` notiert

Wenn etwas rot bleibt: Iteration offen lassen, Blocker im Tracking notieren — nicht überspringen.

## Test-Disziplin (gilt IMMER, nicht nur pro Iteration)

**Jede Verhaltens­änderung braucht einen automatischen Test, bevor ich sie als
"fertig" melde.** Auch ad-hoc-Bugfixes oder kleine Feature-Erweiterungen.
Kein "ich teste das nur manuell" — die Sandbox kann die App nicht
interaktiv ausführen, Regressionen würden so unbemerkt durchrutschen.

Konkret:

1. **Bug-Fix?** Vor dem Fix einen Test schreiben, der den Bug reproduziert
   (rot). Dann fixen (grün). So ist der Regress­schutz dauerhaft im Repo.
2. **Neues Feature?** Beim Hinzufügen einer Property/Methode mindestens
   einen Test, der den Happy-Path abdeckt — und einen, der die Invariante
   prüft, an der das Feature scheitern würde, wenn ein zukünftiger Refactor
   es bricht.
3. **Editor ↔ Export-Konsistenz** ist eine zentrale Invariante: Live-
   Vorschau (`WarpPreviewService`, TextFieldFrame) muss zum exportierten
   PNG passen. Jede neue Render-Property (Stretch, LineHeight,
   LetterSpacing, …) braucht einen Test, der genau das absichert — sonst
   weicht das gespeicherte Bild vom Editor ab und der User merkt es zuerst.
4. **Test-First bei UI-Bugs**: Wenn der User meldet "Editor und Output
   stimmen nicht überein", ist der erste Schritt ein Test, der die
   Differenz quantitativ einfängt (Pixel-Sample, BoundingBox-Vergleich).
   Danach erst die Code-Änderung.
5. `dotnet test` muss am Ende jeder Antwort grün sein, sonst ist der Task
   nicht abgeschlossen.

## Häufige Befehle

```bash
# Build
dotnet build

# Komplette Test-Suite
dotnet test

# Nur eine Klasse
dotnet test --filter "FullyQualifiedName~TemplateRepositoryTests"

# Windows-EXE bauen (Iteration 9)
dotnet publish src/CardTemplateEditor -c Release -r win-x64 \
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Konventionen

- UI-Texte, Plan, Memory, Commit-Messages: **Deutsch** (passend zum bestehenden Stil)
- Persistenz: System.Text.Json mit camelCase, gemeinsame Options in `Services/JsonStorage.cs`
- Atomares Schreiben: erst nach `<file>.tmp`, dann `File.Move(... overwrite: true)`
- `<ImplicitUsings>enable</ImplicitUsings>` ist im Hauptprojekt aktiv (war im Avalonia-Template default aus, wurde manuell gesetzt)
- Compiled Bindings sind aktiv (`AvaloniaUseCompiledBindingsByDefault=true`) → `x:DataType` in jedem Avalonia-XAML setzen, sonst Build-Fehler
