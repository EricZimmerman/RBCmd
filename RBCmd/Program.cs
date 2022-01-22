using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.NamingConventionBinder;
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
#if NET462
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
    private static List<CsvOut> _csvOuts;
        
    private static string Header =
        $"RBCmd version {Assembly.GetExecutingAssembly().GetName().Version}" +
        "\r\n\r\nAuthor: Eric Zimmerman (saericzimmerman@gmail.com)" +
        "\r\nhttps://github.com/EricZimmerman/RBCmd";


    private static string Footer = @"Examples: RBCmd.exe -f ""C:\Temp\INFO2""" + "\r\n\t " +
                                   @"   RBCmd.exe -f ""C:\Temp\$I3VPA17"" --csv ""D:\csvOutput"" " + "\r\n\t " +
                                   @"   RBCmd.exe -d ""C:\Temp"" --csv ""c:\temp"" " + "\r\n\t " +
                                   "\r\n\t" +
                                   "    Short options (single letter) are prefixed with a single dash. Long commands are prefixed with two dashes\r\n";

    private static RootCommand _rootCommand;

    private static async Task Main(string[] args)
    {
        ExceptionlessClient.Default.Startup("IudF6lFjzvdMldPtlYyPmSMHnSEL89n2WmYbCHoy");

        _rootCommand = new RootCommand
        {
            new Option<string>(
                "-f",
                "File to process ($MFT | $J | $Boot | $SDS). Required"),

            new Option<string>(
                "-m",
                "$MFT file to use when -f points to a $J file (Use this to resolve parent path in $J CSV output).\r\n"),

            new Option<string>(
                "--json",
                "Directory to save JSON formatted results to. This or --csv required unless --de or --body is specified"),

            new Option<string>(
                "--jsonf",
                "File name to save JSON formatted results to. When present, overrides default name"),

            new Option<string>(
                "--csv",
                "Directory to save CSV formatted results to. This or --json required unless --de or --body is specified"),

            new Option<string>(
                "--csvf",
                "File name to save CSV formatted results to. When present, overrides default name\r\n"),
            new Option<bool>(
                "--debug",
                () => false,
                "Show debug information during processing"),

            new Option<bool>(
                "--trace",
                () => false,
                "Show trace information during processing")
        };
            
        _rootCommand.Description = Header + "\r\n\r\n" + Footer;

        _rootCommand.Handler = CommandHandler.Create(DoWork);

        await _rootCommand.InvokeAsync(args);

        Log.CloseAndFlush();
    }

    private static void DoWork(string f, string d, string csv, string csvf, bool q, string dt, bool debug, bool trace)
    {
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
            .WriteTo.Console(outputTemplate: template)
            .MinimumLevel.ControlledBy(levelSwitch);

        Log.Logger = conf.CreateLogger();

        if (f.IsNullOrEmpty() &&
            d.IsNullOrEmpty())
        {
            var helpBld = new HelpBuilder(LocalizationResources.Instance, Console.WindowWidth);
            var hc = new HelpContext(helpBld, _rootCommand, Console.Out);

            helpBld.Write(hc);

            Log.Warning("Either -f or -d is required. Exiting");
            return;
        }

        if (f.IsNullOrEmpty() == false &&
            !File.Exists(f))
        {
            Log.Warning("File {F} not found. Exiting",f);
            return;
        }

        if (d.IsNullOrEmpty() == false &&
            !Directory.Exists(d))
        {
            Log.Warning("Directory {D} not found. Exiting",d);
            return;
        }

        Log.Information("{Header}",Header);
        Console.WriteLine();
        Log.Information("Command line: {Args}",string.Join(" ", Environment.GetCommandLineArgs().Skip(1)));
        Console.WriteLine();

        if (IsAdministrator() == false)
        {
            Log.Warning("Warning: Administrator privileges not found!");
            Console.WriteLine();
        }
        
        _csvOuts = new List<CsvOut>();
        _failedFiles = new List<string>();

        var files = new List<string>();

        var sw = new Stopwatch();
        sw.Start();

        if (f?.Length > 0)
        {
            files.Add(f);
        }
        else
        {
            if (q)
            {
                Console.WriteLine();
            }

            files = GetRecycleBinFiles(d);
        }

        foreach (var file in files)
        {
            ProcessFile(file,q,dt);
        }
            
        sw.Stop();

        Log.Information(
            "Processed {FailedFilesCount:N0} out of {Count:N0} files in {TotalSeconds:N4} seconds",files.Count - _failedFiles.Count,files.Count,sw.Elapsed.TotalSeconds);
        Console.WriteLine();

        if (_failedFiles.Count > 0)
        {
            Console.WriteLine();
            Log.Information("Failed files");
            foreach (var failedFile in _failedFiles)
            {
                Log.Information("  {FailedFile}",failedFile);
            }
        }

        if (csv.IsNullOrEmpty() == false && files.Count > 0)
        {
            if (Directory.Exists(csv) == false)
            {
                Log.Information("{Csv} does not exist. Creating...",csv);
                Directory.CreateDirectory(csv);
            }

            var outName = $"{DateTimeOffset.Now:yyyyMMddHHmmss}_RBCmd_Output.csv";

            if (csvf.IsNullOrEmpty() == false)
            {
                outName = Path.GetFileName(csvf);
            }

             
                
            var outFile = Path.Combine(csv, outName);

            outFile =
                Path.GetFullPath(outFile);
              
            Log.Warning("CSV output will be saved to {Path}",Path.GetFullPath(outFile));

            try
            {
                var sw1 = new StreamWriter(outFile);
                var csvWriter = new CsvWriter(sw1,CultureInfo.InvariantCulture);

                csvWriter.WriteHeader(typeof(CsvOut));
                csvWriter.NextRecord();

                foreach (var csvOut in _csvOuts)
                {
                    csvWriter.WriteRecord(csvOut);
                    csvWriter.NextRecord();
                }

                sw1.Flush();
                sw1.Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "Unable to open {OutFile} for writing. CSV export canceled. Error: {Message}",outFile,ex.Message);
            }
        }
    }

    private static string BytesToString(long byteCount)
    {
        string[] suf = {"B", "KB", "MB", "GB", "TB", "PB", "EB"}; //Longs run out around EB
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
            var raw = File.ReadAllBytes(file);

            if (raw[0] == 1 || raw[0] == 2)
            {
                var di = new DollarI(raw, file);

                DisplayDollarI(di,dt,q);
            }
            else if (raw[0] == 5)
            {
                var info = new Info2(raw, file);

                DisplayInfo2(info,q,dt);
            }
            else
            {
                Log.Warning(
                    "Unknown header 0x{Raw:X}! Send file to {Email} so support can be added",raw[0],"saericzimmerman@gmail.com");
                _failedFiles.Add(file);
            }

            if (q == false)
            {
                Console.WriteLine();
            }
        }
        catch (UnauthorizedAccessException ua)
        {
            Log.Error(ua,
                "Unable to access {File}. Are you running as an administrator? Error: {Message}",file,ua.Message);
            _failedFiles.Add(file);
        }
        catch (Exception ex)
        {
            _failedFiles.Add(file);
            Log.Error(ex,
                "Error processing file {File} Please send it to {Email}. Error: {Message}",file,"saericzimmerman@gmail.com",ex.Message);
        }
    }

    private static List<string> GetRecycleBinFiles(string dir)
    {
        var files = new List<string>();

#if NET6_0
        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = true,
            AttributesToSkip = 0
        };
        var files2 =  Directory.EnumerateFileSystemEntries(dir, "INFO2",enumerationOptions);
        
        files.AddRange(files2);
        
        files2 =  Directory.EnumerateFileSystemEntries(dir, "$I*",enumerationOptions);
        
        files.AddRange(files2);
        
#elif NET462
  Privilege[] privs = {Privilege.EnableDelegation, Privilege.Impersonate, Privilege.Tcb};
        using (new PrivilegeEnabler(Privilege.Backup, privs))
        {
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
                    Log.Error("Error accessing {PathProcessed}. Error: {ErrorMessage}",pathProcessed,errorCode);

                    // Return true to continue, false to throw the Exception.
                    return true;
                },

                // Filter to in-/exclude file system entries during the enumeration.
                InclusionFilter = entryInfo =>
                {
                    if (entryInfo.FileName == "INFO2" || entryInfo.FileName.StartsWith("$I"))
                    {
                        Log.Debug("Found match: {FullPath}",entryInfo.FullPath);
                        return true;
                    }

                    return false;
                }
            };

            var dirEnumOptions =
                DirectoryEnumerationOptions.Files | DirectoryEnumerationOptions.Recursive | DirectoryEnumerationOptions.ContinueOnException |
                DirectoryEnumerationOptions.SkipReparsePoints;

            files.AddRange(Directory.EnumerateFileSystemEntryInfos<string>(dir, dirEnumOptions, filters).Where(File.Exists));
            }
#endif

        return files;
    }

    private static void DisplayInfo2(Info2 info, bool q, string dt)
    {
        if (q == false)
        {
            Log.Information("Source file: {SourceName}",info.SourceName);

            Console.WriteLine();
            Log.Information("Version: {Version}",info.Version);

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
                FileType = "INFO2"
            };
            _csvOuts.Add(csv);

            if (q)
            {
                continue;
            }

            Log.Information("Index: {Index}",infoFileRecord.Index);
            Log.Information("Drive #: {DriveNumber}",infoFileRecord.DriveNumber);
            Log.Information("File size: {FileSize} ({Size})",infoFileRecord.FileSize,BytesToString(infoFileRecord.FileSize));

            Log.Information("File name: {Fn}",fn);

            Log.Information("Deleted on: {DeletedOn}",infoFileRecord.DeletedOn);

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

        Log.Information("Source file: {SourceName}",di.SourceName);

        var os = "Pre-Windows 10";

        if (di.Format == 2)
        {
            os = "Windows 10";
        }

        Console.WriteLine();
        Log.Information("Version: {Format} ({Os})",di.Format,os);

        Log.Information("File size: {FileSize} ({Size})",di.FileSize,BytesToString(di.FileSize));
        Log.Information("File name: {Filename}",di.Filename);
        Log.Information(
            "Deleted on: {DeletedOn}",di.DeletedOn);

        if (di.DirectoryFiles.Count > 0)
        {
            Console.WriteLine();
            Log.Information("Subfiles in {Filename}",di.Filename);
        }

        foreach (var diDirectoryFile in di.DirectoryFiles)
        {
            Log.Information("File name: {FileName} Size: {FileSize} ({Size})",diDirectoryFile.FileName,diDirectoryFile.FileSize,BytesToString(diDirectoryFile.FileSize));
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
}