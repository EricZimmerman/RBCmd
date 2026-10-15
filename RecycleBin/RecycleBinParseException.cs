using System;

namespace RecycleBin;

/// <summary>
///     Thrown when data is not a usable $I or INFO2 file (too small or an unsupported format).
///     Problems found after the header are reported in the ParseErrors of <see cref="DollarI" /> and
///     <see cref="Info2" /> instead.
/// </summary>
public class RecycleBinParseException : Exception
{
    public RecycleBinParseException(string message, int offset = -1, string field = null, Exception inner = null)
        : base(message, inner)
    {
        Offset = offset;
        Field = field;
    }

    /// <summary>
    ///     Byte offset of the failing read, or -1 when unknown.
    /// </summary>
    public int Offset { get; }

    /// <summary>
    ///     Name of the field or structure being read when parsing failed, if known.
    /// </summary>
    public string Field { get; }
}
