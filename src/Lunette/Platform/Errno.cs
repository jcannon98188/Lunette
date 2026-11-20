namespace Lunette;

/// <summary>A failure with a known errno, raised by the library layers (bad file descriptor, illegal seek, ...).</summary>
internal sealed class ErrnoException(int code) : Exception(Errno.Text(code) ?? "error")
{
    public int Code { get; } = code;
}

/// <summary>
/// Maps .NET I/O failures onto the POSIX errno table and glibc <c>strerror</c> texts, so <c>nil, msg, errno</c> results
/// stay PUC-literal. Numbering follows Linux. Windows <c>ERROR_*</c> codes are translated; on Unix the raw errno
/// carried in the exception's HResult is used. Anything unmappable becomes errno 0 with the .NET message.
/// </summary>
internal static class Errno
{
    public const int EPERM = 1, ENOENT = 2, EIO = 5, EBADF = 9, EACCES = 13, EBUSY = 16, EEXIST = 17, ENOTDIR = 20,
        EISDIR = 21, EINVAL = 22, EMFILE = 24, ENOSPC = 28, ESPIPE = 29, EROFS = 30, EPIPE = 32, ENAMETOOLONG = 36,
        ENOSYS = 38, ENOTEMPTY = 39, EILSEQ = 84;

    /// <summary>The strerror text for <paramref name="errno"/>, or null when the code is not in the table.</summary>
    public static string? Text(int errno) => errno switch
    {
        EPERM => "Operation not permitted",
        ENOENT => "No such file or directory",
        3 => "No such process",
        4 => "Interrupted system call",
        EIO => "Input/output error",
        EBADF => "Bad file descriptor",
        10 => "No child processes",
        12 => "Cannot allocate memory",
        EACCES => "Permission denied",
        EBUSY => "Device or resource busy",
        EEXIST => "File exists",
        18 => "Invalid cross-device link",
        ENOTDIR => "Not a directory",
        EISDIR => "Is a directory",
        EINVAL => "Invalid argument",
        EMFILE => "Too many open files",
        ENOSPC => "No space left on device",
        ESPIPE => "Illegal seek",
        EROFS => "Read-only file system",
        EPIPE => "Broken pipe",
        ENAMETOOLONG => "File name too long",
        ENOSYS => "Function not implemented",
        ENOTEMPTY => "Directory not empty",
        EILSEQ => "Invalid or incomplete multibyte or wide character",
        _ => null,
    };

    /// <summary>Translates a failure into (errno, message). The message is the strerror text, or the .NET message when unmapped.</summary>
    public static (int Code, string Message) From(Exception e)
    {
        int code = e switch
        {
            FileNotFoundException or DirectoryNotFoundException => ENOENT,
            PathTooLongException => ENAMETOOLONG,
            UnauthorizedAccessException => FromHResult(e.HResult, EACCES),
            ErrnoException x => x.Code,
            ArgumentException or NotSupportedException => EINVAL,
            _ => FromHResult(e.HResult, 0),
        };
        return code != 0 && Text(code) is { } text ? (code, text) : (0, e.Message);
    }

    private static int FromHResult(int hresult, int fallback)
    {
        if ((hresult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000)) return FromWin32(hresult & 0xFFFF, fallback);
        if (hresult is > 0 and < 200 && !OperatingSystem.IsWindows()) return hresult;
        return fallback;
    }

    private static int FromWin32(int code, int fallback) => code switch
    {
        2 or 3 or 15 or 53 or 67 or 267 => ENOENT,
        4 => EMFILE,
        5 or 19 or 32 or 33 or 65 => EACCES,
        6 => EBADF,
        8 or 14 => 12,
        17 => 18,
        80 or 183 => EEXIST,
        87 or 123 or 131 or 161 => EINVAL,
        112 or 39 => ENOSPC,
        145 => ENOTEMPTY,
        206 => ENAMETOOLONG,
        _ => fallback,
    };
}
