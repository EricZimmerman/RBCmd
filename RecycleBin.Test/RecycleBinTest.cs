using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace RecycleBin.Test;

/// <summary>
///     Assertions on the real samples in TestFiles/
/// </summary>
[TestFixture]
public class RecycleBinTest
{
    internal static string Sample(params string[] parts) =>
        Path.Combine(new[] { TestContext.CurrentContext.TestDirectory, "TestFiles" }.Concat(parts).ToArray());

    private static DollarI LoadDollarI(params string[] parts)
    {
        var path = Sample(parts);
        return new DollarI(File.ReadAllBytes(path), path);
    }

    private static Info2 LoadInfo2(params string[] parts)
    {
        var path = Sample(parts);
        return new Info2(File.ReadAllBytes(path), path);
    }

    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi, int s, int ms) =>
        new DateTimeOffset(y, mo, d, h, mi, s, ms, TimeSpan.Zero);

    [TestCase("Win10 - DEFCON 2018 Desktop", "$I2JRX90", 2, 14155310, @"C:\temp")]
    [TestCase("Win10 - DEFCON 2018 Desktop", "$IFATB0K", 2, 3276800, @"C:\vss")]
    [TestCase("Win7 - nfury", "$I6SODDB", 1, 102742, @"C:\Windows\System32\dllhost")]
    [TestCase("Win7 - nfury", "$ID5HSV3.exe", 1, 3943584, @"C:\Windows\System32\hythonize.exe")]
    [TestCase("Win7 - nfury", "$IZI9VH9.exe", 1, 2990574, @"C:\Windows\System32\hythonized.exe")]
    [TestCase("Win7 - nromanoff", "$IPFXZSB", 1, 5838271, @"C:\Users\Public\Pictures\Sample Pictures")]
    [TestCase("Win7 - nromanoff", "$ISEBKEC.zip", 1, 13449655,
        @"C:\Users\nromanoff\Documents\New-Site-HQ-And-Landing-Pad.zip")]
    [TestCase("Win8.1 - Donald Blake", "$I3VPA17", 1, 16131820, @"C:\Users\Donald\SkyDrive\Documents\Lean startup")]
    [TestCase("Win8.1 - Donald Blake", "$IEMAANA", 1, 1399035, @"C:\Users\Donald\SkyDrive\Documents\Articles")]
    [TestCase("Win8.1 - Donald Blake", "$IG1VEXX.xls", 1, 16384,
        @"C:\Users\Donald\SkyDrive\Documents\WACC Calc Spreadsheet -SECRET.xls")]
    [TestCase("Win8.1 - Donald Blake", "$ISFWPA9.xls", 1, 16384,
        @"C:\Users\Donald\SkyDrive\Documents\WACC Calc Spreadsheet -SECRET-Bifrost.xls")]
    public void DollarISample(string folder, string file, long format, long size, string name)
    {
        var di = LoadDollarI(folder, file);

        di.Format.Should().Be(format);
        di.FileSize.Should().Be(size);
        di.Filename.Should().Be(name);
        di.SourceName.Should().Be(Sample(folder, file));
        di.DeletedOn.Offset.Should().Be(TimeSpan.Zero);
        di.DirectoryFiles.Should().BeEmpty();
        di.ParseErrors.Should().BeEmpty();
    }

    [Test]
    public void DollarIDeletionTimes()
    {
        LoadDollarI("Win10 - DEFCON 2018 Desktop", "$I2JRX90").DeletedOn.Should()
            .Be(Utc(2018, 8, 7, 19, 54, 58, 705));
        LoadDollarI("Win7 - nfury", "$I6SODDB").DeletedOn.Should().Be(Utc(2012, 4, 6, 18, 42, 4, 143));
    }

    [Test]
    public void Info2Sample()
    {
        var i2 = LoadInfo2("WinXp - mueller1", "INFO2");

        i2.Version.Should().Be(5);
        i2.FileEntrySize.Should().Be(800);
        i2.Unknown3.Should().Be(0);
        i2.ParseErrors.Should().BeEmpty();

        var record = i2.FileRecords.Should().ContainSingle().Subject;
        record.FileNameAscii.Should().Be(@"Z:\WINDOWS\explorer.exe");
        record.FileNameUnicode.Should().Be(@"Z:\WINDOWS\explorer.exe");
        record.Index.Should().Be(1);
        record.DriveNumber.Should().Be(25);
        record.FileSize.Should().Be(1005568);
        record.DeletedOn.Should().Be(Utc(2008, 1, 30, 15, 13, 3, 703));
        record.Removed.Should().BeFalse();
    }

    [Test]
    public void Info2RecordsThatLeftTheRecycleBinAreFlagged()
    {
        var i2 = LoadInfo2("WinXp - mueller1", "INFO2_MultiFiles");

        i2.ParseErrors.Should().BeEmpty();
        i2.FileRecords.Select(t => t.Index).Should().Equal(1, 2, 3, 4);
        i2.FileRecords.Select(t => t.Removed).Should().Equal(false, true, true, false);
        i2.FileRecords.Select(t => t.FileSize).Should().Equal(0, 4096, 4096, 360448);

        // The ANSI path of a removed record has its first byte cleared; the Unicode path is intact
        i2.FileRecords[1].FileNameAscii.Should().BeEmpty();
        i2.FileRecords[1].FileNameUnicode.Should().Be(@"C:\Documents and Settings\test\Desktop\first file.txt");
        i2.FileRecords[3].FileNameAscii.Should().Be(@"C:\Documents and Settings\test\Desktop\Folder2");
    }

    [Test]
    public void DeletedDirectoryListsTheFilesInItsDollarRDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rb-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // $I2JRX90 is a deleted C:\temp; its contents are in $R2JRX90
            Directory.CreateDirectory(Path.Combine(dir, "$R2JRX90", "sub"));
            File.WriteAllBytes(Path.Combine(dir, "$R2JRX90", "a.txt"), new byte[10]);
            File.WriteAllBytes(Path.Combine(dir, "$R2JRX90", "sub", "b.txt"), new byte[25]);
            var dollarI = Path.Combine(dir, "$I2JRX90");
            File.Copy(Sample("Win10 - DEFCON 2018 Desktop", "$I2JRX90"), dollarI);

            var di = new DollarI(File.ReadAllBytes(dollarI), dollarI);

            di.DirectoryFiles.Select(t => (t.FileName, t.FileSize)).Should().BeEquivalentTo(new[]
            {
                (@"C:\temp\a.txt", 10L),
                (@"C:\temp\sub\b.txt", 25L)
            });
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void DeletedFileHasNoDirectoryFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rb-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // $RSEBKEC.zip is a file, not a directory
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "$RSEBKEC.zip"), new byte[10]);
            var dollarI = Path.Combine(dir, "$ISEBKEC.zip");
            File.Copy(Sample("Win7 - nromanoff", "$ISEBKEC.zip"), dollarI);

            new DollarI(File.ReadAllBytes(dollarI), dollarI).DirectoryFiles.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
