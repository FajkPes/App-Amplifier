using Microsoft.Win32;

namespace ZesilovacAplikaci;

/// <summary>
/// Spousteni s Windows. Pouziva klic Run v HKCU, takze nepotrebuje admina
/// a plati jen pro prihlaseneho uzivatele.
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ZesilovacAplikaci";

    /// <summary>Prepinac, kterym se pri startu rovnou schovame do oznamovaci oblasti.</summary>
    public const string TrayArgument = "--tray";

    private static string CommandLine =>
        $"\"{Environment.ProcessPath}\" {TrayArgument}";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string s && s.Length > 0;
            }
            catch { return false; }
        }
    }

    /// <summary>Vraci true pri uspechu; pri selhani necha stav beze zmeny.</summary>
    public static bool Set(bool enabled, out string problem)
    {
        problem = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key == null) { problem = S.T("The Windows Run key could not be opened.", "Klíč Run nelze otevřít."); return false; }

            if (enabled) key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);

            return true;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Po presunu nebo prekompilovani .exe by v registru zustala stara cesta.
    /// Kdyz je autostart zapnuty, srovname ji s aktualni.
    /// </summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not string current) return;
            if (!string.Equals(current, CommandLine, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
        }
        catch { /* neni kriticke */ }
    }
}
