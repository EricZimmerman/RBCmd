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
using Alphaleonis.Win32.Filesystem;
using Alphaleonis.Win32.Security;

using Exceptionless;

using NLog;
using NLog.Config;
using NLog.Targets;
using RecycleBin;
using ServiceStack;
using CsvWriter = CsvHelper.CsvWriter;
using Directory = Alphaleonis.Win32.Filesystem.Directory;
using File = Alphaleonis.Win32.Filesystem.File;
using Path = Alphaleonis.Win32.Filesystem.Path;

namespace RBCmd
{
    public class Program
    {
        private static Logger _logger;

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

            SetupNLog();

            _logger = LogManager.GetCurrentClassLogger();

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

            };
            
            _rootCommand.Description = Header + "\r\n\r\n" + Footer;

            _rootCommand.Handler = CommandHandler.Create(DoWork);

            await _rootCommand.InvokeAsync(args);

        }

        private static void DoWork(string f, string d, string csv, string csvf, bool q, string dt, bool debug, bool trace)
        {

            if (f.IsNullOrEmpty() &&
                d.IsNullOrEmpty())
            {
                var helpBld = new HelpBuilder(LocalizationResources.Instance, Console.WindowWidth);
                var hc = new HelpContext(helpBld, _rootCommand, Console.Out);

                helpBld.Write(hc);

                _logger.Warn("Either -f or -d is required. Exiting");
                return;
            }

            if (f.IsNullOrEmpty() == false &&
                !File.Exists(f))
            {
                _logger.Warn($"File '{f}' not found. Exiting");
                return;
            }

            if (d.IsNullOrEmpty() == false &&
                !Directory.Exists(d))
            {
                _logger.Warn($"Directory '{d}' not found. Exiting");
                return;
            }

            _logger.Info(Header);
            _logger.Info("");
            _logger.Info($"Command line: {string.Join(" ", Environment.GetCommandLineArgs().Skip(1))}\r\n");

            if (IsAdministrator() == false)
            {
                _logger.Fatal("Warning: Administrator privileges not found!\r\n");
            }

            if (debug)
            {
                foreach (var r in LogManager.Configuration.LoggingRules)
                {
                    r.EnableLoggingForLevel(LogLevel.Debug);
                }

                LogManager.ReconfigExistingLoggers();
                _logger.Debug("Enabled debug messages...");
            }

            if (trace)
            {
                foreach (var r in LogManager.Configuration.LoggingRules)
                {
                    r.EnableLoggingForLevel(LogLevel.Trace);
                }

                LogManager.ReconfigExistingLoggers();
                _logger.Trace("Enabled trace messages...");
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
                    _logger.Info("");
                }

                files = GetRecycleBinFiles(d);
            }

            foreach (var file in files)
            {
                ProcessFile(file,q,dt);
            }
            
            sw.Stop();

            _logger.Info(
                $"Processed {files.Count - _failedFiles.Count:N0} out of {files.Count:N0} files in {sw.Elapsed.TotalSeconds:N4} seconds\r\n");

            if (_failedFiles.Count > 0)
            {
                _logger.Info("");
                _logger.Warn("Failed files");
                foreach (var failedFile in _failedFiles)
                {
                    _logger.Info($"  {failedFile}");
                }
            }

            if (csv.IsNullOrEmpty() == false && files.Count > 0)
            {
                if (Directory.Exists(csv) == false)
                {
                    _logger.Warn($"'{csv} does not exist. Creating...'");
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
              
                _logger.Warn(
                    $"CSV output will be saved to '{Path.GetFullPath(outFile)}'");

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
                    _logger.Error(
                        $"Unable to open '{outFile}' for writing. CSV export canceled. Error: {ex.Message}");
                }
            }
        }
        
           public static string BytesToString(long byteCount)
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
                    _logger.Warn(
                        $"Unknown header '0x{raw[0]:X}! Send file to saericzimmerman@gmail.com so support can be added");
                    _failedFiles.Add(file);
                }

                if (q == false)
                {
                    _logger.Info("");
                }
            }
            catch (UnauthorizedAccessException ua)
            {
                _logger.Error(
                    $"Unable to access '{file}'. Are you running as an administrator? Error: {ua.Message}");
                _failedFiles.Add(file);
            }
            catch (Exception ex)
            {
                _failedFiles.Add(file);
                _logger.Error(
                    $"Error processing file '{file}' Please send it to saericzimmerman@gmail.com. Error: {ex.Message}");
            }
        }

        private static List<string> GetRecycleBinFiles(string dir)
        {
            var files = new List<string>();

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
                        _logger.Error($"Error accessing '{pathProcessed}'. Error: {errorMessage}");

                        // Return true to continue, false to throw the Exception.
                        return true;
                    },

                    // Filter to in-/exclude file system entries during the enumeration.
                    InclusionFilter = entryInfo =>
                    {
                        if (entryInfo.FileName == "INFO2" || entryInfo.FileName.StartsWith("$I"))
                        {
                            _logger.Debug($"Found match: '{entryInfo.FullPath}'");
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

            return files;
        }

        private static void DisplayInfo2(Info2 info, bool q, string dt)
        {
            if (q == false)
            {
                _logger.Warn($"Source file: {info.SourceName}");

                _logger.Info("");
                _logger.Info($"Version: {info.Version}");

                _logger.Info("");
                _logger.Warn("File records");
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

                _logger.Info($"Index: {infoFileRecord.Index}");
                _logger.Info($"Drive #: {infoFileRecord.DriveNumber}");
                _logger.Info($"File size: {infoFileRecord.FileSize} ({BytesToString(infoFileRecord.FileSize)})");

                _logger.Info($"File name: {fn}");

                _logger.Fatal(
                    $"Deleted on: {infoFileRecord.DeletedOn.ToUniversalTime().ToString(dt)}");

                _logger.Info("");
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

            _logger.Warn($"Source file: {di.SourceName}");

            var os = "Pre-Windows 10";

            if (di.Format == 2)
            {
                os = "Windows 10";
            }

            _logger.Info("");
            _logger.Info($"Version: {di.Format} ({os})");

            _logger.Info($"File size: {di.FileSize} ({BytesToString(di.FileSize)})");
            _logger.Info($"File name: {di.Filename}");
            _logger.Fatal(
                $"Deleted on: {di.DeletedOn.ToUniversalTime().ToString(dt)}");

            if (di.DirectoryFiles.Count > 0)
            {
                _logger.Warn($"\r\nSubfiles in '{di.Filename}'");
            }

            foreach (var diDirectoryFile in di.DirectoryFiles)
            {
                _logger.Info($"File name: {diDirectoryFile.FileName} Size: {diDirectoryFile.FileSize} ({BytesToString(diDirectoryFile.FileSize)})");
            }
        }

        private static readonly string BaseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        private static void SetupNLog()
        {
            if (File.Exists( Path.Combine(BaseDirectory,"Nlog.config")))
            {
                return;
            }
            var config = new LoggingConfiguration();
            var loglevel = LogLevel.Info;

            var layout = @"${message}";

            var consoleTarget = new ColoredConsoleTarget();

            config.AddTarget("console", consoleTarget);

            consoleTarget.Layout = layout;

            var rule1 = new LoggingRule("*", loglevel, consoleTarget);
            config.LoggingRules.Add(rule1);

            LogManager.Configuration = config;
        }

        public static bool IsAdministrator()
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

    
}