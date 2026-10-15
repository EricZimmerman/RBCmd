# RBCmd

## Command Line Interface

     RBCmd version 0.5.0.0
    
     Author: Eric Zimmerman (saericzimmerman@gmail.com)
     https://github.com/EricZimmerman/RBCmd
     
             d               Directory to recursively process. Either this or -f is required
             f               File to process. Either this or -d is required
             q               Only show the filename being processed vs all output. Useful to speed up exporting to json and/or csv
     
             csv             Directory to save CSV formatted results to. Be sure to include the full path in double quotes
             csvf            File name to save CSV formatted results to. When present, overrides default name
     
             dt              The custom date/time format to use when displaying time stamps. See https://goo.gl/CNVq0k for options. Default is: yyyy-MM-dd HH:mm:ss
     
             debug           Show debug information during processing
             trace           Show trace information during processing
     
     
     Examples: RBCmd.exe -f "C:\Temp\INFO2"
               RBCmd.exe -f "C:\Temp\$I3VPA17" --csv "D:\csvOutput"
               RBCmd.exe -d "C:\Temp" --csv "c:\temp"

               Short options (single letter) are prefixed with a single dash. Long commands are prefixed with two dashes

## Documentation

Windows Recycle Bin artifact parser. It handles:

- `$I` files from `$Recycle.Bin` (Windows Vista and later), formats 1 (Vista to 8.1) and 2 (Windows 10/11). When the deleted item was a directory, the files in the matching `$R` directory are listed as well.
- `INFO2` files from `RECYCLER` / `RECYCLED` (Windows 95 to XP/2003), with 280 or 800 byte records. Windows clears the first byte of a record's ANSI path when the file leaves the Recycle Bin (restored, or the Recycle Bin was emptied), so those records are shown as removed and have `True` in the CSV `Removed` column.

Times are UTC. Problems in a damaged file are reported as warnings, and everything readable is still shown.

Exit code 0 means success. Exit code 1 means invalid arguments, a file that could not be processed, or a failed CSV export.

The .NET 10 build runs on Linux and macOS.

## RecycleBin NuGet package

The parsers are also available as the [RecycleBin](https://www.nuget.org/packages/RecycleBin) NuGet package: `new DollarI(bytes, path)` and `new Info2(bytes, path)`. Each throws `RecycleBinParseException` when the data is unusable; other problems are listed in `ParseErrors`.

# Download Eric Zimmerman's Tools

All of Eric Zimmerman's tools can be downloaded [here](https://ericzimmerman.github.io/#!index.md). Use the [Get-ZimmermanTools](https://f001.backblazeb2.com/file/EricZimmermanTools/Get-ZimmermanTools.zip) PowerShell script to automate the download and updating of the EZ Tools suite. Additionally, you can automate each of these tools using [KAPE](https://www.kroll.com/en/services/cyber-risk/incident-response-litigation-support/kroll-artifact-parser-extractor-kape)!

# Special Thanks

Open Source Development funding and support provided by the following contributors: 
- [SANS Institute](http://sans.org/) and [SANS DFIR](http://dfir.sans.org/).
- [Tines](https://www.tines.com/?utm_source=oss&utm_medium=sponsorship&utm_campaign=ericzimmerman)
