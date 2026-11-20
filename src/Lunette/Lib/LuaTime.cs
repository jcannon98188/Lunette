namespace Lunette.Lib;

/// <summary>Broken-down time (a C <c>struct tm</c> with a 64-bit year), always in the "C" locale.</summary>
internal readonly record struct BrokenTime(
    long Year, int Month, int Day, int Hour, int Minute, int Second, int YearDay, int WeekDay, bool IsDst, int OffsetSeconds, string Zone);

/// <summary>
/// Calendar arithmetic and strftime for <c>os.date</c> / <c>os.time</c>, independent of <see cref="DateTime"/> so the full
/// 32-bit-year range of PUC works and output is identical on every OS.
/// </summary>
internal static class LuaTime
{
    public const string ValidConversions = "aAbBcCdDeFgGhHIjmMnprRStTuUVwWxXyYzZ%";
    public const string ValidEConversions = "cCxXyY";
    public const string ValidOConversions = "deHImMSuUVwWy";

    private static readonly string[] Days = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    private static readonly string[] Months =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    public static long FloorDiv(long a, long b) => a / b - ((a % b != 0 && (a < 0) != (b < 0)) ? 1 : 0);

    public static long FloorMod(long a, long b) => a - FloorDiv(a, b) * b;

    /// <summary>Days since 1970-01-01 of the proleptic Gregorian date; <paramref name="month"/> is 1-12.</summary>
    public static long DaysFromCivil(long year, int month, int day)
    {
        long y = month <= 2 ? year - 1 : year;
        long era = FloorDiv(y, 400);
        long yoe = y - era * 400;
        long doy = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1;
        long doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
        return era * 146097 + doe - 719468;
    }

    private static void CivilFromDays(long days, out long year, out int month, out int day)
    {
        long z = days + 719468;
        long era = FloorDiv(z, 146097);
        long doe = z - era * 146097;
        long yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        long doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        long mp = (5 * doy + 2) / 153;
        day = (int)(doy - (153 * mp + 2) / 5 + 1);
        month = (int)(mp < 10 ? mp + 3 : mp - 9);
        year = yoe + era * 400 + (month <= 2 ? 1 : 0);
    }

    private static readonly long MinSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds() + 86400;
    private static readonly long MaxSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds() - 86400;

    /// <summary>The zone's UTC offset in seconds at an instant; outside the range of <see cref="DateTime"/> the standard offset is used.</summary>
    public static int OffsetAt(TimeZoneInfo zone, long unixSeconds, out bool isDst)
    {
        if (unixSeconds < MinSeconds || unixSeconds > MaxSeconds)
        {
            isDst = false;
            return (int)zone.BaseUtcOffset.TotalSeconds;
        }
        var at = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        isDst = zone.IsDaylightSavingTime(at);
        return (int)zone.GetUtcOffset(at).TotalSeconds;
    }

    /// <summary>Breaks <paramref name="unixSeconds"/> down in UTC or <paramref name="zone"/>; false when the year does not fit a C int.</summary>
    public static bool TryBreakDown(long unixSeconds, bool utc, TimeZoneInfo zone, out BrokenTime result)
    {
        int offset = 0;
        bool dst = false;
        if (!utc) offset = OffsetAt(zone, unixSeconds, out dst);
        long local = unixSeconds + offset;
        if ((offset > 0 && local < unixSeconds) || (offset < 0 && local > unixSeconds)) { result = default; return false; }
        long days = FloorDiv(local, 86400);
        long sod = local - days * 86400;
        CivilFromDays(days, out long year, out int month, out int day);
        result = default;
        if (year - 1900 < int.MinValue || year - 1900 > int.MaxValue) return false;
        int yday = (int)(days - DaysFromCivil(year, 1, 1));
        int wday = (int)FloorMod(days + 4, 7);
        string name = utc ? "GMT" : ZoneName(zone, dst, offset);
        result = new BrokenTime(year, month, day, (int)(sod / 3600), (int)(sod % 3600 / 60), (int)(sod % 60), yday, wday, dst, offset, name);
        return true;
    }

    private static string ZoneName(TimeZoneInfo zone, bool dst, int offset)
    {
        if (zone.Id is "UTC" or "Etc/UTC") return "UTC";
        string n = dst ? zone.DaylightName : zone.StandardName;
        if (n.Length is > 0 and <= 5 && !n.Contains(' ')) return n;
        int a = Math.Abs(offset);
        return (offset < 0 ? "-" : "+") + (a / 3600).ToString("00") + (a % 3600 == 0 ? "" : (a % 3600 / 60).ToString("00"));
    }

    /// <summary>
    /// <c>mktime</c>: normalises the broken-down fields (month is 0-based, year is minus 1900, as in <c>struct tm</c>) and returns
    /// the instant in the local zone, or null when the result cannot be represented.
    /// </summary>
    public static long? MakeTime(long tmYear, long tmMon, long tmMday, long hour, long min, long sec, TimeZoneInfo zone)
    {
        long year = tmYear + 1900 + FloorDiv(tmMon, 12);
        int month = (int)FloorMod(tmMon, 12) + 1;
        long days = DaysFromCivil(year, month, 1) + (tmMday - 1);
        long local = days * 86400 + hour * 3600 + min * 60 + sec;
        // Resolve the local wall-clock time to an instant: iterate the offset around the guess.
        int guess = OffsetAt(zone, local, out _);
        long t = local - guess;
        int second = OffsetAt(zone, t, out _);
        if (second != guess) t = local - second;
        // The normalised year must still fit a C int, as mktime reports otherwise.
        long normalised = FloorDiv(local, 86400);
        CivilFromDays(normalised, out long ny, out _, out _);
        if (ny - 1900 < int.MinValue || ny - 1900 > int.MaxValue) return null;
        return t;
    }

    // ---- strftime ----

    /// <summary>Appends the "C"-locale rendering of one conversion (the character after '%', E/O modifiers already stripped).</summary>
    public static void AppendConversion(ref ByteBuilder b, char conv, in BrokenTime t)
    {
        switch (conv)
        {
            case 'a': AppendText(ref b, Days[t.WeekDay][..3]); break;
            case 'A': AppendText(ref b, Days[t.WeekDay]); break;
            case 'b':
            case 'h': AppendText(ref b, Months[t.Month - 1][..3]); break;
            case 'B': AppendText(ref b, Months[t.Month - 1]); break;
            case 'c':
                AppendConversion(ref b, 'a', t); b.Append((byte)' ');
                AppendConversion(ref b, 'b', t); b.Append((byte)' ');
                AppendConversion(ref b, 'e', t); b.Append((byte)' ');
                AppendConversion(ref b, 'T', t); b.Append((byte)' ');
                AppendConversion(ref b, 'Y', t);
                break;
            case 'C': AppendNumber(ref b, FloorDiv(t.Year, 100), 2, '0'); break;
            case 'd': AppendNumber(ref b, t.Day, 2, '0'); break;
            case 'D':
            case 'x':
                AppendConversion(ref b, 'm', t); b.Append((byte)'/');
                AppendConversion(ref b, 'd', t); b.Append((byte)'/');
                AppendConversion(ref b, 'y', t);
                break;
            case 'e': AppendNumber(ref b, t.Day, 2, ' '); break;
            case 'F':
                AppendConversion(ref b, 'Y', t); b.Append((byte)'-');
                AppendConversion(ref b, 'm', t); b.Append((byte)'-');
                AppendConversion(ref b, 'd', t);
                break;
            case 'g': AppendNumber(ref b, FloorMod(IsoYearWeek(t, out _), 100), 2, '0'); break;
            case 'G': AppendNumber(ref b, IsoYearWeek(t, out _), 1, '0'); break;
            case 'H': AppendNumber(ref b, t.Hour, 2, '0'); break;
            case 'I': AppendNumber(ref b, t.Hour % 12 == 0 ? 12 : t.Hour % 12, 2, '0'); break;
            case 'j': AppendNumber(ref b, t.YearDay + 1, 3, '0'); break;
            case 'm': AppendNumber(ref b, t.Month, 2, '0'); break;
            case 'M': AppendNumber(ref b, t.Minute, 2, '0'); break;
            case 'n': b.Append((byte)'\n'); break;
            case 'p': AppendText(ref b, t.Hour < 12 ? "AM" : "PM"); break;
            case 'r':
                AppendConversion(ref b, 'I', t); b.Append((byte)':');
                AppendConversion(ref b, 'M', t); b.Append((byte)':');
                AppendConversion(ref b, 'S', t); b.Append((byte)' ');
                AppendConversion(ref b, 'p', t);
                break;
            case 'R':
                AppendConversion(ref b, 'H', t); b.Append((byte)':');
                AppendConversion(ref b, 'M', t);
                break;
            case 'S': AppendNumber(ref b, t.Second, 2, '0'); break;
            case 't': b.Append((byte)'\t'); break;
            case 'T':
            case 'X':
                AppendConversion(ref b, 'H', t); b.Append((byte)':');
                AppendConversion(ref b, 'M', t); b.Append((byte)':');
                AppendConversion(ref b, 'S', t);
                break;
            case 'u': AppendNumber(ref b, t.WeekDay == 0 ? 7 : t.WeekDay, 1, '0'); break;
            case 'U': AppendNumber(ref b, (t.YearDay + 7 - t.WeekDay) / 7, 2, '0'); break;
            case 'V': IsoYearWeek(t, out int week); AppendNumber(ref b, week, 2, '0'); break;
            case 'w': AppendNumber(ref b, t.WeekDay, 1, '0'); break;
            case 'W': AppendNumber(ref b, (t.YearDay + 7 - (t.WeekDay + 6) % 7) / 7, 2, '0'); break;
            case 'y': AppendNumber(ref b, FloorMod(t.Year, 100), 2, '0'); break;
            case 'Y': AppendNumber(ref b, t.Year, 1, '0'); break;
            case 'z':
            {
                int a = Math.Abs(t.OffsetSeconds);
                b.Append((byte)(t.OffsetSeconds < 0 ? '-' : '+'));
                AppendNumber(ref b, a / 3600, 2, '0');
                AppendNumber(ref b, a % 3600 / 60, 2, '0');
                break;
            }
            case 'Z': AppendText(ref b, t.Zone); break;
            case '%': b.Append((byte)'%'); break;
        }
    }

    /// <summary>Returns the ISO 8601 week-based year and sets the week number (1-53).</summary>
    private static long IsoYearWeek(in BrokenTime t, out int week)
    {
        long year = t.Year;
        int wd = (t.WeekDay + 6) % 7; // Monday = 0
        int w = (t.YearDay - wd + 10) / 7;
        if (w < 1)
        {
            year--;
            w = WeeksInYear(year);
        }
        else if (w > WeeksInYear(year))
        {
            year++;
            w = 1;
        }
        week = w;
        return year;
    }

    private static int WeeksInYear(long year)
    {
        long P(long y) => FloorMod(y + FloorDiv(y, 4) - FloorDiv(y, 100) + FloorDiv(y, 400), 7);
        return P(year) == 4 || P(year - 1) == 3 ? 53 : 52;
    }

    private static void AppendText(ref ByteBuilder b, string s)
    {
        for (int i = 0; i < s.Length; i++) b.Append((byte)s[i]);
    }

    private static void AppendNumber(ref ByteBuilder b, long value, int width, char pad)
    {
        string s = Math.Abs(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value < 0) b.Append((byte)'-');
        for (int i = s.Length; i < width; i++) b.Append((byte)pad);
        AppendText(ref b, s);
    }
}
