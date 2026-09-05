using System.Runtime.InteropServices;

namespace HeyAI.Core;

/// <summary>
/// Whether this process is running with MSIX package identity.
///
/// Nothing in HeyAI currently needs identity to work. It would be required for
/// `Windows.UI.Notifications` toasts, which is why an earlier version of this comment
/// called it a precondition for the confirmation prompts -- but those were built as a
/// WinForms dialog, and no toast exists in the project.
///
/// What identity does today is change where state lives, silently: see
/// <see cref="HeyAIPaths.IsRedirected"/>. Reported so that behaviour is visible rather
/// than mysterious.
///
/// Detection is <c>GetCurrentPackageFullName</c>, which returns APPMODEL_ERROR_NO_PACKAGE
/// when the process was launched outside a package.
/// </summary>
public static partial class PackageIdentity
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    private static readonly Lazy<string?> Cached = new(Resolve, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>True when the process has MSIX identity.</summary>
    public static bool IsPackaged => Cached.Value is not null;

    /// <summary>Package full name, or null when running unpackaged.</summary>
    public static string? FullName => Cached.Value;

    private static string? Resolve()
    {
        uint length = 0;
        var hr = GetCurrentPackageFullName(ref length, null);

        if (hr == AppModelErrorNoPackage) return null;
        if (hr != ErrorInsufficientBuffer && hr != ErrorSuccess) return null;
        if (length == 0) return null;

        var buffer = new char[length];
        hr = GetCurrentPackageFullName(ref length, buffer);
        if (hr != ErrorSuccess) return null;

        // length includes the terminating null.
        return new string(buffer, 0, (int)length - 1);
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetCurrentPackageFullName(
        ref uint packageFullNameLength,
        [Out] char[]? packageFullName);
}
