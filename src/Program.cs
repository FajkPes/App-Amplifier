namespace ZesilovacAplikaci;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (Environment.GetCommandLineArgs().Any(a =>
                a.Equals("--check", StringComparison.OrdinalIgnoreCase)))
            return;

        // Dve instance by si prepisovaly preamp a prala se o hlasitosti.
        using var single = new Mutex(true, @"Local\ZesilovacAplikaci", out bool first);
        if (!first)
        {
            MessageBox.Show("App Amplifier is already running.",
                "App Amplifier",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var form = new MainForm();

        // Pojistka pro necekane konce: preamp nesmi zustat zvednuty a aplikace
        // ztlumene, jinak by uzivateli zustal rozhozeny zvuk. Pri beznem
        // ukonceni i pri vypinani Windows uz je predani hotove a tohle nic
        // nedela - HandOff se provede jen jednou.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeRestore(form);
        Application.ThreadException += (_, e) =>
        {
            SafeRestore(form);
            MessageBox.Show(S.T("Unexpected error:\n\n", "Neočekávaná chyba:\n\n") + e.Exception.Message,
                "App Amplifier", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        Application.Run(form);
    }

    private static void SafeRestore(MainForm form)
    {
        try { form.RestoreEverything(); } catch { }
    }
}
