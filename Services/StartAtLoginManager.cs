using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace ZaloShortcuts.Services;

public sealed record StartupRegistrationResult(
    bool Success,
    string Message,
    bool RequiresApproval = false);

public sealed class StartAtLoginManager
{
    private const string WindowsRunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string WindowsValueName = "Zalo Shortcuts";
    private const string MacLaunchAgentLabel = "com.zaloshortcuts.desktop";

    public bool IsSupported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public StartupRegistrationResult SetEnabled(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            return SetWindowsEnabled(enabled);
        }

        if (OperatingSystem.IsMacOSVersionAtLeast(13))
        {
            return SetModernMacEnabled(enabled);
        }

        if (OperatingSystem.IsMacOS())
        {
            return SetLegacyMacEnabled(enabled);
        }

        return enabled
            ? new StartupRegistrationResult(
                false,
                "Automatic startup is currently available on Windows and macOS.")
            : new StartupRegistrationResult(true, "Automatic startup is off.");
    }

    [SupportedOSPlatform("windows")]
    private static StartupRegistrationResult SetWindowsEnabled(bool enabled)
    {
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(WindowsRunKey);
            if (runKey is null)
            {
                return new StartupRegistrationResult(
                    false,
                    "Windows could not open your Startup Apps settings.");
            }

            if (!enabled)
            {
                runKey.DeleteValue(WindowsValueName, throwOnMissingValue: false);
                return new StartupRegistrationResult(
                    true,
                    "Zalo Shortcuts will not start automatically.");
            }

            var executablePath = GetExecutablePath();
            runKey.SetValue(
                WindowsValueName,
                $"\"{executablePath}\" --background",
                RegistryValueKind.String);

            return new StartupRegistrationResult(
                true,
                "Zalo Shortcuts will start quietly when you sign into Windows.");
        }
        catch (Exception exception)
        {
            return new StartupRegistrationResult(
                false,
                $"Windows could not enable automatic startup. {exception.Message}");
        }
    }

    private static StartupRegistrationResult SetModernMacEnabled(bool enabled)
    {
        try
        {
            var service = MacServiceManagement.GetMainAppService();
            var status = MacServiceManagement.GetStatus(service);

            if (!enabled)
            {
                if (status == MacServiceStatus.NotRegistered)
                {
                    return new StartupRegistrationResult(
                        true,
                        "Zalo Shortcuts will not start automatically.");
                }

                if (!MacServiceManagement.Unregister(service, out var unregisterError))
                {
                    return new StartupRegistrationResult(
                        false,
                        $"macOS could not disable the login item. {unregisterError}");
                }

                return new StartupRegistrationResult(
                    true,
                    "Zalo Shortcuts will not start automatically.");
            }

            if (status == MacServiceStatus.Enabled)
            {
                return new StartupRegistrationResult(
                    true,
                    "Zalo Shortcuts will start quietly when you sign into your Mac.");
            }

            if (status != MacServiceStatus.RequiresApproval &&
                !MacServiceManagement.Register(service, out var registerError))
            {
                status = MacServiceManagement.GetStatus(service);
                if (status != MacServiceStatus.RequiresApproval)
                {
                    return new StartupRegistrationResult(
                        false,
                        $"macOS could not add the login item. {registerError}");
                }
            }

            status = MacServiceManagement.GetStatus(service);
            if (status == MacServiceStatus.RequiresApproval)
            {
                MacServiceManagement.OpenLoginItemsSettings();
                return new StartupRegistrationResult(
                    true,
                    "Allow Zalo Shortcuts under Login Items so it can start automatically.",
                    RequiresApproval: true);
            }

            return status == MacServiceStatus.Enabled
                ? new StartupRegistrationResult(
                    true,
                    "Zalo Shortcuts will start quietly when you sign into your Mac.")
                : new StartupRegistrationResult(
                    false,
                    "macOS did not enable the login item. Open System Settings → General → Login Items and allow Zalo Shortcuts.");
        }
        catch (Exception exception)
        {
            return new StartupRegistrationResult(
                false,
                $"macOS could not configure the login item. {exception.Message}");
        }
    }

    private static StartupRegistrationResult SetLegacyMacEnabled(bool enabled)
    {
        try
        {
            var launchAgentsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library",
                "LaunchAgents");
            var plistPath = Path.Combine(
                launchAgentsDirectory,
                $"{MacLaunchAgentLabel}.plist");

            if (!enabled)
            {
                if (File.Exists(plistPath))
                {
                    File.Delete(plistPath);
                }

                return new StartupRegistrationResult(
                    true,
                    "Zalo Shortcuts will not start automatically.");
            }

            Directory.CreateDirectory(launchAgentsDirectory);
            var executablePath = SecurityElement.Escape(GetExecutablePath()) ??
                                 GetExecutablePath();
            var plist = $"""
                         <?xml version="1.0" encoding="UTF-8"?>
                         <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                         <plist version="1.0">
                         <dict>
                             <key>Label</key>
                             <string>{MacLaunchAgentLabel}</string>
                             <key>ProgramArguments</key>
                             <array>
                                 <string>{executablePath}</string>
                                 <string>--background</string>
                             </array>
                             <key>RunAtLoad</key>
                             <true/>
                         </dict>
                         </plist>
                         """;
            File.WriteAllText(plistPath, plist);

            return new StartupRegistrationResult(
                true,
                "Zalo Shortcuts will start quietly when you sign into your Mac.");
        }
        catch (Exception exception)
        {
            return new StartupRegistrationResult(
                false,
                $"macOS could not create the login item. {exception.Message}");
        }
    }

    private static string GetExecutablePath() =>
        Environment.ProcessPath ??
        throw new InvalidOperationException("The app executable path is unavailable.");

    private enum MacServiceStatus : long
    {
        NotRegistered = 0,
        Enabled = 1,
        RequiresApproval = 2,
        NotFound = 3
    }

    private static class MacServiceManagement
    {
        private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
        private const string ServiceManagementFramework =
            "/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement";
        private static IntPtr _frameworkHandle;

        public static IntPtr GetMainAppService()
        {
            EnsureFrameworkLoaded();
            var serviceClass = objc_getClass("SMAppService");
            var service = SendPointer(
                serviceClass,
                sel_registerName("mainAppService"));

            return service != IntPtr.Zero
                ? service
                : throw new InvalidOperationException(
                    "The macOS login-item service is unavailable.");
        }

        public static MacServiceStatus GetStatus(IntPtr service) =>
            (MacServiceStatus)SendInteger(
                service,
                sel_registerName("status"));

        public static bool Register(IntPtr service, out string error) =>
            SendWithError(service, "registerAndReturnError:", out error);

        public static bool Unregister(IntPtr service, out string error) =>
            SendWithError(service, "unregisterAndReturnError:", out error);

        public static void OpenLoginItemsSettings()
        {
            EnsureFrameworkLoaded();
            var serviceClass = objc_getClass("SMAppService");
            SendVoid(
                serviceClass,
                sel_registerName("openSystemSettingsLoginItems"));
        }

        private static void EnsureFrameworkLoaded()
        {
            if (_frameworkHandle == IntPtr.Zero)
            {
                _frameworkHandle = NativeLibrary.Load(ServiceManagementFramework);
            }
        }

        private static bool SendWithError(
            IntPtr service,
            string selectorName,
            out string error)
        {
            var nativeError = IntPtr.Zero;
            var succeeded = SendBooleanWithError(
                service,
                sel_registerName(selectorName),
                ref nativeError);
            error = GetErrorMessage(nativeError);
            return succeeded;
        }

        private static string GetErrorMessage(IntPtr error)
        {
            if (error == IntPtr.Zero)
            {
                return "No additional error information was provided.";
            }

            var description = SendPointer(
                error,
                sel_registerName("localizedDescription"));
            var utf8 = SendPointer(
                description,
                sel_registerName("UTF8String"));
            return Marshal.PtrToStringUTF8(utf8) ??
                   "No additional error information was provided.";
        }

        [DllImport(ObjectiveCLibrary)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(ObjectiveCLibrary)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(
            ObjectiveCLibrary,
            EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendPointer(
            IntPtr receiver,
            IntPtr selector);

        [DllImport(
            ObjectiveCLibrary,
            EntryPoint = "objc_msgSend")]
        private static extern long SendInteger(
            IntPtr receiver,
            IntPtr selector);

        [DllImport(
            ObjectiveCLibrary,
            EntryPoint = "objc_msgSend")]
        private static extern void SendVoid(
            IntPtr receiver,
            IntPtr selector);

        [DllImport(
            ObjectiveCLibrary,
            EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SendBooleanWithError(
            IntPtr receiver,
            IntPtr selector,
            ref IntPtr error);
    }
}
