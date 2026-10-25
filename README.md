# RBCmd

Parses Windows Recycle Bin artifacts: `$I` files from `$Recycle.Bin` (Windows Vista and later) and `INFO2` files from
`RECYCLER` / `RECYCLED` (Windows 95 to XP/2003). For each deleted item it shows the original path, the size and when it
was deleted. Results are shown on the console and can be exported to CSV.

| | |
|---|---|
| **Version** | 2026.10.1 |
| **Download** | [.NET 10](https://download.ericzimmermanstools.com/net/RBCmd.zip) (recommended) &middot; [.NET Framework 4.7.2](https://download.ericzimmermanstools.com/RBCmd.zip) |
| **All tools** | [ericzimmerman.github.io](https://ericzimmerman.github.io) or [Get-ZimmermanTools](https://download.ericzimmermanstools.com/Get-ZimmermanTools.zip), which downloads them all and keeps them current |
| **Changes** | [ChangeLog](https://ericzimmerman.github.io/changelog/) |
| **Issues** | [EricZimmerman/RBCmd](https://github.com/EricZimmerman/RBCmd/issues) or [EricZimmerman/Issues](https://github.com/EricZimmerman/Issues/issues) |

## Requirements

- **.NET 10 build**: the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Run `RBCmd.exe` on Windows,
  or `dotnet RBCmd.dll` on any system with the runtime.
- **.NET Framework build**: .NET Framework 4.7.2 or later (Windows only).

## Command line

```text
Usage:
  RBCmd [options]

Options:
  -d <d>          Directory to recursively process. Either this or -f is required
  -f <f>          File to process. Either this or -d is required
  -q              Only show the filename being processed vs all output. Useful to speed up exporting to JSON and/or CSV
  --csv <csv>     Directory to save CSV formatted results to. Be sure to include the full path in double quotes
  --csvf <csvf>   File name to save CSV formatted results to. When present, overrides default name
  --dt <dt>       The custom date/time format to use when displaying time stamps. See https://goo.gl/CNVq0k for options [default: yyyy-MM-dd HH:mm:ss]
  --debug         Show debug information during processing
  --trace         Show trace information during processing
  --co            When true, save all console messages to <timestamp>_RBCmd_<version>_ConsoleLog.txt in the --csv directory. Use --co false to turn it off
  -?, -h, --help  Show help and usage information
  --version       Show version information
```

Short options (a single letter) start with one dash, long options with two.

## Examples

```text
RBCmd.exe -f "C:\Temp\INFO2"
RBCmd.exe -f "C:\Temp\$I3VPA17" --csv "D:\csvOutput"
RBCmd.exe -d "C:\Temp" --csv "c:\temp"
```

## Output

- **Console**: each deleted item in each file (`-q` shows only the file names). Times are UTC, as in the CSV.
- **CSV** (`--csv`): `<timestamp>_RBCmd_Output.csv` (or the `--csvf` name), one row per deleted item, with the columns
  `SourceName`, `FileType`, `FileName`, `FileSize`, `DeletedOn` and `Removed`.

The file type is detected from the content, not the name; `-d` finds files named `$I*` and `INFO2`.

- **`$I` files**: formats 1 (Vista to 8.1) and 2 (Windows 10/11). When the deleted item was a directory, the files in
  the matching `$R` directory are listed as well.
- **`INFO2` files**: 280 byte (Windows 9x) or 800 byte records. Windows clears the first byte of a record's ANSI path
  when the file leaves the Recycle Bin (restored, or the Recycle Bin was emptied), so those records are shown as removed
  and have `True` in the CSV `Removed` column.

Problems in a damaged file (including out of range time stamps) are reported as warnings, and everything readable is
still shown.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. Damaged `$I` and `INFO2` files are listed under Failed files but are not a failure |
| `1` | The tool failed: invalid arguments, a missing `-f` or `-d` path, output that can't be written, or files or folders it can't read. Folders under `-d` that can't be read are reported (`Unable to read folder <path>: <reason>`) and the rest is still processed |

## What's new in 2026.10

- `-d` finds files in the .NET Framework build (it found none).
- Contents of deleted folders (`$R` directories) are listed (they almost never were).
- `INFO2`: Windows 9x records (280 bytes) are read; new `Removed` column for records Windows has cleared (file restored
  or Recycle Bin emptied).
- Damaged `$I` and `INFO2` files are reported with what could be read, instead of crashing.
- Console times are UTC like the CSV; out of range time stamps are reported.
- A file that can't be read (for example locked by another process) is reported as "Unable to read".
- Exit code 1 when the tool fails (it was always 0). Error reports include source file and line numbers.
- 2026.10.1: `--co` (on by default; `--co false` turns it off) saves all console messages to `<yyyyMMddHHmmss>_RBCmd_<version>_ConsoleLog.txt` in the `--csv` directory. `--debug` and `--trace` show the time of each message again (they showed the `--dt` date).

The full list is in the [ChangeLog](https://ericzimmerman.github.io/changelog/).

## Building

```text
dotnet build RBCmd.slnx -c Release
dotnet test -c Release
```

Builds the `RecycleBin` library (`netstandard2.0`), RBCmd (`net10.0` and `net472`) and the tests. The parsers are also
available as the [RecycleBin](https://www.nuget.org/packages/RecycleBin) NuGet package: `new DollarI(bytes, path)` and
`new Info2(bytes, path)`. Each throws `RecycleBinParseException` when the data is unusable; other problems are listed in
`ParseErrors`.

## Special thanks

Open source development funding and support provided by the [SANS Institute](https://www.sans.org/) and
[SANS DFIR](https://www.sans.org/digital-forensics-incident-response/).

## License

[MIT](LICENSE), Copyright (c) Eric R. Zimmerman 2027.
