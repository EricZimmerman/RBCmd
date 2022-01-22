using System.Diagnostics;
using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace RecycleBin.Test;

public class RecycleBinTest
{
    public static string BasePath = @"..\..\TestFiles";

    [Test]
    public void Info2Test()
    {
        var infoPath = Path.Combine(BasePath, "WinXp - mueller1", "INFO2");

        var i2 = new Info2(File.ReadAllBytes(infoPath), infoPath);

        i2.Version.Should().Be(5);
        i2.FileEntrySize.Should().Be(800);
        i2.Unknown3.Should().Be(0);

        foreach (var i2FileRecord in i2.FileRecords)
        {
            i2FileRecord.FileNameAscii.Should().NotBeNullOrEmpty();
            i2FileRecord.FileNameUnicode.Should().NotBeNullOrEmpty();

            Debug.WriteLine(i2FileRecord);
        }
    }

    [Test]
    public void DollarI()
    {
        var filePath = Path.Combine(BasePath, "Win8.1 - Donald Blake");

        foreach (var file in Directory.GetFiles(filePath, "$*"))
        {
            var raw = File.ReadAllBytes(file);

            var d1 = new DollarI(raw, file);

            d1.Filename.Should().NotBeNullOrEmpty();
            d1.FileSize.Should().BeGreaterThan(0);
            d1.SourceName.Should().NotBeNullOrEmpty();


            Debug.WriteLine(d1);
        }

        filePath = Path.Combine(BasePath, "Win7 - nfury");

        foreach (var file in Directory.GetFiles(filePath, "$*"))
        {
            var raw = File.ReadAllBytes(file);

            var d1 = new DollarI(raw, file);

            d1.Filename.Should().NotBeNullOrEmpty();
            d1.FileSize.Should().BeGreaterThan(0);
            d1.SourceName.Should().NotBeNullOrEmpty();

            Debug.WriteLine(d1);
        }

        filePath = Path.Combine(BasePath, "Win7 - nromanoff");

        foreach (var file in Directory.GetFiles(filePath, "$*"))
        {
            var raw = File.ReadAllBytes(file);

            var d1 = new DollarI(raw, file);

            d1.Filename.Should().NotBeNullOrEmpty();
            d1.FileSize.Should().BeGreaterThan(0);
            d1.SourceName.Should().NotBeNullOrEmpty();

            Debug.WriteLine(d1);
        }

        filePath = Path.Combine(BasePath, "Win10 - DEFCON 2018 Desktop");

        foreach (var file in Directory.GetFiles(filePath, "$*"))
        {
            var raw = File.ReadAllBytes(file);

            var d1 = new DollarI(raw, file);

            d1.Filename.Should().NotBeNullOrEmpty();
            d1.FileSize.Should().BeGreaterThan(0);
            d1.SourceName.Should().NotBeNullOrEmpty();

            Debug.WriteLine(d1);
        }
    }
}