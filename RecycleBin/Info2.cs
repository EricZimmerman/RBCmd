using System;
using System.Collections.Generic;
using System.Text;

namespace RecycleBin;

/// <summary>
///     An INFO2 file from RECYCLER / RECYCLED (Windows 95 to XP/2003).
/// </summary>
/// <remarks>
///     https://github.com/libyal/dtformats/blob/master/documentation/Windows%20Recycler%20file%20formats.asciidoc
///     A 20 byte header with the record size at 12, then fixed size records: a 260 byte ANSI path, record index,
///     drive number, deletion time, size and, in 800 byte records, a 520 byte UTF-16 path.
/// </remarks>
public class Info2
{
    private const int HeaderSize = 20;

    /// <summary>
    ///     Size of a record without the UTF-16 path (Windows 95/98/ME).
    /// </summary>
    public const int AnsiRecordSize = 280;

    /// <summary>
    ///     Size of a record with the UTF-16 path (Windows NT 4 to XP/2003).
    /// </summary>
    public const int UnicodeRecordSize = 800;

    /// <summary>
    ///     Parses an INFO2 file.
    /// </summary>
    /// <exception cref="RecycleBinParseException">The data is too small or the record size is too small</exception>
    public Info2(byte[] rawBytes, string sourceName)
    {
        if (rawBytes == null)
        {
            throw new ArgumentNullException(nameof(rawBytes));
        }

        SourceName = sourceName;

        if (rawBytes.Length < HeaderSize)
        {
            throw new RecycleBinParseException(
                $"File is {rawBytes.Length} bytes, smaller than the {HeaderSize} byte INFO2 header", 0, "Header");
        }

        Version = BitConverter.ToInt32(rawBytes, 0);
        Unknown1 = BitConverter.ToInt32(rawBytes, 4);
        Unknown2 = BitConverter.ToInt32(rawBytes, 8);
        FileEntrySize = BitConverter.ToInt32(rawBytes, 12);
        Unknown3 = BitConverter.ToInt32(rawBytes, 16);

        if (FileEntrySize < AnsiRecordSize)
        {
            throw new RecycleBinParseException(
                $"Record size {FileEntrySize} is smaller than the {AnsiRecordSize} byte minimum", 12, "FileEntrySize");
        }

        if (FileEntrySize != AnsiRecordSize && FileEntrySize != UnicodeRecordSize)
        {
            ParseErrors.Add(
                $"Unexpected record size {FileEntrySize} (expected {AnsiRecordSize} or {UnicodeRecordSize})");
        }

        var index = HeaderSize;

        while (rawBytes.Length - index >= FileEntrySize)
        {
            FileRecords.Add(ReadRecord(rawBytes, index));
            index += FileEntrySize;
        }

        if (index < rawBytes.Length)
        {
            ParseErrors.Add(
                $"{rawBytes.Length - index} trailing byte(s) at 0x{index:X} are too short for a {FileEntrySize} byte record");
        }
    }

    public int Version { get; }
    public int Unknown1 { get; }
    public int Unknown2 { get; }
    public int FileEntrySize { get; }
    public int Unknown3 { get; }

    public string SourceName { get; }

    public List<FileRecord> FileRecords { get; } = new List<FileRecord>();

    /// <summary>
    ///     Problems found while parsing, such as trailing bytes or an out-of-range time. Parsing continues where it can.
    /// </summary>
    public List<string> ParseErrors { get; } = new List<string>();

    /// <summary>
    ///     True when <see cref="ParseErrors" /> is not empty.
    /// </summary>
    public bool ParsingError => ParseErrors.Count > 0;

    private FileRecord ReadRecord(byte[] rawBytes, int offset)
    {
        var uniName = string.Empty;
        if (FileEntrySize >= UnicodeRecordSize)
        {
            uniName = Encoding.Unicode.GetString(rawBytes, offset + 280, 520).Split('\0')[0];
        }

        // Windows clears the first byte of the ANSI path when the file leaves the Recycle Bin (restored or emptied)
        var removed = rawBytes[offset] == 0;

        return new FileRecord(uniName, Encoding.ASCII.GetString(rawBytes, offset, 260).Split('\0')[0],
            BitConverter.ToInt32(rawBytes, offset + 260), BitConverter.ToInt32(rawBytes, offset + 264),
            Helpers.FromFileTime(BitConverter.ToInt64(rawBytes, offset + 268), offset + 268, "Deletion time",
                ParseErrors),
            BitConverter.ToInt32(rawBytes, offset + 276), removed);
    }
}

public class FileRecord
{
    public FileRecord(string fileNameUnicode, string fileNameAscii, int index, int driveNumber,
        DateTimeOffset deletedOn, int fileSize)
        : this(fileNameUnicode, fileNameAscii, index, driveNumber, deletedOn, fileSize, false)
    {
    }

    public FileRecord(string fileNameUnicode, string fileNameAscii, int index, int driveNumber,
        DateTimeOffset deletedOn, int fileSize, bool removed)
    {
        FileNameUnicode = fileNameUnicode;
        FileNameAscii = fileNameAscii;
        Index = index;
        DriveNumber = driveNumber;
        DeletedOn = deletedOn;
        FileSize = fileSize;
        Removed = removed;
    }

    public string FileNameUnicode { get; }

    /// <summary>
    ///     Empty when <see cref="Removed" /> is true, since the first byte has been cleared.
    /// </summary>
    public string FileNameAscii { get; }

    public int Index { get; }

    /// <summary>
    ///     Drive the file was deleted from: 0 is A:, 2 is C: and so on.
    /// </summary>
    public int DriveNumber { get; }

    /// <summary>
    ///     Deletion time in UTC. DateTimeOffset.MinValue when the stored value is out of range.
    /// </summary>
    public DateTimeOffset DeletedOn { get; }

    public int FileSize { get; }

    /// <summary>
    ///     True when the file is no longer in the Recycle Bin (it was restored, or the Recycle Bin was emptied):
    ///     Windows clears the first byte of the ANSI path but leaves the rest of the record.
    /// </summary>
    public bool Removed { get; }

    public override string ToString()
    {
        return
            $"Filename Ascii: {FileNameAscii}, Filename Uni: {FileNameUnicode}, Index: {Index}, Drive #: 0x{DriveNumber:X}, Deleted on: {DeletedOn.ToUniversalTime():yyyy/MM/dd HH:mm:ss.ffffff}, File size: 0x{FileSize:X}, Removed: {Removed}";
    }
}
