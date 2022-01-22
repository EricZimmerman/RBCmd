using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RecycleBin;

public class DollarI
{
    //https://github.com/libyal/dtformats/blob/master/documentation/Windows%20Recycle.Bin%20file%20formats.asciidoc

    public DollarI(byte[] rawBytes, string sourceName)
    {
        SourceName = sourceName;

        Format = BitConverter.ToInt64(rawBytes, 0);
        FileSize = BitConverter.ToInt64(rawBytes, 8);

        DeletedOn = DateTimeOffset.FromFileTime(BitConverter.ToInt64(rawBytes, 16));

        DirectoryFiles = new List<DirFileInfo>();
        switch (Format)
        {
            case 1:

                Filename = Encoding.Unicode.GetString(rawBytes, 24, rawBytes.Length - 24).Split('\0').First();
                break;
            case 2:
                var nameLen = BitConverter.ToInt32(rawBytes, 24);

                Filename = Encoding.Unicode.GetString(rawBytes, 28, nameLen * 2).Split('\0').First();
                break;
        }

        var fName = Path.GetFileName(sourceName).Replace("$IR","$RR");
        var baseDirname = Path.GetDirectoryName(sourceName);
        var dirName = Path.Combine(baseDirname, fName);

        if (Directory.Exists(dirName))
        {
            //get files
            var files = Directory.GetFiles(dirName, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                {
                    var fif = new FileInfo(file);
                    var dfi = new DirFileInfo(file.Replace(dirName,Filename),fif.Length);
                    DirectoryFiles.Add(dfi);
                }
            }
        }
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

    public long Format { get; }
    public long FileSize { get; }
    public string Filename { get; }
    public string SourceName { get; }

    public List<DirFileInfo> DirectoryFiles { get; }

    public DateTimeOffset DeletedOn { get; }

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
}