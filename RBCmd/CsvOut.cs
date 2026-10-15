namespace RBCmd;

public class CsvOut
{
    public string SourceName { get; set; }
    public string FileType { get; set; }
    public string FileName { get; set; }
    public long FileSize { get; set; }
    public string DeletedOn { get; set; }

    /// <summary>
    ///     INFO2 only: the file is no longer in the Recycle Bin (restored, or the Recycle Bin was emptied)
    /// </summary>
    public bool Removed { get; set; }
}