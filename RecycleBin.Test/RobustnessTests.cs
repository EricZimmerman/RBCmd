using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using NUnit.Framework;

namespace RecycleBin.Test;

/// <summary>
///     In-memory files that exercise malformed input
/// </summary>
[TestFixture]
public class RobustnessTests
{
    // Not a real path, so no $R directory is looked at
    private const string Name = "memory";

    private static readonly long Time = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc).ToFileTimeUtc();

    private static byte[] DollarI1(string name, long time = 0, int nameBytes = 520)
    {
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes(1L));
        data.AddRange(BitConverter.GetBytes(1234L));
        data.AddRange(BitConverter.GetBytes(time == 0 ? Time : time));
        var nameData = new byte[520];
        Encoding.Unicode.GetBytes(name).CopyTo(nameData, 0);
        data.AddRange(nameData.Take(nameBytes));
        return data.ToArray();
    }

    private static byte[] DollarI2(string name, int? length = null)
    {
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes(2L));
        data.AddRange(BitConverter.GetBytes(1234L));
        data.AddRange(BitConverter.GetBytes(Time));
        data.AddRange(BitConverter.GetBytes(length ?? name.Length + 1));
        data.AddRange(Encoding.Unicode.GetBytes(name + "\0"));
        return data.ToArray();
    }

    private static byte[] Info2(int recordSize, params (string Name, bool Removed)[] records)
    {
        var data = new List<byte>();
        data.AddRange(BitConverter.GetBytes(recordSize == 280 ? 4 : 5));
        data.AddRange(new byte[8]);
        data.AddRange(BitConverter.GetBytes(recordSize));
        data.AddRange(new byte[4]);

        var index = 1;
        foreach (var (name, removed) in records)
        {
            var record = new byte[recordSize];
            Encoding.ASCII.GetBytes(name).CopyTo(record, 0);
            if (removed)
            {
                record[0] = 0;
            }

            BitConverter.GetBytes(index++).CopyTo(record, 260);
            BitConverter.GetBytes(2).CopyTo(record, 264);
            BitConverter.GetBytes(Time).CopyTo(record, 268);
            BitConverter.GetBytes(4096).CopyTo(record, 276);
            if (recordSize >= 800)
            {
                Encoding.Unicode.GetBytes(name).CopyTo(record, 280);
            }

            data.AddRange(record);
        }

        return data.ToArray();
    }

    [Test]
    public void BuiltDollarIMatchesTheWin10Sample()
    {
        var sample = System.IO.File.ReadAllBytes(RecycleBinTest.Sample("Win10 - DEFCON 2018 Desktop", "$IFATB0K"));
        var di = new DollarI(sample, Name);

        DollarI2(di.Filename).Skip(24).Should().Equal(sample.Skip(24));
    }

    [TestCase(0)]
    [TestCase(8)]
    [TestCase(23)]
    public void DollarISmallerThanTheHeaderThrows(int size)
    {
        Action act = () => new DollarI(new byte[size], Name);

        act.Should().Throw<RecycleBinParseException>().Where(e => e.Field == "Header");
    }

    [TestCase(0L)]
    [TestCase(3L)]
    [TestCase(-1L)]
    public void UnsupportedDollarIFormatThrows(long format)
    {
        var data = DollarI2("C:\\a.txt");
        BitConverter.GetBytes(format).CopyTo(data, 0);

        Action act = () => new DollarI(data, Name);

        act.Should().Throw<RecycleBinParseException>().Where(e => e.Field == "Format");
    }

    [Test]
    public void NullDataThrows()
    {
        ((Action)(() => new DollarI(null, Name))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new Info2(null, Name))).Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Format1WithTruncatedNameKeepsWhatIsThere()
    {
        var di = new DollarI(DollarI1(@"C:\short.txt", nameBytes: 40), Name);

        di.Filename.Should().Be(@"C:\short.txt");
        di.ParseErrors.Should().ContainSingle().Which.Should().Contain("shorter than the 520 bytes");
    }

    [TestCase(-1)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    [TestCase(500)]
    public void Format2BadNameLengthIsReportedAndClamped(int length)
    {
        var di = new DollarI(DollarI2(@"C:\file.txt", length), Name);

        di.Filename.Should().Be(@"C:\file.txt");
        di.ParseErrors.Should().ContainSingle().Which.Should().Contain($"is {length} characters");
    }

    [Test]
    public void Format2WithoutNameLengthIsReported()
    {
        var di = new DollarI(DollarI2("x").Take(26).ToArray(), Name);

        di.Filename.Should().BeEmpty();
        di.ParseErrors.Should().ContainSingle();
    }

    [TestCase(-1L)]
    [TestCase(long.MaxValue)]
    public void OutOfRangeDeletionTimeIsReported(long time)
    {
        var di = new DollarI(DollarI1(@"C:\a.txt", time), Name);

        di.DeletedOn.Should().Be(DateTimeOffset.MinValue);
        di.Filename.Should().Be(@"C:\a.txt");
        di.ParseErrors.Should().ContainSingle().Which.Should().Contain("Deletion time");
    }

    [Test]
    public void DeletionTimeIsUtc()
    {
        var di = new DollarI(DollarI1(@"C:\a.txt"), Name);

        di.DeletedOn.Should().Be(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero));
        di.DeletedOn.Offset.Should().Be(TimeSpan.Zero);
    }

    [TestCase(0)]
    [TestCase(19)]
    public void Info2SmallerThanTheHeaderThrows(int size)
    {
        Action act = () => new Info2(new byte[size], Name);

        act.Should().Throw<RecycleBinParseException>().Where(e => e.Field == "Header");
    }

    [TestCase(0)]
    [TestCase(-800)]
    [TestCase(279)]
    public void Info2RecordSizeTooSmallThrows(int size)
    {
        var data = Info2(800);
        BitConverter.GetBytes(size).CopyTo(data, 12);

        Action act = () => new Info2(data, Name);

        act.Should().Throw<RecycleBinParseException>().Where(e => e.Field == "FileEntrySize");
    }

    [Test]
    public void Info2WithAnsiOnlyRecords()
    {
        // Windows 95/98/ME
        var i2 = new Info2(Info2(280, (@"C:\WINDOWS\a.txt", false), (@"C:\WINDOWS\b.txt", true)), Name);

        i2.ParseErrors.Should().BeEmpty();
        i2.FileRecords.Select(t => t.FileNameAscii).Should().Equal(@"C:\WINDOWS\a.txt", "");
        i2.FileRecords.Select(t => t.FileNameUnicode).Should().Equal("", "");
        i2.FileRecords.Select(t => t.Removed).Should().Equal(false, true);
    }

    [TestCase(1)]
    [TestCase(799)]
    public void Info2TrailingBytesAreReported(int extra)
    {
        var data = Info2(800, (@"C:\a.txt", false)).Concat(new byte[extra]).ToArray();

        var i2 = new Info2(data, Name);

        i2.FileRecords.Should().ContainSingle();
        i2.ParseErrors.Should().ContainSingle().Which.Should().Contain($"{extra} trailing byte(s)");
    }

    [Test]
    public void Info2UnexpectedRecordSizeIsReportedAndUsed()
    {
        var data = Info2(900, (@"C:\a.txt", false), (@"C:\b.txt", false));

        var i2 = new Info2(data, Name);

        i2.FileRecords.Select(t => t.FileNameUnicode).Should().Equal(@"C:\a.txt", @"C:\b.txt");
        i2.ParseErrors.Should().ContainSingle().Which.Should().Contain("Unexpected record size 900");
    }

    [Test]
    public void Info2OutOfRangeTimeIsReported()
    {
        var data = Info2(800, (@"C:\a.txt", false));
        BitConverter.GetBytes(-5L).CopyTo(data, 20 + 268);

        var i2 = new Info2(data, Name);

        i2.FileRecords.Single().DeletedOn.Should().Be(DateTimeOffset.MinValue);
        i2.ParseErrors.Should().ContainSingle().Which.Should().Contain("Deletion time");
    }

    [Test]
    public void RandomCorruptionOnlyThrowsParseExceptions()
    {
        var random = new Random(7);
        var goods = new[]
        {
            DollarI1(@"C:\Users\test\file.txt"), DollarI2(@"C:\Users\test\file.txt"),
            Info2(800, (@"C:\a.txt", false), (@"C:\b.txt", true)), Info2(280, (@"C:\a.txt", false))
        };

        foreach (var good in goods)
        {
            for (var i = 0; i < 1000; i++)
            {
                var data = good.Take(random.Next(0, good.Length + 1)).ToArray();
                for (var flips = random.Next(0, 6); flips > 0 && data.Length > 0; flips--)
                {
                    data[random.Next(data.Length)] = (byte)random.Next(256);
                }

                Action act = () =>
                {
                    if (good == goods[0] || good == goods[1])
                    {
                        _ = new DollarI(data, Name);
                    }
                    else
                    {
                        _ = new Info2(data, Name);
                    }
                };

                try
                {
                    act();
                }
                catch (RecycleBinParseException)
                {
                    // Expected for a damaged header
                }
            }
        }
    }

    [Test]
    public void FileRecordToStringShowsSizeInHex()
    {
        new FileRecord("u", "a", 1, 2, DateTimeOffset.MinValue, 255).ToString().Should().Contain("File size: 0xFF");
    }
}
