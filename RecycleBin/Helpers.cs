using System;
using System.Collections.Generic;

namespace RecycleBin;

internal static class Helpers
{
    /// <summary>
    ///     Converts a FILETIME to UTC. An out-of-range value is added to errors and becomes DateTimeOffset.MinValue.
    /// </summary>
    public static DateTimeOffset FromFileTime(long fileTime, int offset, string field, List<string> errors)
    {
        try
        {
            return new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime));
        }
        catch (ArgumentOutOfRangeException)
        {
            errors.Add($"{field} at 0x{offset:X} is out of range (0x{fileTime:X16})");
            return DateTimeOffset.MinValue;
        }
    }
}
