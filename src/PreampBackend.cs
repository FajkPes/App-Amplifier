using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace ZesilovacAplikaci;

/// <summary>
/// Dodava "hlavicku" (headroom) nad 100 % pomoci Equalizer APO.
///
/// Princip: APO sedi primo v audio enginu, takze zesileni nestoji zadnou
/// latenci navic - zpracovava se buffer, ktery tam stejne uz je. Per-aplikaci
/// se pak hlasitost dolaďuje nativni session hlasitosti (0-100 %), takze
/// vysledek je gain_aplikace = preamp * session_volume, coz muze byt > 100 %.
///
/// Do uzivatelske konfigurace nezasahujeme - pisemene jen do vlastniho
/// include souboru a do config.txt pridame jediny radek "Include:".
/// </summary>
internal sealed class PreampBackend
{
    private const string IncludeFileName = "ZesilovacAplikaci.txt";
    private const string Marker = "# --- App Amplifier (generated automatically; do not edit) ---";
    private const string LegacyMarker = "# --- Zesilovac aplikaci (generovano automaticky, needitovat) ---";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Odkud se Equalizer APO stahuje. Projekt zadne jine oficialni misto nema.</summary>
    public const string DownloadUrl = "https://sourceforge.net/projects/equalizerapo/";

    /// <summary>Oficialni dokumentace ke konfiguraci.</summary>
    public const string DocsUrl = "https://sourceforge.net/projects/equalizerapo/files/Documentation/";

    private readonly string _configDir;
    private readonly string _installDir;
    private double _lastWrittenDb = double.NaN;
    private DateTime _configStamp = DateTime.MinValue;

    public bool IsAvailable => _configDir != null;
    public string ConfigDirectory => _configDir;
    public string InstallDirectory => _installDir;

    /// <summary>Preamp, ktery uz ma uzivatel rucne v config.txt. Equalizer APO
    /// preampy scita, takze ho musime zapocitat, jinak by slidery lhaly.</summary>
    public double ExternalDb { get; private set; }

    /// <summary>Kolik dB pridava tato aplikace nad ramec uzivatelskeho nastaveni.</summary>
    public double OwnDb { get; private set; }

    /// <summary>Celkove zesileni zarizeni = uzivateluv preamp + nas.</summary>
    public double TotalDb => ExternalDb + OwnDb;

    public PreampBackend()
    {
        _installDir = LocateInstallDirectory();
        _configDir = LocateConfigDirectory(_installDir);
    }

    private static string ReadRegistry(string valueName)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\EqualizerAPO");
                if (key?.GetValue(valueName) is string s && !string.IsNullOrWhiteSpace(s)) return s;
            }
            catch { /* registr nedostupny - zkusime pevne cesty */ }
        }
        return null;
    }

    private static string LocateInstallDirectory()
    {
        var install = ReadRegistry("InstallPath");
        if (install != null && Directory.Exists(install)) return install;

        foreach (var guess in new[]
                 {
                     @"C:\Program Files\EqualizerAPO",
                     @"C:\Program Files (x86)\EqualizerAPO",
                 })
        {
            if (Directory.Exists(guess)) return guess;
        }
        return null;
    }

    private static string LocateConfigDirectory(string installDir)
    {
        var path = ReadRegistry("ConfigPath");
        if (path != null && Directory.Exists(path)) return path;

        if (installDir != null)
        {
            var cfg = Path.Combine(installDir, "config");
            if (Directory.Exists(cfg)) return cfg;
        }
        return null;
    }

    /// <summary>
    /// Vybirac zarizeni (Configurator). Tady se zaskrtava, na kterych
    /// reproduktorech ci sluchatkach ma Equalizer APO vubec pracovat - bez toho
    /// se do zvuku nedostane nic. Vyzaduje pravu spravce.
    /// </summary>
    public bool LaunchDeviceSelector(out string problem)
        => LaunchTool("DeviceSelector.exe", elevated: true, out problem);

    /// <summary>Editor konfigurace - pro uzivatele, ktery si chce doladit vlastni EQ.</summary>
    public bool LaunchEditor(out string problem)
        => LaunchTool("Editor.exe", elevated: false, out problem);

    private bool LaunchTool(string exeName, bool elevated, out string problem)
    {
        problem = null;
        if (_installDir == null) { problem = "Equalizer APO not found."; return false; }

        var exe = Path.Combine(_installDir, exeName);
        if (!File.Exists(exe)) { problem = exe + " not found."; return false; }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = _installDir,
            };
            if (elevated) psi.Verb = "runas";
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch (Exception ex) { problem = ex.Message; return false; }
    }

    /// <summary>
    /// Zapne v editoru Equalizer APO okamzity rezim - zmeny se ukladaji a
    /// projevi hned pri psani, misto aby cekaly na rucni ulozeni. Nastaveni
    /// je v HKCU, takze na nej nejsou potreba prava spravce.
    ///
    /// Pokud editor prave bezi, prepise si klic pri ukonceni podle sebe - proto
    /// se to vyplati nastavit, kdyz je zavreny.
    /// </summary>
    public static bool SetEditorInstantMode(out string problem)
    {
        problem = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\EqualizerAPO\Configuration Editor");
            key.SetValue("instantMode", "true", RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) { problem = ex.Message; return false; }
    }

    /// <summary>True, kdyz uz je okamzity rezim editoru zapnuty.</summary>
    public static bool EditorInstantMode
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\EqualizerAPO\Configuration Editor");
                return string.Equals(key?.GetValue("instantMode") as string, "true",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    /// <summary>Je nas Include uz zapojeny v config.txt?</summary>
    public bool IsIncluded
    {
        get
        {
            if (!IsAvailable) return false;
            try
            {
                var configTxt = Path.Combine(_configDir, "config.txt");
                if (!File.Exists(configTxt)) return false;
                foreach (var raw in File.ReadAllLines(configTxt))
                {
                    var t = raw.Trim();
                    if (t.StartsWith("#")) continue;
                    if (t.StartsWith("Include:", StringComparison.OrdinalIgnoreCase)
                        && t.Contains(IncludeFileName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }
    }

    /// <summary>Overi, ze do config slozky opravdu muzeme zapisovat.</summary>
    public bool CanWrite()
    {
        if (_configDir == null) return false;
        try
        {
            var probe = Path.Combine(_configDir, ".zesilovac_probe");
            File.WriteAllText(probe, "x", Utf8NoBom);
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Precte preamp, ktery si uzivatel nastavil sam v config.txt.
    /// Radky uvnitr bloku "Device:" ignorujeme - ty plati jen pro jedno
    /// zarizeni a nedaji se spolehlive zapocitat globalne.
    /// </summary>
    public void RefreshExternalDb()
    {
        if (!IsAvailable) { ExternalDb = 0; return; }

        // Volalo se to kazdou sekundu, takze se config.txt porad dokola cetl
        // z disku. Datum zmeny je jen dotaz na metadata - soubor otevirame
        // jen kdyz se opravdu neco zmenilo.
        try
        {
            var stamp = File.GetLastWriteTimeUtc(Path.Combine(_configDir, "config.txt"));
            if (stamp == _configStamp) return;
            _configStamp = stamp;
        }
        catch { /* soubor zmizel - projdeme si to nize a spadne to na 0 */ }

        double sum = 0;
        try
        {
            bool inDeviceBlock = false;
            foreach (var raw in File.ReadAllLines(Path.Combine(_configDir, "config.txt")))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                if (line.StartsWith("Device:", StringComparison.OrdinalIgnoreCase))
                {
                    // "Device:" bez hodnoty blok ukoncuje
                    inDeviceBlock = line[7..].Trim().Length > 0;
                    continue;
                }
                if (inDeviceBlock) continue;

                if (!line.StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase)) continue;

                var value = line[7..].Trim();
                if (value.EndsWith("dB", StringComparison.OrdinalIgnoreCase))
                    value = value[..^2].Trim();
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double db))
                    sum += db;
            }
        }
        catch { sum = 0; }

        ExternalDb = Math.Clamp(sum, -40.0, 40.0);
    }

    /// <summary>
    /// Zajisti, aby cele zarizeni melo aspon <paramref name="neededDb"/> zesileni.
    /// Uzivateluv vlastni preamp se zapocita, dopisujeme jen rozdil.
    /// Vraci skutecne dosazeny celkovy zisk v dB.
    /// </summary>
    public double EnsureHeadroomDb(double neededDb)
    {
        RefreshExternalDb();
        SetOwnDb(Math.Max(0.0, neededDb - ExternalDb));
        return TotalDb;
    }

    /// <summary>
    /// Nastavi nas vlastni preamp. Zapisuje jen pri skutecne zmene, aby se APO
    /// zbytecne nereloadovalo pri kazdem pohybu slideru.
    /// </summary>
    public void SetOwnDb(double db)
    {
        if (!IsAvailable) { OwnDb = db; return; }

        db = Math.Round(Math.Clamp(db, 0.0, 40.0), 2);
        OwnDb = db;
        if (!double.IsNaN(_lastWrittenDb) && Math.Abs(db - _lastWrittenDb) < 0.01) return;

        var body = new StringBuilder();
        body.AppendLine(Marker);
        body.AppendLine("# This file is managed by App Amplifier while it is running.");
        body.AppendLine("# Manual changes will be lost. Keep your own EQ in config.txt.");
        body.AppendLine(FormattableString.Invariant($"Preamp: {db:0.##} dB"));

        File.WriteAllText(Path.Combine(_configDir, IncludeFileName), body.ToString(), Utf8NoBom);

        _lastWrittenDb = db;
    }

    /// <summary>
    /// Prida do config.txt radek "Include: ..." na konec, pokud tam jeste neni.
    /// Uzivatelovy vlastni radky zustavaji netknute a jsou zpracovany drive,
    /// takze nas preamp sedi az za jeho EQ.
    /// </summary>
    public bool EnsureIncluded(out string problem)
    {
        problem = null;
        if (!IsAvailable) { problem = S.T("Equalizer APO was not found.", "Equalizer APO nebylo nalezeno."); return false; }

        try
        {
            var configTxt = Path.Combine(_configDir, "config.txt");
            var lines = File.Exists(configTxt)
                ? new List<string>(File.ReadAllLines(configTxt))
                : new List<string>();

            bool present = lines.Any(l =>
            {
                var t = l.Trim();
                if (t.StartsWith("#")) return false;
                return t.StartsWith("Include:", StringComparison.OrdinalIgnoreCase)
                       && t.Contains(IncludeFileName, StringComparison.OrdinalIgnoreCase);
            });

            if (!present)
            {
                // Zaloha uzivatelske konfigurace, at je vzdy cesta zpet.
                var backup = Path.Combine(_configDir, "config.txt.zesilovac-backup");
                if (File.Exists(configTxt) && !File.Exists(backup))
                    File.Copy(configTxt, backup);

                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                    lines.Add("");
                lines.Add(Marker);
                lines.Add("Include: " + IncludeFileName);
                File.WriteAllLines(configTxt, lines, Utf8NoBom);
            }

            SetOwnDb(0);
            RefreshExternalDb();
            return true;
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Vrati nas prispevek na 0 dB. Uzivateluv vlastni preamp v config.txt
    /// zustava netknuty. Volat pri ukonceni aplikace.
    /// </summary>
    public void Reset()
    {
        try { SetOwnDb(0); } catch { /* pri shutdownu uz nas chyba nezajima */ }
    }

    /// <summary>
    /// Uplne odstrani stopy po aplikaci z konfigurace Equalizer APO
    /// (radek Include + nas soubor). Pro tlacitko "odinstalovat zasah".
    /// </summary>
    public void RemoveFromConfig()
    {
        if (!IsAvailable) return;
        try
        {
            var configTxt = Path.Combine(_configDir, "config.txt");
            if (File.Exists(configTxt))
            {
                var kept = File.ReadAllLines(configTxt)
                    .Where(l => !l.Trim().Equals(Marker, StringComparison.Ordinal))
                    .Where(l => !l.Trim().Equals(LegacyMarker, StringComparison.Ordinal))
                    .Where(l => !(l.Trim().StartsWith("Include:", StringComparison.OrdinalIgnoreCase)
                                  && l.Contains(IncludeFileName, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                File.WriteAllLines(configTxt, kept, Utf8NoBom);
            }

            var inc = Path.Combine(_configDir, IncludeFileName);
            if (File.Exists(inc)) File.Delete(inc);
            _lastWrittenDb = double.NaN;
            OwnDb = 0;
        }
        catch { /* best effort */ }
    }

    public static double GainToDb(double gain) => 20.0 * Math.Log10(Math.Max(gain, 1e-6));

    public static double DbToGain(double db) => Math.Pow(10.0, db / 20.0);
}
