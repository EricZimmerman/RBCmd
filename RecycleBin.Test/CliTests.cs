using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace RecycleBin.Test;

/// <summary>
///     Runs the built RBCmd.dll (copied next to the tests by the project reference) as a separate process
/// </summary>
[TestFixture]
public class CliTests
{
    private string _dir;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rbcmd-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_dir, true);
    }

    private static (int ExitCode, string Output) Run(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(TestContext.CurrentContext.TestDirectory, "RBCmd.dll"));
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Close();

        process.WaitForExit(60_000).Should().BeTrue("RBCmd should finish");

        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    private string[] CsvLines() => File.ReadAllLines(Path.Combine(_dir, "csv", "out.csv"));

    [Test]
    public void DirectoryFindsEverySample()
    {
        var (exitCode, output) = Run("-d", RecycleBinTest.Sample(), "--csv", Path.Combine(_dir, "csv"), "--csvf",
            "out.csv", "-q");

        exitCode.Should().Be(0);
        output.Should().Contain("Found 12 files");
        var lines = CsvLines();
        lines[0].Should().Be("SourceName,FileType,FileName,FileSize,DeletedOn,Removed");
        lines.Should().HaveCount(13);
        lines.Should().Contain(t => t.EndsWith(@",$I,C:\temp,14155310,2018-08-07 19:54:58,False"));
        lines.Should().Contain(t => t.EndsWith(@",INFO2,Z:\WINDOWS\explorer.exe,1005568,2008-01-30 15:13:03,False"));
    }

    [Test]
    public void RemovedInfo2RecordsAreFlagged()
    {
        var (exitCode, output) = Run("-f", RecycleBinTest.Sample("WinXp - mueller1", "INFO2_MultiFiles"), "--csv",
            Path.Combine(_dir, "csv"), "--csvf", "out.csv");

        exitCode.Should().Be(0);
        output.Should().Contain("Removed: no longer in the Recycle Bin");
        CsvLines().Skip(1).Select(t => t.Split(',').Last()).Should().Equal("False", "True", "True", "False");
    }

    [Test]
    public void DeletedDirectoryContentsAreExported()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "bin", "$R2JRX90"));
        File.WriteAllBytes(Path.Combine(_dir, "bin", "$R2JRX90", "a.txt"), new byte[10]);
        File.Copy(RecycleBinTest.Sample("Win10 - DEFCON 2018 Desktop", "$I2JRX90"),
            Path.Combine(_dir, "bin", "$I2JRX90"));

        var (exitCode, _) = Run("-d", Path.Combine(_dir, "bin"), "--csv", Path.Combine(_dir, "csv"), "--csvf",
            "out.csv", "-q");

        exitCode.Should().Be(0);
        CsvLines().Should().Contain(t => t.Contains(@",$I,C:\temp\a.txt,10,"));
    }

    [Test]
    public void TimesAreUtcOnTheConsoleAndInTheCsv()
    {
        var (_, output) = Run("-f", RecycleBinTest.Sample("Win10 - DEFCON 2018 Desktop", "$I2JRX90"));

        output.Should().Contain("Deleted on: 2018-08-07 19:54:58");
    }

    [Test]
    public void ConsoleLog()
    {
        //Issues #259: --co (on by default) saves the console messages to <yyyyMMddHHmmss>_RBCmd_<version>_ConsoleLog.txt in the --csv directory
        var csv = Path.Combine(_dir, "csv");

        var (exitCode, output) = Run("-f", RecycleBinTest.Sample("Win10 - DEFCON 2018 Desktop", "$I2JRX90"), "--csv", csv);
        exitCode.Should().Be(0, output);
        var log = Directory.GetFiles(csv, "*_ConsoleLog.txt").Should().ContainSingle().Subject;
        Path.GetFileName(log).Should().MatchRegex(@"^\d{14}_RBCmd_\d+\.\d+\.\d+_ConsoleLog\.txt$");
        output.Should().Contain("Console log: " + log);
        //the same time stamp as the CSV
        var csvFile = Directory.GetFiles(csv, "*_RBCmd_Output.csv").Should().ContainSingle().Subject;
        Path.GetFileName(log)[..14].Should().Be(Path.GetFileName(csvFile)[..14]);
        var text = File.ReadAllText(log);
        text.Should().MatchRegex(@"\[\d\d:\d\d:\d\d\.\d{3} INF\] RBCmd version ");
        text.Should().Contain("Command line: -f ");
        text.Should().Contain("CSV output will be saved to ");
        text.Should().Contain("Deleted on: 2018-08-07 19:54:58");
    }

    [Test]
    public void NoConsoleLogWithCoFalse()
    {
        var csv = Path.Combine(_dir, "csv");

        var (exitCode, output) = Run("-f", RecycleBinTest.Sample("Win10 - DEFCON 2018 Desktop", "$I2JRX90"), "--csv", csv, "--co", "false");
        exitCode.Should().Be(0, output);
        Directory.GetFiles(csv, "*_ConsoleLog.txt").Should().BeEmpty();
        output.Should().NotContain("Console log:");
    }

    [Test]
    public void NoConsoleLogWithoutOutputDirectory()
    {
        //nothing to put it beside, and no warning: --co is on by default
        var (exitCode, output) = Run("-f", RecycleBinTest.Sample("Win10 - DEFCON 2018 Desktop", "$I2JRX90"));
        exitCode.Should().Be(0, output);
        output.Should().NotContain("--co:").And.NotContain("Console log:");
    }

    [Test]
    public void MissingArgumentsExitWithOne()
    {
        var (exitCode, output) = Run();

        exitCode.Should().Be(1);
        output.Should().Contain("Either -f or -d is required");
    }

    [Test]
    public void MissingFileOrDirectoryExitsWithOne()
    {
        Run("-f", Path.Combine(_dir, "nope")).ExitCode.Should().Be(1);
        Run("-d", Path.Combine(_dir, "nope")).ExitCode.Should().Be(1);
    }

    [TestCase(0)]
    [TestCase(10)]
    public void UnusableFileExitsWithZero(int size)
    {
        // A damaged file is not a failure of the tool: it is listed under "Failed files", the exit code is 0
        var file = Path.Combine(_dir, "$IBROKEN");
        File.WriteAllBytes(file, Enumerable.Repeat((byte)0x99, size).ToArray());

        var (exitCode, output) = Run("-f", file);

        exitCode.Should().Be(0);
        output.Should().Contain("Failed files");
    }

    [Test]
    public void LockedFileExitsWithOne()
    {
        var file = Path.Combine(_dir, "$ILOCKED");
        File.Copy(RecycleBinTest.Sample("Win10 - DEFCON 2018 Desktop", "$I2JRX90"), file);

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var (exitCode, output) = Run("-f", file);

            exitCode.Should().Be(1);
            output.Should().Contain("Unable to read");
        }
    }

    [Test]
    public void UnreadableFolderExitsWithOne()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Deny ACEs are Windows only");
            return;
        }

        // A folder under -d that can't be read is reported, the rest is processed, and the exit code is 1
        File.Copy(RecycleBinTest.Sample("Win10 - DEFCON 2018 Desktop", "$I2JRX90"), Path.Combine(_dir, "$I2JRX90"));
        var denied = Path.Combine(_dir, "denied");
        Directory.CreateDirectory(denied);
        File.Copy(RecycleBinTest.Sample("Win10 - DEFCON 2018 Desktop", "$I2JRX90"), Path.Combine(denied, "$IHIDDEN"));

        using (new DeniedFolder(denied))
        {
            var (exitCode, output) = Run("-d", _dir, "--csv", Path.Combine(_dir, "csv"), "--csvf", "out.csv", "-q");

            exitCode.Should().Be(1);
            output.Should().Contain("Unable to read folder").And.Contain("denied");
            output.Should().Contain("Found 1 files");
        }

        CsvLines().Should().HaveCount(2);
    }

    [Test]
    public void TruncatedDollarIIsReported()
    {
        // Format 1 with a header but no name: unusable data is reported, not a crash
        var file = Path.Combine(_dir, "$ISHORT");
        File.WriteAllBytes(file, File.ReadAllBytes(RecycleBinTest.Sample("Win7 - nfury", "$I6SODDB")).Take(20).ToArray());

        var (exitCode, output) = Run("-f", file);

        exitCode.Should().Be(0);
        output.Should().Contain("smaller than the 24 byte $I header");
    }

    [Test]
    public void PartlyParsedFileExitsWithZero()
    {
        // Format 2 with a header but too short for the name length: parsed with a parse error, listed as a failed file, exit code 0
        var file = Path.Combine(_dir, "$IPART");
        File.WriteAllBytes(file,
            File.ReadAllBytes(RecycleBinTest.Sample("Win10 - DEFCON 2018 Desktop", "$I2JRX90")).Take(26).ToArray());

        var (exitCode, output) = Run("-f", file);

        exitCode.Should().Be(0);
        output.Should().Contain("did not parse completely");
    }

    [Test]
    public void FailedExportExitsWithOne()
    {
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "");

        Run("-f", RecycleBinTest.Sample("WinXp - mueller1", "INFO2"), "--csv", blocker, "-q").ExitCode.Should().Be(1);
    }
}
