using System.Drawing;
using System.Drawing.Drawing2D;

namespace ZesilovacAplikaci;

/// <summary>Jeden radek smesovace: ikona, nazev, slider 0-1000 %, metr, mute.</summary>
internal sealed class AppRow : UserControl
{
    private readonly PictureBox _icon = new();
    private readonly Label _name = new();
    private readonly Label _percent = new();
    private readonly GainSlider _slider = new();
    private readonly Button _mute = new();
    private readonly Button _watchdog = new();
    private readonly ContextMenuStrip _watchdogMenu = new();
    private readonly PeakMeter _meter = new();

    /// <summary>Nabidnute sily hlidace. Vic jak pet polozek uz je zbytecne klikani.</summary>
    private static readonly int[] StrengthPresets = { 0, 25, 50, 75, 100 };

    /// <summary>
    /// Levy okraj sloupce hlidace. Sdili ho zahlavi seznamu, aby popisek sloupce
    /// sedel presne nad tlacitkem - jinak by to bylo jen dalsi cislo v rade.
    /// </summary>
    public const int WatchdogX = 578;

    /// <summary>Jedna bublina pro cely program - kazda instance ToolTip stoji okno.</summary>
    private static readonly ToolTip Tips = new();

    private bool _suppress;
    private bool _wasDucking;
    private int _ceilingMark;

    // Pisma drzime, ne vyrabime. Prirazeni noveho Fontu do Control.Font stare
    // pismo neuvolni, takze puvodni "vyrob pokazde nove" tichl unikalo GDI
    // handly pri kazdem zasahu hlidace - az do vycerpani limitu procesu.
    private Font _boldFont;
    private Font _regularFont;

    public AppEntry Entry { get; }
    public event Action<AppEntry, double> GainChanged;
    public event Action<AppEntry, bool> MuteChanged;

    /// <summary>Uzivatel zmenil silu hlidace u teto aplikace (0 az 1).</summary>
    public event Action<AppEntry, double> WatchdogStrengthChanged;

    public AppRow(AppEntry entry)
    {
        Entry = entry;
        Height = 62;
        Width = 640;
        Margin = new Padding(0, 0, 0, 2);
        Padding = new Padding(8, 6, 8, 6);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        _icon.SetBounds(10, 15, 32, 32);
        _icon.SizeMode = PictureBoxSizeMode.Zoom;
        if (entry.Icon != null)
        {
            try { _icon.Image = entry.Icon.ToBitmap(); } catch { }
        }

        _name.SetBounds(52, 8, 200, 18);
        _name.Text = entry.DisplayName;
        _name.AutoEllipsis = true;
        _boldFont = new Font(Font, FontStyle.Bold);
        _name.Font = _boldFont;

        _meter.SetBounds(52, 30, 200, 8);

        _slider.SetBounds(260, 8, 220, 48);
        _slider.Maximum = MixerEngine.MaxGainPercent;
        _slider.Value = (int)Math.Round(entry.Gain * 100);
        _slider.CurrentPercent = _slider.Value;
        _slider.ValueChanged += (_, _) =>
        {
            if (_suppress) return;
            _slider.CurrentPercent = _slider.Value;   // at cislo nekmita za tahem
            UpdatePercentLabel();
            GainChanged?.Invoke(Entry, _slider.Value / 100.0);
        };
        _slider.DoubleClick += (_, _) => SetGain(1.0, notify: true);

        _percent.SetBounds(486, 18, 52, 20);
        _percent.TextAlign = ContentAlignment.MiddleRight;

        _mute.SetBounds(544, 16, 28, 24);
        _mute.FlatStyle = FlatStyle.Flat;
        _mute.Text = "M";
        _mute.Click += (_, _) =>
        {
            Entry.Muted = !Entry.Muted;
            UpdateMuteVisual();
            MuteChanged?.Invoke(Entry, Entry.Muted);
        };
        Tips.SetToolTip(_mute, S.T("Mute this app", "Ztlumit aplikaci"));

        // Sila hlidace jen pro tuhle aplikaci. U her je stahovani vitane,
        // u hudby nebo hlasovych hovoru spis prekazi - proto se da ubrat.
        _watchdog.SetBounds(WatchdogX, 16, 48, 24);
        _watchdog.FlatStyle = FlatStyle.Flat;
        _watchdog.Click += (_, _) => ShowStrengthMenu();

        foreach (int preset in StrengthPresets)
        {
            int value = preset;
            var item = new ToolStripMenuItem(preset + " %");
            item.Click += (_, _) => SetWatchdogStrength(value / 100.0, notify: true);
            _watchdogMenu.Items.Add(item);
        }

        Controls.AddRange(new Control[] { _icon, _name, _meter, _slider, _percent, _mute, _watchdog });
        UpdatePercentLabel();
        UpdateMuteVisual();
        UpdateWatchdogVisual();
    }

    private void ShowStrengthMenu()
    {
        int current = (int)Math.Round(Entry.WatchdogStrength * 100);
        for (int i = 0; i < _watchdogMenu.Items.Count; i++)
        {
            if (_watchdogMenu.Items[i] is ToolStripMenuItem item)
                item.Checked = StrengthPresets[i] == current;
        }
        _watchdogMenu.Show(_watchdog, new Point(0, _watchdog.Height));
    }

    /// <summary>Nastavi silu hlidace u teto aplikace. 1.0 = plny zasah, 0 = zadny.</summary>
    public void SetWatchdogStrength(double strength, bool notify)
    {
        Entry.WatchdogStrength = Math.Clamp(strength, 0.0, 1.0);
        UpdateWatchdogVisual();
        ApplyCeilingMark();
        if (notify) WatchdogStrengthChanged?.Invoke(Entry, Entry.WatchdogStrength);
    }

    /// <summary>
    /// Posune doraz slideru na strop hlasitosti. Nad nej se proste nedojede,
    /// takze omylem dotazeny slider na konec uz nic neprovede.
    /// </summary>
    public void SetLimit(int limitPercent)
    {
        if (_slider.Maximum == limitPercent) return;

        // pri snizovani stropu musime nejdriv dolu s hodnotou, jinak WinForms
        // vyhodi ArgumentException (Value mimo rozsah)
        if (limitPercent < _slider.Value)
        {
            _slider.Value = limitPercent;
            UpdatePercentLabel();
            GainChanged?.Invoke(Entry, limitPercent / 100.0);
        }
        _slider.Maximum = limitPercent;
    }

    public void SetGain(double gain, bool notify)
    {
        _suppress = true;
        _slider.Value = (int)Math.Clamp(Math.Round(gain * 100), _slider.Minimum, _slider.Maximum);
        _suppress = false;
        UpdatePercentLabel();
        if (notify) GainChanged?.Invoke(Entry, _slider.Value / 100.0);
    }

    /// <summary>Zapina ukazatel hlasitosti primo v draze slideru.</summary>
    public void SetShowPeakOnSlider(bool show) => _slider.ShowPeak = show;

    /// <summary>Znacka stropu hlidace v draze; 0 znamena neukazovat.</summary>
    public void SetCeilingMark(int percent)
    {
        _ceilingMark = percent;
        ApplyCeilingMark();
    }

    /// <summary>U aplikace s vypnutym hlidacem nema strop co ukazovat.</summary>
    private void ApplyCeilingMark()
        => _slider.CeilingMark = Entry.WatchdogStrength > 0.001 ? _ceilingMark : 0;

    public void UpdateMeter()
    {
        _meter.SetPeak(Entry.Peak);
        _slider.PeakLevel = Entry.Peak;
        _slider.WindowsPercent = Entry.WindowsVolume * 100f;
        _slider.DuckPercent = Entry.IsDucking
            ? (int)Math.Round(Entry.DuckGain * 100)
            : int.MaxValue;
        _slider.CurrentPercent = (int)Math.Round(
            (Entry.IsDucking ? Entry.DuckGain : Entry.Gain) * 100);

        if (Entry.IsDucking != _wasDucking)
        {
            _wasDucking = Entry.IsDucking;
            UpdatePercentLabel();
        }
    }

    private void UpdatePercentLabel()
    {
        int pct = _slider.Value;
        _percent.Text = pct + " %";
        _percent.ForeColor = _wasDucking ? Color.FromArgb(200, 120, 20)
                           : pct > 100 ? Color.FromArgb(0, 120, 60)
                           : SystemColors.ControlText;

        _boldFont ??= new Font(Font, FontStyle.Bold);
        _regularFont ??= new Font(Font, FontStyle.Regular);

        var wanted = pct > 100 ? _boldFont : _regularFont;
        if (!ReferenceEquals(_percent.Font, wanted)) _percent.Font = wanted;
    }

    private void UpdateWatchdogVisual()
    {
        int pct = (int)Math.Round(Entry.WatchdogStrength * 100);
        _watchdog.Text = pct + " %";

        // Plna sila je vychozi stav, takze vypada nenapadne. Cim vic je hlidac
        // u aplikace utlumeny, tim vic je to na tlacitku videt.
        _watchdog.BackColor = pct >= 100 ? SystemColors.Control
                            : pct == 0 ? Color.FromArgb(228, 228, 228)
                                       : Color.FromArgb(255, 236, 205);
        _watchdog.ForeColor = pct == 0 ? Color.FromArgb(130, 130, 130) : SystemColors.ControlText;

        Tips.SetToolTip(_watchdog, pct == 0
            ? S.T("Loudness watchdog ignores this app. Click to change.",
                  "Hlídač hlasitých zvuků tuhle aplikaci neřeší. Klikni pro změnu.")
            : S.T($"Watchdog strength for this app: {pct} %. Click to change.",
                  $"Síla hlídače u této aplikace: {pct} %. Klikni pro změnu."));
    }

    /// <summary>Kdyz je hlidac vypnuty globalne, nema sila u aplikace co delat.</summary>
    public void SetWatchdogAvailable(bool available) => _watchdog.Enabled = available;

    private void UpdateMuteVisual()
    {
        _mute.BackColor = Entry.Muted ? Color.FromArgb(210, 70, 70) : SystemColors.Control;
        _mute.ForeColor = Entry.Muted ? Color.White : SystemColors.ControlText;
    }

    private static readonly Pen DividerPen = new(Color.FromArgb(230, 230, 230));
    private static readonly Pen ColumnPen = new(Color.FromArgb(214, 214, 214));

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.DrawLine(DividerPen, 0, Height - 1, Width, Height - 1);

        // Svisla cara pred sloupcem hlidace. Bez ni splyvala jeho sila s
        // procenty zesileni a vypadala jako dalsi nahodne cislo v rade.
        e.Graphics.DrawLine(ColumnPen, WatchdogX - 12, 10, WatchdogX - 12, Height - 12);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Image?.Dispose();
            _watchdogMenu.Dispose();
            _percent.Font = null;   // odpojit drive, nez pismo zrusime
            _name.Font = null;
            _boldFont?.Dispose();
            _regularFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Jednoduchy spickovy metr s pomalym padem.</summary>
internal sealed class PeakMeter : Control
{
    private float _peak;
    private float _hold;

    public PeakMeter()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public void SetPeak(float peak)
    {
        _peak = peak;
        float hold = peak > _hold ? peak : Math.Max(0f, _hold - 0.02f);

        // prekreslujeme jen kdyz se zmeni sirka v pixelech; jinak by se metr
        // marne prekresloval desitky krat za sekundu i pri stejnem obrazu
        if ((int)(hold * Width) == (int)(_hold * Width) && Math.Abs(hold - _hold) < 0.001f) return;

        _hold = hold;
        Invalidate();
    }

    private static readonly SolidBrush MeterBg = new(Color.FromArgb(238, 238, 238));
    private static readonly SolidBrush MeterOk = new(Color.FromArgb(60, 170, 90));
    private static readonly SolidBrush MeterWarn = new(Color.FromArgb(230, 160, 40));
    private static readonly SolidBrush MeterLoud = new(Color.FromArgb(220, 60, 60));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.FillRectangle(MeterBg, ClientRectangle);

        int w = (int)(_hold * Width);
        if (w > 0)
        {
            // zelena do -6 dBFS, oranzova, cervena tesne pod clipem
            g.FillRectangle(_hold > 0.97f ? MeterLoud : _hold > 0.80f ? MeterWarn : MeterOk,
                0, 0, w, Height);
        }
    }
}
