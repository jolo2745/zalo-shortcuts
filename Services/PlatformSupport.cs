namespace ZaloShortcuts.Services;

public static class PlatformSupport
{
    public static bool IsLinux => OperatingSystem.IsLinux();

    public static bool CanSuppressKeys => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static bool IsWaylandOnly =>
        IsLinux &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) &&
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));

    public static string CurrentSummary =>
        OperatingSystem.IsWindows()
            ? "Windows 10/11"
            : OperatingSystem.IsMacOS()
                ? "macOS: allow Accessibility access when asked"
                : IsWaylandOnly
                    ? "Linux: switch to an X11 session"
                    : "Linux X11 mode";
}
