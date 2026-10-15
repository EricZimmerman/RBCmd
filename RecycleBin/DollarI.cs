using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RecycleBin;

/// <summary>
///     A $I file from $Recycle.Bin (Windows Vista and later).
/// </summary>
/// <remarks>
///     https://github.com/libyal/dtformats/blob/master/documentation/Windows%20Recycle.Bin%20file%20formats.asciidoc
///     Format 1 (Vista to 8.1): format, size, deletion time, then a 520 byte (260 character) UTF-16 path.
///     Format 2 (Windows 10/11): format, size, deletion time, a 32-bit character count (including the NUL), then the path.
/// </remarks>
public class DollarI
{
    private const int HeaderSize = 24;
    private const int Format1NameSize = 520;

    /// <summary>
    ///     Parses a $I file. When the matching $R item next to sourceName is a directory, its files are listed in
    ///     <see cref="DirectoryFiles" />.
    /// </summary>
    /// <exception cref="RecycleBinParseException">The data is too small or the format is not 1 or 2</exception>
    public DollarI(byte[] rawBytes, string sourceName)
    {
        if (rawBytes == null)
        {
            throw new ArgumentNullException(nameof(rawBytes));
        }

        SourceName = sourceName;

        if (rawBytes.Length < HeaderSize)
        {
            throw new RecycleBinParseException(
                $"File is {rawBytes.Length} bytes, smaller than the {HeaderSize} byte $I header", 0, "Header");
        }

        Format = BitConverter.ToInt64(rawBytes, 0);

        if (Format != 1 && Format != 2)
        {
            throw new RecycleBinParseException($"Unsupported $I format {Format}. Expected 1 or 2", 0, "Format");
        }

        FileSize = BitConverter.ToInt64(rawBytes, 8);
        DeletedOn = Helpers.FromFileTime(BitConverter.ToInt64(rawBytes, 16), 16, "Deletion time", ParseErrors);

        Filename = Format == 1 ? ReadFormat1Name(rawBytes) : ReadFormat2Name(rawBytes);

        FindDirectoryFiles();
    }

    public long Format { get; }

    /// <summary>
    ///     Size of the deleted file or directory, in bytes.
    /// </summary>
    public long FileSize { get; }

    /// <summary>
    ///     Original path of the deleted file or directory.
    /// </summary>
    public string Filename { get; }

    public string SourceName { get; }

    /// <summary>
    ///     When the deleted item is a directory, the files in its $R directory, with paths under <see cref="Filename" />.
    /// </summary>
    public List<DirFileInfo> DirectoryFiles { get; } = new List<DirFileInfo>();

    /// <summary>
    ///     Deletion time in UTC. DateTimeOffset.MinValue when the stored value is out of range.
    /// </summary>
    public DateTimeOffset DeletedOn { get; }

    /// <summary>
    ///     Problems found while parsing, such as a truncated name or an out-of-range time. Parsing continues where it can.
    /// </summary>
    public List<string> ParseErrors { get; } = new List<string>();

    /// <summary>
    ///     True when <see cref="ParseErrors" /> is not empty.
    /// </summary>
    public bool ParsingError => ParseErrors.Count > 0;

    private string ReadFormat1Name(byte[] rawBytes)
    {
        var available = rawBytes.Length - HeaderSize;

        if (available < Format1NameSize)
        {
            ParseErrors.Add(
                $"File name at 0x{HeaderSize:X} is {available} bytes, shorter than the {Format1NameSize} bytes of format 1");
        }

        var length = Math.Min(available, Format1NameSize) & ~1;
        return Encoding.Unicode.GetString(rawBytes, HeaderSize, length).Split('\0')[0];
    }

    private string ReadFormat2Name(byte[] rawBytes)
    {
        const int nameOffset = HeaderSize + 4;

        if (rawBytes.Length < nameOffset)
        {
            ParseErrors.Add($"File is too short for the file name length at 0x{HeaderSize:X}");
            return string.Empty;
        }

        var nameLength = BitConverter.ToInt32(rawBytes, HeaderSize);
        var available = (rawBytes.Length - nameOffset) / 2;

        if (nameLength < 0 || nameLength > available)
        {
            ParseErrors.Add(
                $"File name length at 0x{HeaderSize:X} is {nameLength} characters, but only {available} remain");
            nameLength = available;
        }

        return Encoding.Unicode.GetString(rawBytes, nameOffset, nameLength * 2).Split('\0')[0];
    }

    private void FindDirectoryFiles()
    {
        var name = Path.GetFileName(SourceName ?? string.Empty);

        if (!name.StartsWith("$I", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // $IXXXXXX.ext goes with $RXXXXXX.ext
        var dirName = Path.Combine(Path.GetDirectoryName(SourceName) ?? string.Empty, "$R" + name.Substring(2));

        if (!Directory.Exists(dirName))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.GetFiles(dirName, "*", SearchOption.AllDirectories))
            {
                // Filename is a Windows path, so keep its separator whatever the platform
                var relative = file.Substring(dirName.Length).Replace(Path.DirectorySeparatorChar, '\\');
                DirectoryFiles.Add(new DirFileInfo(Filename + relative, new FileInfo(file).Length));
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            ParseErrors.Add($"Unable to list the files in '{dirName}': {ex.Message}");
        }
    }

    public override string ToString()
    {
        var os = "Pre-Win10";

        if (Format == 2)
        {
            os = "Win10";
        }

        return
            $"Source: {SourceName}, Format: {Format} ({os}), File size: 0x{FileSize:X}, Filename: {Filename}, Deleted on: {DeletedOn.ToUniversalTime():yyyy/MM/dd HH:mm:ss.ffffff}";
    }

    public class DirFileInfo
    {
        public DirFileInfo(string name, long size)
        {
            FileName = name;
            FileSize = size;
        }

        public string FileName { get; }
        public long FileSize { get; }
    }
}
