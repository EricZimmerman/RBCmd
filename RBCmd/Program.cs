using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using Alphaleonis.Win32.Filesystem;
using CsvHelper;
using Exceptionless;
using Fclp;
using Fclp.Internals.Extensions;
using NLog;
using NLog.Config;
using NLog.Targets;
using RecycleBin;
using Directory = Alphaleonis.Win32.Filesystem.Directory;
using File = Alphaleonis.Win32.Filesystem.File;
using Path = Alphaleonis.Win32.Filesystem.Path;

namespace RBCmd
{
    public class Program
    {
        private static Logger _logger;

        private static readonly string _preciseTimeFormat = "yyyy-MM-dd HH:mm:ss.fffffff";

        private static FluentCommandLineParser<ApplicationArguments> _fluentCommandLineParser;

        private static List<string> _failedFiles;
        private static List<CsvOut> _csvOuts;

        private static void Main(string[] args)
        {
            ExceptionlessClient.Default.Startup("IudF6lFjzvdMldPtlYyPmSMHnSEL89n2WmYbCHoy");

            SetupNLog();

            _logger = LogManager.GetCurrentClassLogger();

            _fluentCommandLineParser = new FluentCommandLineParser<ApplicationArguments>
            {
                IsCaseSensitive = false
            };

            _fluentCommandLineParser.Setup(arg => arg.File)
                .As('f')
                .WithDescription("File to process. Either this or -d is required");

            _fluentCommandLineParser.Setup(arg => arg.Directory)
                .As('d')
                .WithDescription("Directory to recursively process. Either this or -f is required");

            _fluentCommandLineParser.Setup(arg => arg.CsvDirectory)
                .As("csv")
                .WithDescription(
                    "Directory to save CSV formatted results to. Be sure to include the full path in double quotes");

            _fluentCommandLineParser.Setup(arg => arg.CsvName)
                .As("csvf")
                .WithDescription("File name to save CSV formatted results to. When present, overrides default name\r\n");


            _fluentCommandLineParser.Setup(arg => arg.Quiet)
                .As('q')
                .WithDescription(
                    "Only show the filename being processed vs all output. Useful to speed up exporting to json and/or csv\r\n")
                .SetDefault(false);

            _fluentCommandLineParser.Setup(arg => arg.DateTimeFormat)
                .As("dt")
                .WithDescription(
                    "The custom date/time format to use when displaying time stamps. See https://goo.gl/CNVq0k for options. Default is: yyyy-MM-dd HH:mm:ss")
                .SetDefault(_preciseTimeFormat);

            var header =
                $"RBCmd version {Assembly.GetExecutingAssembly().GetName().Version}" +
                "\r\n\r\nAuthor: Eric Zimmerman (saericzimmerman@gmail.com)" +
                "\r\nhttps://github.com/EricZimmerman/RBCmd";


            var footer = @"Examples: RBCmd.exe -f ""C:\Temp\INFO2""" + "\r\n\t " +
                         @" RBCmd.exe -f ""C:\Temp\$I3VPA17"" --csv ""D:\csvOutput"" " + "\r\n\t " +
                         @" RBCmd.exe -d ""C:\Temp"" --csv ""c:\temp"" " + "\r\n\t " +
                         "\r\n\t" +
                         "  Short options (single letter) are prefixed with a single dash. Long commands are prefixed with two dashes\r\n";

            _fluentCommandLineParser.SetupHelp("?", "help")
                .WithHeader(header)
                .Callback(text => _logger.Info(text + "\r\n" + footer));

            var result = _fluentCommandLineParser.Parse(args);

            if (result.HelpCalled)
            {
                return;
            }

            if (result.HasErrors)
            {
                _logger.Error("");
                _logger.Error(result.ErrorText);

                _fluentCommandLineParser.HelpOption.ShowHelp(_fluentCommandLineParser.Options);

                return;
            }

            if (_fluentCommandLineParser.Object.File.IsNullOrEmpty() &&
                _fluentCommandLineParser.Object.Directory.IsNullOrEmpty())
            {
                _fluentCommandLineParser.HelpOption.ShowHelp(_fluentCommandLineParser.Options);

                _logger.Warn("Either -f or -d is required. Exiting");
                return;
            }

            if (_fluentCommandLineParser.Object.File.IsNullOrEmpty() == false &&
                !File.Exists(_fluentCommandLineParser.Object.File))
            {
                _logger.Warn($"File '{_fluentCommandLineParser.Object.File}' not found. Exiting");
                return;
            }

            if (_fluentCommandLineParser.Object.Directory.IsNullOrEmpty() == false &&
                !Directory.Exists(_fluentCommandLineParser.Object.Directory))
            {
                _logger.Warn($"Directory '{_fluentCommandLineParser.Object.Directory}' not found. Exiting");
                return;
            }

            _logger.Info(header);
            _logger.Info("");
            _logger.Info($"Command line: {string.Join(" ", Environment.GetCommandLineArgs().Skip(1))}\r\n");

            if (IsAdministrator() == false)
            {
                _logger.Fatal("Warning: Administrator privileges not found!\r\n");
            }

            _csvOuts = new List<CsvOut>();
            _failedFiles = new List<string>();

            var files = new List<string>();

            var sw = new Stopwatch();
            sw.Start();

            if (_fluentCommandLineParser.Object.File?.Length > 0)
            {
              
                files.Add(_fluentCommandLineParser.Object.File);
            }
            else
            {
                if (_fluentCommandLineParser.Object.Quiet)
                {
                    _logger.Info("");
                }

                files = GetRecycleBinFiles(_fluentCommandLineParser.Object.Directory);
            }

            foreach (var file in files)
            {
                ProcessFile(file);
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

            if (_fluentCommandLineParser.Object.CsvDirectory.IsNullOrEmpty() == false && files.Count > 0)
            {
                if (Directory.Exists(_fluentCommandLineParser.Object.CsvDirectory) == false)
                {
                    _logger.Warn($"'{_fluentCommandLineParser.Object.CsvDirectory} does not exist. Creating...'");
                    Directory.CreateDirectory(_fluentCommandLineParser.Object.CsvDirectory);
                }

                var outName = $"{DateTimeOffset.Now:yyyyMMddHHmmss}_RBCmd_Output.csv";

                if (_fluentCommandLineParser.Object.CsvName.IsNullOrEmpty() == false)
                {
                    outName = Path.GetFileName(_fluentCommandLineParser.Object.CsvName);
                }

                var outFile = Path.Combine(_fluentCommandLineParser.Object.CsvDirectory, outName);

                _fluentCommandLineParser.Object.CsvDirectory =
                    Path.GetFullPath(outFile);
                _logger.Warn(
                    $"CSV output will be saved to '{Path.GetFullPath(outFile)}'");

                try
                {
                    var sw1 = new StreamWriter(outFile);
                    var csv = new CsvWriter(sw1);

                    csv.WriteHeader(typeof(CsvOut));
                    csv.NextRecord();

                    foreach (var csvOut in _csvOuts)
                    {
                        csv.WriteRecord(csvOut);
                        csv.NextRecord();
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

        private static void ProcessFile(string file)
        {
            try
            {
                var raw = File.ReadAllBytes(file);

                if (raw[0] == 1 || raw[0] == 2)
                {
                    var di = new DollarI(raw, file);

                    DisplayDollarI(di);
                }
                else if (raw[0] == 5)
                {
                    var info = new Info2(raw, file);

                    DisplayInfo2(info);
                }
                else
                {
                    _logger.Warn(
                        $"Unknown header '0x{raw[0]:X}! Send file to saericzimmerman@gmail.com so support can be added");
                    _failedFiles.Add(file);
                }

                if (_fluentCommandLineParser.Object.Quiet == false)
                {
                    _logger.Info("");
                }
            }
            catch (UnauthorizedAccessException ua)
            {
                _logger.Error(
                    $"Unable to access '{_fluentCommandLineParser.Object.File}'. Are you running as an administrator? Error: {ua.Message}");
                _failedFiles.Add(file);
            }
            catch (Exception ex)
            {
                _failedFiles.Add(file);
                _logger.Error(
                    $"Error processing file '{_fluentCommandLineParser.Object.File}' Please send it to saericzimmerman@gmail.com. Error: {ex.Message}");
            }
        }

        private static List<string> GetRecycleBinFiles(string dir)
        {
            var files = new List<string>();

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
                DirectoryEnumerationOptions.Files | DirectoryEnumerationOptions.Recursive |
                DirectoryEnumerationOptions.SkipReparsePoints;

            files.AddRange(Directory.EnumerateFileSystemEntryInfos<string>(dir, dirEnumOptions, filters));

            return files;
        }

        private static void DisplayInfo2(Info2 info)
        {
            if (_fluentCommandLineParser.Object.Quiet == false)
            {
                _logger.Warn($"Source file: {info.SourceName}");

                _logger.Info("");
                _logger.Info($"Version: {info.Version}");
                //_logger.Info($"File Entry Size: 0x{info.FileEntrySize:X}");
//            _logger.Info($"Unknown 1: 0x{info.Unknown1:X}");
//            _logger.Info($"Unknown 2: 0x{info.Unknown2:X}");
//            _logger.Info($"Unknown 3: 0x{info.Unknown3:X}");

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
                        .ToString(_fluentCommandLineParser.Object.DateTimeFormat),
                    FileType = "INFO2"
                };


                _csvOuts.Add(csv);

                if (_fluentCommandLineParser.Object.Quiet)
                {
                    continue;
                }

                _logger.Info($"Index: {infoFileRecord.Index}");
                _logger.Info($"Drive #: {infoFileRecord.DriveNumber}");
                _logger.Info($"File size: {infoFileRecord.FileSize} ({BytesToString(infoFileRecord.FileSize)})");

                _logger.Info($"File name: {fn}");

                _logger.Fatal(
                    $"Deleted on: {infoFileRecord.DeletedOn.ToUniversalTime().ToString(_fluentCommandLineParser.Object.DateTimeFormat)}");

                _logger.Info("");
            }
        }

        private static void DisplayDollarI(DollarI di)
        {
            var csv = new CsvOut
            {
                FileSize = di.FileSize,
                FileName = di.Filename,
                SourceName = di.SourceName,
                DeletedOn = di.DeletedOn.ToUniversalTime().ToString(_fluentCommandLineParser.Object.DateTimeFormat),
                FileType = "$I"
            };

            _csvOuts.Add(csv);

            if (_fluentCommandLineParser.Object.Quiet)
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
                $"Deleted on: {di.DeletedOn.ToUniversalTime().ToString(_fluentCommandLineParser.Object.DateTimeFormat)}");
        }

        private static void SetupNLog()
        {
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
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    internal class ApplicationArguments
    {
        public string File { get; set; }
        public string Directory { get; set; }

       // public string JsonDirectory { get; set; }

        public string CsvDirectory { get; set; }
        public string CsvName { get; set; }

        public string DateTimeFormat { get; set; }

        public bool Quiet { get; set; }
    }
}