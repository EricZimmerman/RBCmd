using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RecycleBin;

public class Info2
{
    //https://github.com/libyal/dtformats/blob/master/documentation/Windows%20Recycler%20file%20formats.asciidoc

    public Info2(byte[] rawBytes, string sourceName)
    {
        SourceName = sourceName;
        Version = BitConverter.ToInt32(rawBytes, 0);
        Unknown1 = BitConverter.ToInt32(rawBytes, 4);
        Unknown2 = BitConverter.ToInt32(rawBytes, 8);
        FileEntrySize = BitConverter.ToInt32(rawBytes, 12);
        Unknown3 = BitConverter.ToInt32(rawBytes, 16);

        FileRecords = new List<FileRecord>();

        var index = 20;

        while (index < rawBytes.Length)
        {
            var b = new byte[800];

            Buffer.BlockCopy(rawBytes, index, b, 0, 800);


            var uniname = string.Empty;
            if (b.Length > 280)
            {
                uniname = Encoding.Unicode.GetString(b, 280, 520).Split('\0').First();
            }

            var fr = new FileRecord(uniname, Encoding.ASCII.GetString(b, 0, 260).Split('\0').First(),
                BitConverter.ToInt32(b, 260), BitConverter.ToInt32(b, 264),
                DateTimeOffset.FromFileTime(BitConverter.ToInt64(b, 268)).ToUniversalTime(),
                BitConverter.ToInt32(b, 276));

            FileRecords.Add(fr);

            index += 800;
        }
    }

    public int Version { get; }
    public int Unknown1 { get; }
    public int Unknown2 { get; }
    public int FileEntrySize { get; }
    public int Unknown3 { get; }

    public string SourceName { get; }

    public List<FileRecord> FileRecords { get; }
}

public class FileRecord
{
    public FileRecord(string fileNameUnicode, string fileNameAscii, int index, int driveNumber,
        DateTimeOffset deletedOn, int fileSize)
    {
        FileNameUnicode = fileNameUnicode;
        FileNameAscii = fileNameAscii;
        Index = index;
        DriveNumber = driveNumber;
        DeletedOn = deletedOn;
        FileSize = fileSize;
    }

    public string FileNameUnicode { get; }

    public string FileNameAscii { get; }
    public int Index { get; }
    public int DriveNumber { get; }
    public DateTimeOffset DeletedOn { get; }

    public int FileSize { get; }

    public override string ToString()
    {
        return
            $"Filename Ascii: {FileNameAscii}, Filename Uni: {FileNameUnicode}, Index: {Index}, Drive #: 0x{DriveNumber:X}, Deleted on: {DeletedOn.ToUniversalTime():yyyy/MM/dd HH:mm:ss.ffffff}, File size: 0x{FileSize}";
    }
}