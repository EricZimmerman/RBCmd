using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using Exceptionless;
using RecycleBin;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using ServiceStack;
using CsvWriter = CsvHelper.CsvWriter;
#if !NET9_0_OR_GREATER
using Directory = Alphaleonis.Win32.Filesystem.Directory;
using File = Alphaleonis.Win32.Filesystem.File;
using Path = Alphaleonis.Win32.Filesystem.Path;
using Alphaleonis.Win32.Filesystem;
using Alphaleonis.Win32.Security;
#else
using Path = System.IO.Path;
using Directory = System.IO.Directory;
using File = System.IO.File;
#endif

namespace RBCmd;

public class Program
{
    private static List<string> _failedFiles;
    //files that could not be read (access denied, IO errors): exit code 1. Damaged files are only listed as failed
    private static int _unreadFiles;
    private static List<CsvOut> _csvOuts;

    public static string ActiveDateTimeFormat;

    private static string Header =
        $"RBCmd version {Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}" +
        "\r\n\r\nAuthor: Eric Zimmerman (saericzimmerman@gmail.com)" +
        "\r\nhttps://github.com/EricZimmerman/RBCmd";


    private static string Footer = @"Examples: RBCmd.exe -f ""C:\Temp\INFO2""" + "\r\n\t " +
                                   @"   RBCmd.exe -f ""C:\Temp\$I3VPA17"" --csv ""D:\csvOutput"" " + "\r\n\t " +
                                   @"   RBCmd.exe -d ""C:\Temp"" --csv ""c:\temp"" " + "\r\n\t " +
                                   "\r\n\t" +
                                   "    Short options (single letter) are prefixed with a single dash. Long commands are prefixed with two dashes\r\n";

    private static RootCommand _rootCommand;

    private static async Task<int> Main(string[] args)
    {
        ExceptionlessClient.Default.Startup("IudF6lFjzvdMldPtlYyPmSMHnSEL89n2WmYbCHoy");
        
        var dOpt = new Option<string>("-d")
        {
            Description = "Directory to recursively process. Either this or -f is required"
        };

        var fOpt = new Option<string>("-f")
        {
            Description = "File to process. Either this or -d is required"
        };

        var qOpt = new Option<bool>("-q")
        {
            Description = "Only show the filename being processed vs all output. Useful to speed up exporting to JSON and/or CSV"
        };

        var dtOpt = new Option<string>(
                "--dt"){
                Description =        "The custom date/time format to use when displaying time stamps. See https://goo.gl/CNVq0k for options",
            DefaultValueFactory = _ => "yyyy-MM-dd HH:mm:ss"
        };

        var csvOpt = new Option<string>(
            "--csv")
        {
         Description = "Directory to save CSV formatted results to. Be sure to include the full path in double quotes"
        
        };

        var csvfOpt = new Option<string>(
            "--csvf")
        {
         Description   = "File name to save CSV formatted results to. When present, overrides default name"
        };

        var debugOpt = new Option<bool>("--debug")
        {
            Description = "Show debug information during processing",
            DefaultValueFactory = _ => false
        };
        var traceOpt = new Option<bool>("--trace")
        {
            Description = "Show trace information during processing",
            DefaultValueFactory = _ => false
        };
        var coOpt = new Option<bool>("--co")
        {
            Description = "Save all console messages to <timestamp>_RBCmd_<version>_ConsoleLog.txt in the --csv directory",
            DefaultValueFactory = _ => false
        };
        
        _rootCommand = new RootCommand
        {
            dOpt,
            fOpt,
            qOpt,
            csvOpt,
            csvfOpt,
            dtOpt,
            debugOpt,
            traceOpt,
            coOpt
        };

        _rootCommand.Description = Header + "\r\n\r\n" + Footer;

        _rootCommand.SetAction(result => DoWork(result.GetValue(dOpt), result.GetValue(fOpt), result.GetValue(qOpt),
            result.GetValue(csvOpt), result.GetValue(csvfOpt), result.GetValue(dtOpt), result.GetValue(debugOpt),
            result.GetValue(traceOpt), result.GetValue(coOpt)));
            
        var exitCode = await _rootCommand.Parse(args).InvokeAsync();

        Log.CloseAndFlush();

        return exitCode;
    }

    /// <returns>0 on success. 1 when arguments are invalid, a file fails or the CSV export fails</returns>
    private static int DoWork(string d, string f,  bool q, string csv, string csvf, string dt, bool debug, bool trace, bool co)
    {
        ActiveDateTimeFormat = dt;
        
        var formatter  =
            new DateTimeOffsetFormatter(CultureInfo.CurrentCulture);
        
        var levelSwitch = new LoggingLevelSwitch();

        var template = "{Message:lj}{NewLine}{Exception}";

        if (debug)
        {
            levelSwitch.MinimumLevel = LogEventLevel.Debug;
            template = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}";
        }

        if (trace)
        {
            levelSwitch.MinimumLevel = LogEventLevel.Verbose;
            template = "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}";
        }

        var conf = new LoggerConfiguration()
            .WriteTo.Console(outputTemplate: template,formatProvider: formatter)
            .MinimumLevel.ControlledBy(levelSwitch);

        var tsNow = DateTimeOffset.UtcNow;

        var consoleLog = co ? ConsoleLog.GetPath(tsNow, csv) : null;
        if (consoleLog != null)
        {
            conf.WriteTo.File(consoleLog, outputTemplate: ConsoleLog.Template, formatProvider: formatter);
        }

        Log.Logger = conf.CreateLogger();

        if (f.IsNullOrEmpty() &&
            d.IsNullOrEmpty())
        {
            var helpAction = new CustomHelpAction(new HelpAction());
            helpAction.Invoke(_rootCommand.Parse(""));

            return 1;
        }

        if (f.IsNullOrEmpty() == false &&
            !File.Exists(f))
        {
            Log.Warning("File {F} not found. Exiting", f);
            return 1;
        }

        if (d.IsNullOrEmpty() == false &&
            !Directory.Exists(d))
        {
            Log.Warning("Directory {D} not found. Exiting", d);
            return 1;
        }

        Log.Information("{Header}", Header);
        Console.WriteLine();
        Log.Information("Command line: {Args}", string.Join(" ", Environment.GetCommandLineArgs().Skip(1)));

        if (consoleLog != null)
        {
            Log.Information("Console log: {Path}", consoleLog);
        }
        else if (co)
        {
            Log.Warning("--co: no output directory was given, so the console messages are not saved");
        }
        
        if (IsAdministrator() == false)
        {
            Log.Warning("Warning: Administrator privileges not found!");
            Console.WriteLine();
        }

        _csvOuts = new List<CsvOut>();
        _failedFiles = new List<string>();
        _unreadFiles = 0;

        var files = new List<string>();

        var sw = new Stopwatch();
        sw.Start();

        if (f?.Length > 0)
        {
            files.Add(f);
        }
        else
        {
            
            Console.WriteLine();
        
            Log.Information("Looking for files in {Dir}",d);
            if (!q)
            {
                Console.WriteLine();
            }
            
            files = GetRecycleBinFiles(d);
        }

        Log.Information("Found {Count:N0} files. Processing...",files.Count);

        if (!q)
        {
            Console.WriteLine();
        }
        
        foreach (var file in files)
        {
            ProcessFile(file, q, dt);
        }

        sw.Stop();

        Console.WriteLine();
        Log.Information(
            "Processed {FailedFilesCount:N0} out of {Count:N0} files in {TotalSeconds:N4} seconds", files.Count - _failedFiles.Count, files.Count, sw.Elapsed.TotalSeconds);
        Console.WriteLine();

        if (_failedFiles.Count > 0)
        {
            Console.WriteLine();
            Log.Information("Failed files");
            foreach (var failedFile in _failedFiles)
            {
                Log.Information("  {FailedFile}", failedFile);
            }
        }

        if (csv.IsNullOrEmpty() == false && files.Count > 0)
        {
            try
            {
                if (Directory.Exists(csv) == false)
                {
                    Log.Information("{Csv} does not exist. Creating...", csv);
                    Directory.CreateDirectory(csv);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Unable to create {Csv}. CSV export canceled. Error: {Message}", csv, ex.Message);
                return 1;
            }

            var outName = $"{tsNow:yyyyMMddHHmmss}_RBCmd_Output.csv";

            if (csvf.IsNullOrEmpty() == false)
            {
                outName = Path.GetFileName(csvf);
            }


            var outFile = Path.Combine(csv, outName);

            outFile =
                Path.GetFullPath(outFile);

            Log.Warning("CSV output will be saved to {Path}", Path.GetFullPath(outFile));

            try
            {
                using var sw1 = new StreamWriter(outFile);
                using var csvWriter = new CsvWriter(sw1, CultureInfo.InvariantCulture);

                csvWriter.WriteHeader(typeof(CsvOut));
                csvWriter.NextRecord();

                foreach (var csvOut in _csvOuts)
                {
                    csvWriter.WriteRecord(csvOut);
                    csvWriter.NextRecord();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "Unable to open {OutFile} for writing. CSV export canceled. Error: {Message}", outFile, ex.Message);
                return 1;
            }
        }

        return _unreadFiles > 0 ? 1 : 0;
    }

    private static string BytesToString(long byteCount)
    {
        string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
        if (byteCount == 0)
        {
            return "0" + suf[0];
        }

        var bytes = Math.Abs(byteCount);
        var place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        var num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return $"{Math.Sign(byteCount) * num}{suf[place]}";
    }

    private static void ProcessFile(string file, bool q, string dt)
    {
        try
        {
            byte[] raw;
            try
            {
                raw = File.ReadAllBytes(file);
            }
            catch (IOException io)
            {
                //the file could not be read (locked, IO error): not a damaged file
                Log.Error(io, "Unable to read {File}. Error: {Message}", file, io.Message);
                _failedFiles.Add(file);
                _unreadFiles++;
                return;
            }

            // $I files start with a 64-bit format of 1 or 2, INFO2 files with a 32-bit version of 4 or 5
            var format = raw.Length >= 8 ? BitConverter.ToInt64(raw, 0) : -1;
            var version = raw.Length >= 4 ? BitConverter.ToInt32(raw, 0) : -1;

            List<string> parseErrors;

            if (format == 1 || format == 2)
            {
                var di = new DollarI(raw, file);

                DisplayDollarI(di, dt, q);
                parseErrors = di.ParseErrors;
            }
            else if (version == 4 || version == 5)
            {
                var info = new Info2(raw, file);

                DisplayInfo2(info, q, dt);
                parseErrors = info.ParseErrors;
            }
            else
            {
                if (raw.Length == 0)
                {
                    Log.Warning("{File} is empty", file);
                }
                else
                {
                    Log.Warning(
                        "Unknown header 0x{Raw:X}! Send file to {Email} so support can be added", raw[0],
                        "saericzimmerman@gmail.com");
                }

                _failedFiles.Add(file);
                parseErrors = new List<string>();
            }

            foreach (var parseError in parseErrors)
            {
                Log.Warning("{File}: {ParseError}", file, parseError);
            }

            //a file that did not parse completely is still exported, and listed as a failed file
            if (parseErrors.Count > 0)
            {
                _failedFiles.Add($"{file} did not parse completely");
            }

            if (q == false)
            {
                Console.WriteLine();
            }
        }
        catch (RecycleBinParseException pe)
        {
            Log.Error("Error processing file {File}: {Message} (offset 0x{Offset:X}, field {Field})", file, pe.Message,
                pe.Offset, pe.Field);
            _failedFiles.Add(file);
        }
        catch (UnauthorizedAccessException ua)
        {
            Log.Error(ua,
                "Unable to access {File}. Are you running as an administrator? Error: {Message}", file, ua.Message);
            _failedFiles.Add(file);
            _unreadFiles++;
        }
        catch (Exception ex)
        {
            _failedFiles.Add(file);
            Log.Error(ex,
                "Error processing file {File} Please send it to {Email}. Error: {Message}", file, "saericzimmerman@gmail.com", ex.Message);
        }
    }

    private static List<string> GetRecycleBinFiles(string dir)
    {
        var files = new List<string>();


#if NET9_0_OR_GREATER
        // Files only: a deleted directory has a $R directory, never a $I one, but don't trip over odd names.
        // One pass (case-sensitive, as before), $I files first, then INFO2 files. Folders that can't be read are reported
        // and counted (IgnoreInaccessible skipped them silently)
        var infos = new List<string>();
        using (var finder = new FileFinder(dir))
        {
            while (finder.MoveNext())
            {
                if (Path.GetFileName(finder.Current) == "INFO2")
                {
                    infos.Add(finder.Current);
                }
                else
                {
                    files.Add(finder.Current);
                }
            }

            foreach (var (folder, reason) in finder.Unreadable)
            {
                Log.Error("Unable to read folder {Folder}: {Reason}", folder ?? dir, reason);
                _unreadFiles++;
            }
        }

        files.AddRange(infos);
#else
        Privilege[] privs = { Privilege.EnableDelegation, Privilege.Impersonate, Privilege.Tcb };
        using var enabler = new PrivilegeEnabler(Privilege.Backup, privs);
        var filters = new DirectoryEnumerationFilters
        {
            // Used to abort the enumeration.
            // CancellationToken = cancelSource.Token,

            // Filter to decide whether to recurse into subdirectories.
            RecursionFilter = entryInfo =>
            {
                if (!entryInfo.IsMountPoint && !entryInfo.IsSymbolicLink)
                {
                    return true;
                }

                return false;
            },

            // Filter to process Exception handling.
            ErrorFilter = delegate(int errorCode, string errorMessage, string pathProcessed)
            {
                //a folder that can't be read is reported and counted (exit code 1); the rest of the run continues
                Log.Error("Unable to read folder {Folder}: {Reason}", pathProcessed, errorMessage);
                _unreadFiles++;

                // Return true to continue, false to throw the Exception.
                return true;
            },

            // Filter to in-/exclude file system entries during the enumeration.
            InclusionFilter = entryInfo =>
            {
                if (entryInfo.FileName == "INFO2" || entryInfo.FileName.StartsWith("$I"))
                {
                    Log.Debug("Found match: {FullPath}", entryInfo.FullPath);
                    return true;
                }

                return false;
            }
        };

        var dirEnumOptions =
            //no ContinueOnException: with it AlphaFS skips folders it can't read without calling ErrorFilter
            DirectoryEnumerationOptions.Files | DirectoryEnumerationOptions.Recursive |
            DirectoryEnumerationOptions.SkipReparsePoints;

        files.AddRange(Directory.EnumerateFileSystemEntryInfos<string>(dir, dirEnumOptions, filters).Where(File.Exists));
#endif

        return files;
    }

#if NET9_0_OR_GREATER
    /// <summary>
    ///     $I* and INFO2 files under a folder, recursively (case-sensitive). Folders that can't be read are skipped and
    ///     listed in <see cref="Unreadable" /> (a null folder is the root, reported before this class's fields were set)
    /// </summary>
    private sealed class FileFinder : System.IO.Enumeration.FileSystemEnumerator<string>
    {
        private string _lastRecursed;

        public readonly List<(string Folder, string Reason)> Unreadable = new();

        public FileFinder(string directory) : base(directory,
            new EnumerationOptions { IgnoreInaccessible = false, RecurseSubdirectories = true, AttributesToSkip = 0 })
        {
        }

        protected override bool ShouldRecurseIntoEntry(ref System.IO.Enumeration.FileSystemEntry entry)
        {
            //the subfolder is opened right after this: an error in ContinueOnError is about it
            _lastRecursed = entry.ToFullPath();
            return true;
        }

        protected override bool ShouldIncludeEntry(ref System.IO.Enumeration.FileSystemEntry entry)
        {
            _lastRecursed = null;
            if (entry.IsDirectory)
            {
                return false;
            }

            var name = entry.FileName;
            return name.StartsWith("$I".AsSpan(), StringComparison.Ordinal) || name.SequenceEqual("INFO2".AsSpan());
        }

        protected override string TransformEntry(ref System.IO.Enumeration.FileSystemEntry entry)
        {
            return entry.ToFullPath();
        }

        protected override bool ContinueOnError(int error)
        {
            Unreadable?.Add((_lastRecursed, new System.ComponentModel.Win32Exception(error).Message));
            _lastRecursed = null;
            return true;
        }
    }
#endif

    private static void DisplayInfo2(Info2 info, bool q, string dt)
    {
        if (q == false)
        {
            Log.Information("Source file: {SourceName}", info.SourceName);

            Console.WriteLine();
            Log.Information("Version: {Version}", info.Version);

            Console.WriteLine();
            Log.Information("File records");
        }

        foreach (var infoFileRecord in info.FileRecords)
        {
            var fn = infoFileRecord.FileNameAscii;
            if (infoFileRecord.FileNameUnicode.IsNullOrEmpty() == false)
            {
                fn = infoFileRecord.FileNameUnicode;
            }

            var csv = new CsvOut
            {
                FileSize = infoFileRecord.FileSize,
                FileName = fn,
                SourceName = info.SourceName,
                DeletedOn = infoFileRecord.DeletedOn.ToUniversalTime()
                    .ToString(dt),
                FileType = "INFO2",
                Removed = infoFileRecord.Removed
            };
            _csvOuts.Add(csv);

            if (q)
            {
                continue;
            }

            Log.Information("Index: {Index}", infoFileRecord.Index);
            Log.Information("Drive #: {DriveNumber}", infoFileRecord.DriveNumber);
            Log.Information("File size: {FileSize:N0} ({Size})", infoFileRecord.FileSize, BytesToString(infoFileRecord.FileSize));

            Log.Information("File name: {Fn}", fn);

            Log.Information("Deleted on: {DeletedOn}", infoFileRecord.DeletedOn);

            if (infoFileRecord.Removed)
            {
                Log.Information("Removed: no longer in the Recycle Bin (restored, or the Recycle Bin was emptied)");
            }

            Console.WriteLine();
        }
    }

    private static void DisplayDollarI(DollarI di, string dt, bool q)
    {
        var csv = new CsvOut
        {
            FileSize = di.FileSize,
            FileName = di.Filename,
            SourceName = di.SourceName,
            DeletedOn = di.DeletedOn.ToUniversalTime().ToString(dt),
            FileType = "$I"
        };

        _csvOuts.Add(csv);


        foreach (var diDirectoryFile in di.DirectoryFiles)
        {
            csv = new CsvOut
            {
                FileSize = diDirectoryFile.FileSize,
                FileName = diDirectoryFile.FileName,
                SourceName = di.SourceName,
                DeletedOn = di.DeletedOn.ToUniversalTime().ToString(dt),
                FileType = "$I"
            };

            _csvOuts.Add(csv);
        }

        if (q)
        {
            return;
        }

        Log.Information("Source file: {SourceName}", di.SourceName);

        var os = "Pre-Windows 10";

        if (di.Format == 2)
        {
            os = "Windows 10/11";
        }

        Console.WriteLine();
        Log.Information("Version: {Format} ({Os})", di.Format, os);

        Log.Information("File size: {FileSize:N0} ({Size})", di.FileSize, BytesToString(di.FileSize));
        Log.Information("File name: {Filename}", di.Filename);
        Log.Information(
            "Deleted on: {DeletedOn}", di.DeletedOn);

        if (di.DirectoryFiles.Count > 0)
        {
            Console.WriteLine();
            Log.Information("Subfiles in {Filename}", di.Filename);
        }

        foreach (var diDirectoryFile in di.DirectoryFiles)
        {
            Log.Information("File name: {FileName} Size: {FileSize:N0} ({Size})", diDirectoryFile.FileName, diDirectoryFile.FileSize, BytesToString(diDirectoryFile.FileSize));
        }
    }

    class DateTimeOffsetFormatter : IFormatProvider, ICustomFormatter
    {
        private readonly IFormatProvider _innerFormatProvider;

        public DateTimeOffsetFormatter(IFormatProvider innerFormatProvider)
        {
            _innerFormatProvider = innerFormatProvider;
        }

        public object GetFormat(Type formatType)
        {
            return formatType == typeof(ICustomFormatter) ? this : _innerFormatProvider.GetFormat(formatType);
        }

        public string Format(string format, object arg, IFormatProvider formatProvider)
        {
            if (arg is DateTimeOffset)
            {
                //an explicit format (the log templates' {Timestamp:HH:mm:ss.fff}) wins over --dt
                var size = (DateTimeOffset)arg;
                return size.ToString(string.IsNullOrEmpty(format) ? ActiveDateTimeFormat : format);
            }

            var formattable = arg as IFormattable;
            if (formattable != null)
            {
                return formattable.ToString(format, _innerFormatProvider);
            }

            return arg.ToString();
        }
    }

    private static bool IsAdministrator()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return true;
        }

        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
    
    private class CustomHelpAction : SynchronousCommandLineAction
    {
        private readonly HelpAction _defaultHelp;

        public CustomHelpAction(HelpAction action)
        {
            _defaultHelp = action;
        }

        public override int Invoke(ParseResult parseResult)
        {
            var result = _defaultHelp.Invoke(parseResult);

            Log.Warning("Either -f or -d is required. Exiting");

            return result;
        }
    }
}
