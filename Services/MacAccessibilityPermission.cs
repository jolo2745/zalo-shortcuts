using System.Runtime.InteropServices;

namespace ZaloShortcuts.Services;

public static class MacAccessibilityPermission
{
    private const string ApplicationServicesFramework =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string FoundationFramework =
        "/System/Library/Frameworks/Foundation.framework/Foundation";
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private static IntPtr _applicationServicesHandle;
    private static IntPtr _foundationHandle;

    public static bool IsTrusted()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return true;
        }

        try
        {
            EnsureFrameworksLoaded();
            return AXIsProcessTrusted();
        }
        catch
        {
            return false;
        }
    }

    public static bool RequestAccess()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return true;
        }

        try
        {
            EnsureFrameworksLoaded();

            var stringClass = objc_getClass("NSString");
            var numberClass = objc_getClass("NSNumber");
            var dictionaryClass = objc_getClass("NSDictionary");

            var promptKey = SendUtf8String(
                stringClass,
                sel_registerName("stringWithUTF8String:"),
                "AXTrustedCheckOptionPrompt");
            var trueValue = SendBoolean(
                numberClass,
                sel_registerName("numberWithBool:"),
                true);
            var options = SendTwoPointers(
                dictionaryClass,
                sel_registerName("dictionaryWithObject:forKey:"),
                trueValue,
                promptKey);

            return AXIsProcessTrustedWithOptions(options);
        }
        catch
        {
            return IsTrusted();
        }
    }

    private static void EnsureFrameworksLoaded()
    {
        if (_applicationServicesHandle == IntPtr.Zero)
        {
            _applicationServicesHandle =
                NativeLibrary.Load(ApplicationServicesFramework);
        }

        if (_foundationHandle == IntPtr.Zero)
        {
            _foundationHandle = NativeLibrary.Load(FoundationFramework);
        }
    }

    [DllImport(ApplicationServicesFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(ApplicationServicesFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport(ObjectiveCLibrary)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(
        ObjectiveCLibrary,
        EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendUtf8String(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(
        ObjectiveCLibrary,
        EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendBoolean(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(
        ObjectiveCLibrary,
        EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendTwoPointers(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second);
}
