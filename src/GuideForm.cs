using System.Drawing;

namespace ZesilovacAplikaci;

/// <summary>
/// Navod, co je potreba nastavit, aby zesilovani nad 100 % vubec fungovalo.
/// Krok se zarizenim se neda udelat za uzivatele: vyber vystupu bezi v
/// samostatnem nastroji s pravy spravce a nikdo jiny nez uzivatel nevi, ktera
/// sluchatka nebo reproduktory chce zesilovat.
/// </summary>
internal sealed class GuideForm : Form
{
    private readonly PreampBackend _preamp;

    public GuideForm(PreampBackend preamp)
    {
        _preamp = preamp;

        Text = S.T("Setup guide", "Návod k nastavení");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 470);
        Font = new Font("Segoe UI", 9f);

        int y = 14;

        y = AddStep(1, y,
            S.T("Install Equalizer APO", "Nainstaluj Equalizer APO"),
            S.T("The amplifier gets above 100 % through Equalizer APO, which sits directly in the " +
                "Windows audio engine — so the boost costs no extra latency. Without it the app only " +
                "turns other apps down and you have to raise the master volume yourself.",
                "Nad 100 % se aplikace dostane přes Equalizer APO, které sedí přímo v audio enginu " +
                "Windows — zesílení tak nestojí žádnou latenci navíc. Bez něj umí aplikace jen " +
                "ztlumit ostatní a rozdíl si musíš přidat hlavní hlasitostí sám."),
            _preamp.IsAvailable
                ? S.T("Installed.", "Nainstalováno.")
                : S.T("Not found — use the Download button below.",
                      "Nenalezeno — použij tlačítko Stáhnout dole."),
            _preamp.IsAvailable);

        y = AddStep(2, y,
            S.T("Tick your output device", "Zaškrtni svůj výstup"),
            S.T("In the Device selector tick exactly the headphones or speakers you want amplified — " +
                "and only those. Equalizer APO does nothing on devices that are not ticked. " +
                "The tool needs administrator rights and Windows will ask for confirmation; " +
                "after ticking a device you have to unplug and replug it or restart Windows.",
                "Ve vybírači zařízení zaškrtni přesně ta sluchátka nebo reproduktory, které chceš " +
                "zesilovat — a jen ty. Na nezaškrtnutých zařízeních Equalizer APO nedělá nic. " +
                "Nástroj potřebuje práva správce a Windows se zeptají na potvrzení; po zaškrtnutí " +
                "zařízení ho odpoj a znovu připoj, nebo restartuj Windows."),
            null, false);

        y = AddStep(3, y,
            S.T("Hook the amplifier into the config", "Zapoj zesilovač do konfigurace"),
            S.T("The app adds a single Include line to config.txt and keeps its own preamp in a " +
                "separate file. Your own EQ and preamp stay untouched, and config.txt is backed up " +
                "first. The Remove button at the bottom of the main window undoes it.",
                "Aplikace přidá do config.txt jediný řádek Include a svůj preamp si drží ve vlastním " +
                "souboru. Tvůj vlastní EQ i preamp zůstávají netknuté a config.txt se předtím zálohuje. " +
                "Tlačítko Odebrat v hlavním okně to zase vrátí."),
            _preamp.IsIncluded
                ? S.T("Hooked up.", "Zapojeno.")
                : S.T("Not hooked up yet — use the Set up button.",
                      "Zatím nezapojeno — použij tlačítko Nastavit."),
            _preamp.IsIncluded);

        y = AddStep(4, y,
            S.T("Instant apply in the editor", "Okamžité uložení v editoru"),
            S.T("Instant mode makes the Equalizer APO editor apply and save every change as you type, " +
                "instead of waiting for a manual save. The Set up button turns it on for you. " +
                "If the editor is open while you do that, close it first — it rewrites the setting " +
                "on exit.",
                "Okamžitý režim způsobí, že editor Equalizer APO ukládá a promítá každou změnu hned " +
                "při psaní, místo čekání na ruční uložení. Tlačítko Nastavit ho zapne za tebe. " +
                "Pokud je přitom editor otevřený, nejdřív ho zavři — při ukončení si nastavení " +
                "přepíše podle sebe."),
            PreampBackend.EditorInstantMode
                ? S.T("On.", "Zapnuto.")
                : S.T("Off.", "Vypnuto."),
            PreampBackend.EditorInstantMode);

        AddStep(5, y,
            S.T("Then just use the sliders", "Pak už jen táhni za slidery"),
            S.T("Each app has its own gain. Above 100 % the app writes the difference as device " +
                "headroom and pulls every session down by the same amount, so only the boosted app " +
                "gets louder. The loudness watchdog then keeps sudden loud sounds from using that " +
                "boost — and its per-app strength column lets you turn it down where it gets in the way.",
                "Každá aplikace má vlastní zesílení. Nad 100 % si aplikace dopíše rozdíl jako rezervu " +
                "zařízení a o stejně tolik stáhne všechny session, takže hlasitěji je jen ta zesílená. " +
                "Hlídač hlasitých zvuků pak nepustí náhlé rány přes strop — a sloupec síly u každé " +
                "aplikace dovolí ubrat mu tam, kde překáží."),
            null, false);

        var close = new Button
        {
            Text = S.T("Close", "Zavřít"),
            DialogResult = DialogResult.OK,
            Bounds = new Rectangle(464, ClientSize.Height - 38, 80, 26),
        };

        var docs = new Button
        {
            Text = S.T("Online documentation", "Dokumentace online"),
            Bounds = new Rectangle(14, ClientSize.Height - 38, 160, 26),
        };
        docs.Click += (_, _) => MainForm.OpenUrl(PreampBackend.DocsUrl);

        var devices = new Button
        {
            Text = S.T("Device selector…", "Vybrat zařízení…"),
            Bounds = new Rectangle(182, ClientSize.Height - 38, 140, 26),
            Enabled = _preamp.IsAvailable,
        };
        devices.Click += (_, _) =>
        {
            if (!_preamp.LaunchDeviceSelector(out string problem))
                MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        Controls.AddRange(new Control[] { docs, devices, close });
        AcceptButton = close;
        CancelButton = close;
    }

    /// <summary>Vykresli jeden krok a vrati svislou pozici pro dalsi.</summary>
    private int AddStep(int number, int top, string title, string body, string state, bool ok)
    {
        var head = new Label
        {
            Text = number + ". " + title,
            Bounds = new Rectangle(14, top, 420, 18),
            Font = new Font(Font, FontStyle.Bold),
        };

        var text = new Label
        {
            Text = body,
            Bounds = new Rectangle(28, top + 20, 516, 58),
            ForeColor = Color.FromArgb(90, 90, 90),
            Font = new Font("Segoe UI", 8.25f),
        };

        Controls.Add(head);
        Controls.Add(text);

        if (state != null)
        {
            Controls.Add(new Label
            {
                Text = state,
                Bounds = new Rectangle(440, top, 110, 18),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = ok ? Color.FromArgb(40, 130, 70) : Color.FromArgb(190, 110, 20),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
            });
        }

        return top + 86;
    }
}
