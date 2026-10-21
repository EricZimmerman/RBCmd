# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Two deliverables in one repo, part of Eric Zimmerman's DFIR tool suite:
- **`RecycleBin`**: a `netstandard2.0` NuGet library that parses Recycle Bin artifacts: `$I` files (`DollarI`) and `INFO2` files (`Info2`). Released to NuGet from a GitHub release.
- **`RBCmd`**: the CLI (`net472;net10.0`). It references the library as a project, not the package, and displays or exports to CSV. Built, signed and staged manually; there is no GitHub release for it.

Input may be corrupt or hostile: parse everything that is there and report problems rather than giving up.

## Commands

The SDK is pinned in `global.json` (.NET 10). All projects use `LangVersion 10`, so avoid newer C# syntax. Tests use NUnit 5 + FluentAssertions 7 (pinned to `[7.0.0,8.0.0)`, since 8.x requires a commercial license).

Debug info: `Directory.Build.props` sets `DebugType embedded` (the pdbs are inside the assemblies: stack traces and Exceptionless reports have file and line numbers, and no .pdb file ships) and, for Release, `ContinuousIntegrationBuild` (source paths in the pdbs are `/_/...`, not this machine's folders). Don't add `DebugType` or `DebugSymbols` to the project files.

```bash
dotnet build -c Release                                    # builds RBCmd.slnx (library, CLI net472+net10.0, tests)
dotnet test -c Release                                     # all tests
dotnet test --filter "FullyQualifiedName~RobustnessTests"  # one fixture
dotnet test --filter "Name=Info2Sample"                    # one test
dotnet pack -c Release                                     # library package (README.md and icon.png from the repo root)
dotnet run --project RBCmd -f net10.0 -- -d RecycleBin.Test/TestFiles --csv out
```

## Formats and library

- **`DollarI`**: a 24 byte header (Int64 format 1 or 2, Int64 size, FILETIME deletion time). Format 1 is followed by a fixed 520 byte UTF-16 path. Format 2 is followed by an Int32 character count (including the NUL) and the path. When the source is `$IXXXXXX[.ext]` and `$RXXXXXX[.ext]` next to it is a directory, its files go in `DirectoryFiles` with paths under `Filename`, always with `\` separators. This is the only file system access in the library.
- **`Info2`**: a 20 byte header with the record size at 12, which is 280 (ANSI path only, Windows 9x, version 4) or 800 (plus a 520 byte UTF-16 path, version 5). Record layout: a 260 byte ANSI path, then index, drive number (0 = A:), FILETIME, size and the Unicode path. **`FileRecord.Removed`**: Windows clears the first byte of the ANSI path when a file leaves the Recycle Bin, so `FileNameAscii` is empty and the Unicode path is still there.
- Times are UTC (`Helpers.FromFileTime`). Out-of-range values become `DateTimeOffset.MinValue` and are added to `ParseErrors`.
- **Error model** (same as the Prefetch library): `RecycleBinParseException` (with `Offset` and `Field`) is thrown only when the data is unusable: smaller than the header, a `$I` format other than 1/2, or an INFO2 record size under 280. After that the constructors never throw. Problems go to `ParseErrors` (`ParsingError` is true when it isn't empty). Check lengths and counts against the remaining bytes before reading.

## CLI

All in `RBCmd/Program.cs`.
- `Main` awaits `InvokeAsync` and returns `DoWork`'s exit code: 0, or 1 for invalid arguments, any failed file, or a failed CSV export.
- File type is detected from content: Int64 1/2 is `$I`, Int32 4/5 is INFO2.
- `-d` finds `$I*` and `INFO2` files. On net10.0 this uses `System.IO` enumeration (case-sensitive, inaccessible folders skipped). On net472 it uses AlphaFS with the Backup privilege and skips reparse points.
- `#if NET9_0_OR_GREATER` separates the two targets. A previous `#if NET462` meant the net472 build found no files with `-d`, so build both targets.
- `CsvOut` defines the CSV columns. Keep the existing ones in order and add new ones at the end (`Removed` was added in 2026.10.0).

## Tests

`RecycleBin.Test` (net10.0) references the library and the CLI.
- `RecycleBinTest`: exact values for every sample in `TestFiles/` (copied to the test output), plus `$R` directory listing.
- `RobustnessTests`: in-memory `$I`/INFO2 builders (`DollarI2` reproduces the Win10 sample) covering malformed input and random corruption. Add regression tests here.
- `CliTests`: runs the built `RBCmd.dll` with `dotnet` as a separate process, for exit codes and CSV output.

## Versioning and release

- **Library:** MinVer sets the version from the git tag (bare tags like `2026.10.0`); there is no `<Version>` in its csproj. To release: push to `master`, then `git tag <ver> && git push origin <ver> && gh release create <ver> --verify-tag --generate-notes`.
- **CLI:** `<Version>` in `RBCmd/RBCmd.csproj`, bumped by hand per release.
- Versions are `yyyy.mm.x`.
- `.github/workflows/publish.yml` (identical to GuidMapping's) packs, validates and tests on PRs and pushes to `master`. When a GitHub Release is published, it pushes to NuGet.org using trusted publishing (`NuGet/login` with the `NUGET_USER` repo variable).
- Dependabot (`.github/dependabot.yml`) opens monthly PRs for GitHub Actions and NuGet updates.
- Release builds are staged, signed and zipped by `Build-ZimmermanTools.ps1` in the get-zimmermantools repository (cloned next to this one). Its program table says what goes in this tool's download zip (the files taken from the build, folders copied from the repositories, files kept from the current release zip); a new output file, a content folder or a change in how the tool is built (single file, another framework) needs that entry updated.
