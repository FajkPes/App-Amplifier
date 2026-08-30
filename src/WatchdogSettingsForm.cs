using System.Drawing;

namespace ZesilovacAplikaci;

/// <summary>Doladeni hlidace hlasitych zvuku.</summary>
internal sealed class WatchdogSettingsForm : Form
{
    private readonly NumericUpDown _ceiling = new();
    private readonly NumericUpDown _floor = new();
    private readonly NumericUpDown _attack = new();
    private readonly NumericUpDown _release = new();
    private readonly NumericUpDown _smoothness = new();
    private readonly Label _preview = new();
    private readonly Label _scope = new();

    public int CeilingPercent => (int)_ceiling.Value;
    public int FloorPercent => (int)_floor.Value;
    public int AttackMs => (int)_attack.Value;
    public int ReleaseMs => (int)_release.Value;
    public int SmoothnessPercent => (int)_smoothness.Value;

    public WatchdogSettingsForm(int ceilingPercent, int floorPercent, int attackMs,
                                int releaseMs, int smoothnessPercent)
    {
        Text = S.T("Loud sound watchdog", "Hlídač hlasitých zvuků");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 422);
        Font = new Font("Segoe UI", 9f);

        Add(S.T("Loudness ceiling", "Strop hlasitosti"), _ceiling, 10, 120, ceilingPercent, "%", 14,
            S.T("Decides how soon the watchdog kicks in. Anything louder than this\n" +
                "limit gets pulled down. Lower = stricter, only quiet sounds keep full gain.",
                "Určuje, jak brzy hlídač zabere. Zvuk hlasitější než tahle mez\n" +
                "se stáhne. Níž = přísnější, plná hlasitost zůstane jen tichým zvukům."));

        Add(S.T("Lowest volume", "Nejnižší hlasitost"), _floor, 10, 100, floorPercent, "%", 80,
            S.T("How far down the watchdog may pull. 100 % = never below the original\n" +
                "volume, so it only watches boosted apps. Lower = watches all of them.",
                "Kam až smí hlídač stáhnout. 100 % = nikdy pod původní hlasitost,\n" +
                "takže hlídá jen zesílené aplikace. Níž = hlídá i nezesílené."));

        Add(S.T("Attack", "Nájezd útlumu"), _attack, 1, 500, attackMs, "ms", 146,
            S.T("How fast it grabs. Shorter catches sharp sounds too, but can be\n" +
                "heard as a click. Below 16 ms the sampling period decides anyway.",
                "Jak rychle hlídač zabere. Kratší chytí i ostré zvuky, ale může být\n" +
                "slyšet jako cvaknutí. Pod 16 ms už rozhoduje perioda vzorkování."));

        Add(S.T("Release", "Návrat hlasitosti"), _release, 100, 3000, releaseMs, "ms", 212,
            S.T("How fast the volume comes back. A short release pumps on dense\n" +
                "material — the volume breathes up and down.",
                "Jak rychle se hlasitost vrátí. Krátký návrat u hutné zvukové\n" +
                "kulisy pumpuje - hlasitost dýchá nahoru a dolů."));

        Add(S.T("Transition shape", "Plynulost přechodu"), _smoothness, 0, 100, smoothnessPercent, "%", 278,
            S.T("The shape of the transition, not its length. 0 % = even ramp, arrives\n" +
                "exactly on time. 100 % = soft exponential attack and decay.",
                "Tvar přechodu, ne jeho délka. 0 % = rovnoměrná rampa, dojede přesně\n" +
                "v zadaném čase. 100 % = měkký exponenciální náběh i doběh."));

        _release.Increment = 50;

        // posledni napoveda konci na 278 + 26 + 32 = 336, takze az odtud
        var separator = new Label
        {
            BorderStyle = BorderStyle.Fixed3D,
            Bounds = new Rectangle(16, 342, 428, 2),
        };

        _scope.SetBounds(16, 352, 428, 16);
        _scope.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);

        _preview.SetBounds(16, 370, 428, 16);
        _preview.ForeColor = Color.FromArgb(90, 90, 90);
        _preview.Font = new Font("Segoe UI", 8.25f);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 284, Top = 392, Width = 80 };
        var cancel = new Button
        {
            Text = S.T("Cancel", "Zrušit"),
            DialogResult = DialogResult.Cancel,
            Left = 370, Top = 392, Width = 80,
        };
        var reset = new Button { Text = S.T("Defaults", "Výchozí"), Left = 16, Top = 392, Width = 80 };
        reset.Click += (_, _) =>
        {
            _ceiling.Value = 100; _floor.Value = 100;
            _attack.Value = 100; _release.Value = 1000; _smoothness.Value = 100;
        };

        Controls.AddRange(new Control[] { separator, _scope, _preview, ok, cancel, reset });
        AcceptButton = ok;
        CancelButton = cancel;

        _ceiling.ValueChanged += (_, _) => UpdatePreview();
        _floor.ValueChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    private void Add(string caption, NumericUpDown box, int min, int max, int value,
                     string unit, int top, string help)
    {
        var label = new Label { Text = caption, Left = 16, Top = top + 2, Width = 120 };
        box.SetBounds(140, top, 70, 24);
        box.Minimum = min;
        box.Maximum = max;
        box.Value = Math.Clamp(value, min, max);
        box.TextAlign = HorizontalAlignment.Right;
        var unitLabel = new Label { Text = unit, Left = 216, Top = top + 2, Width = 30 };
        var helpLabel = new Label
        {
            Text = help,
            Left = 16,
            Top = top + 26,
            Width = 400,
            Height = 32,
            ForeColor = Color.FromArgb(110, 110, 110),
            Font = new Font("Segoe UI", 8f),
        };
        Controls.AddRange(new Control[] { label, box, unitLabel, helpLabel });
    }

    private void UpdatePreview()
    {
        bool everything = _floor.Value < 100;
        _scope.Text = everything
            ? S.T("Watches every app in the mixer, boosted or not.",
                  "Hlídá všechny aplikace ve směšovači, i nezesílené.")
            : S.T("Watches boosted apps only — it takes back boost but never goes below 100 %.",
                  "Hlídá jen zesílené aplikace — ubírá boost, ale nejde pod 100 %.");
        _scope.ForeColor = everything ? Color.FromArgb(180, 100, 15) : Color.FromArgb(40, 110, 60);

        // Od jake hlasitosti zvuku uz hlidac zacne ubirat. U zesilene aplikace
        // se boost pricita, u nezesilene se prah rovna primo stropu.
        double ceiling = (double)_ceiling.Value / 100.0;
        double gain = everything ? 1.0 : 1.5;
        double triggerPeak = Math.Min(1.0, ceiling / gain);
        double db = 20.0 * Math.Log10(Math.Max(triggerPeak, 1e-6));

        string subject = everything
            ? S.T("an app at 100 %", "aplikace na 100 %")
            : S.T("an app at 150 %", "aplikace na 150 %");

        _preview.Text = S.T(
            FormattableString.Invariant(
                $"Example: {subject} → the watchdog starts pulling from level {triggerPeak:0.00} ({db:0.0} dBFS)."),
            FormattableString.Invariant(
                $"Příklad: {subject} → hlídač zabere od hlasitosti {triggerPeak:0.00} ({db:0.0} dBFS)."));
    }
}
