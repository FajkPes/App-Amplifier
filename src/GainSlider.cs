using System.Drawing;
using System.Drawing.Drawing2D;

namespace ZesilovacAplikaci;

/// <summary>
/// Slider zesileni. Krome vlastni hodnoty kresli primo do drahy i to,
/// co ma aplikace prave nastavene ve smesovaci Windows - aby bylo videt,
/// jak se pozadovane zesileni rozpadlo na preamp + hlasitost session.
/// </summary>
internal sealed class GainSlider : Control
{
    private const int TrackHeight = 6;
    private const int HandleRadius = 8;

    private int _value = 100;
    private int _maximum = 400;
    private float _windowsPercent = 100f;
    private int _duckPercent = int.MaxValue;
    private int _currentPercent = 100;
    private float _peakLevel;
    private int _ceilingMark;
    private bool _showPeak = true;
    private bool _dragging;

    public event EventHandler ValueChanged;

    public GainSlider()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
    }

    public int Minimum => 0;

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(10, value);
            if (_value > _maximum) Value = _maximum;
            Invalidate();
        }
    }

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, Minimum, _maximum);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Zesileni, ktere prave povoluje hlidac. Kdyz je nizsi nez Value,
    /// kus drahy mezi nimi se vykresli jinak - je videt, kolik boostu
    /// hlidac zrovna drzi zpatky.
    /// </summary>
    public int DuckPercent
    {
        get => _duckPercent;
        set
        {
            if (_duckPercent == value) return;
            _duckPercent = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Zesileni, ktere aplikace prave dostava. Za normalnich okolnosti se rovna
    /// Value, pri zasahu hlidace klesa. Kresli se primo nad drahou.
    /// </summary>
    public int CurrentPercent
    {
        get => _currentPercent;
        set
        {
            if (_currentPercent == value) return;
            _currentPercent = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Skutecne slysena hlasitost (0-1). Kresli se primo v draze na stejne ose
    /// jako zesileni, takze kdyz ukazatel dosahne rysky na 100 %, zvuk je na
    /// plnem rozsahu a zacina orezavat.
    /// </summary>
    public float PeakLevel
    {
        get => _peakLevel;
        set
        {
            if (Math.Abs(_peakLevel - value) < 0.004f) return;
            _peakLevel = value;
            if (_showPeak) Invalidate();
        }
    }

    /// <summary>Zobrazovat ukazatel hlasitosti v draze slideru.</summary>
    public bool ShowPeak
    {
        get => _showPeak;
        set { if (_showPeak == value) return; _showPeak = value; Invalidate(); }
    }

    /// <summary>
    /// Strop hlidace v procentech na stejne ose jako ukazatel hlasitosti.
    /// Kdyz ho pruh hlasitosti dosahne, hlidac zacne ubirat. 0 = neukazovat.
    /// </summary>
    public int CeilingMark
    {
        get => _ceilingMark;
        set { if (_ceilingMark == value) return; _ceilingMark = value; Invalidate(); }
    }

    /// <summary>Hlasitost ze smesovace Windows v procentech (0-100).</summary>
    public float WindowsPercent
    {
        get => _windowsPercent;
        set
        {
            if (Math.Abs(_windowsPercent - value) < 0.4f) return;
            _windowsPercent = value;
            Invalidate();
        }
    }

    // ------------------------------------------------------------------

    // draha sedi mirne nad stredem, aby se nad ni veslo cislo aktualni hodnoty
    private Rectangle TrackRect => new(HandleRadius, (Height - TrackHeight) / 2 + 2,
                                       Math.Max(1, Width - 2 * HandleRadius), TrackHeight);

    private int PercentToX(double percent)
    {
        var t = TrackRect;
        return t.X + (int)Math.Round(t.Width * Math.Clamp(percent, 0, _maximum) / _maximum);
    }

    private int XToPercent(int x)
    {
        var t = TrackRect;
        return (int)Math.Round((double)(x - t.X) / t.Width * _maximum);
    }

    // Kreslici prostredky se sdileji mezi vsemi slidery. Puvodne se vyrabely
    // pri kazdem prekresleni, tedy stovky pisem a stetcu za sekundu - to je
    // presne to, po cem aplikace casem zacne skubat.
    private static readonly SolidBrush TrackBrush = new(Color.FromArgb(224, 224, 224));
    private static readonly SolidBrush BoostBrush = new(Color.FromArgb(45, 160, 95));
    private static readonly SolidBrush NormalBrush = new(Color.FromArgb(70, 130, 220));
    private static readonly SolidBrush HeldBrush = new(Color.FromArgb(235, 150, 40));
    private static readonly SolidBrush HandleBrush = new(Color.White);
    private static readonly SolidBrush PeakLoudBrush = new(Color.FromArgb(225, 235, 70, 70));
    private static readonly SolidBrush PeakWarnBrush = new(Color.FromArgb(210, 255, 225, 130));
    private static readonly SolidBrush PeakBrush = new(Color.FromArgb(190, 255, 255, 255));
    private static readonly SolidBrush WinLabelBrush = new(Color.FromArgb(120, 120, 120));
    private static readonly SolidBrush CurLabelBrush = new(Color.FromArgb(70, 70, 70));
    private static readonly SolidBrush HeldLabelBrush = new(Color.FromArgb(200, 110, 15));
    private static readonly Pen UnityPen = new(Color.FromArgb(150, 150, 150));
    private static readonly Pen WindowsPen = new(Color.FromArgb(70, 70, 70), 2f);
    private static readonly Pen BoostPen = new(Color.FromArgb(45, 160, 95), 2.5f);
    private static readonly Pen NormalPen = new(Color.FromArgb(70, 130, 220), 2.5f);
    private static readonly Pen FocusPen = new(Color.FromArgb(120, 70, 130, 220), 1f);
    private static readonly Pen CeilingPen = new(Color.FromArgb(235, 150, 40), 2f);

    private string _winLabel;
    private int _winLabelValue = -1;
    private SizeF _winLabelSize;
    private string _curLabel;
    private int _curLabelValue = -1;
    private bool _curLabelBold;
    private SizeF _curLabelSize;

    private Font _smallFont;
    private Font _labelFont;
    private Font _labelBoldFont;

    private void EnsureFonts()
    {
        _smallFont ??= new Font(Font.FontFamily, 6.75f);
        _labelFont ??= new Font(Font.FontFamily, 7.5f);
        _labelBoldFont ??= new Font(Font.FontFamily, 7.5f, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        EnsureFonts();

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = TrackRect;
        int valueX = PercentToX(_value);
        bool boosted = _value > 100;

        FillRounded(g, TrackBrush, track, TrackHeight / 2);

        // vyplneni po aktualni hodnotu; nad 100 % zezelena, at je boost videt
        var filled = new Rectangle(track.X, track.Y, Math.Max(0, valueX - track.X), track.Height);
        if (filled.Width > 0)
            FillRounded(g, boosted ? BoostBrush : NormalBrush, filled, TrackHeight / 2);

        // kolik boostu prave drzi hlidac zpatky (od jeho meze po jezdec)
        bool watchdogHolding = _duckPercent < _value;
        if (watchdogHolding)
        {
            int duckX = PercentToX(_duckPercent);
            var held = new Rectangle(duckX, track.Y, Math.Max(0, valueX - duckX), track.Height);
            if (held.Width > 0) g.FillRectangle(HeldBrush, held);
        }

        // ryska na 100 % = puvodni hlasitost aplikace
        if (_maximum > 100)
        {
            int unityX = PercentToX(100);
            g.DrawLine(UnityPen, unityX, track.Y - 5, unityX, track.Bottom + 5);
        }

        // ziva hlasitost primo v draze; lezi vzdy uvnitr vyplne, protoze
        // slysena hlasitost = surovy zvuk krat zesileni a surovy zvuk je <= 1
        if (_showPeak && _peakLevel > 0.001f)
        {
            int peakX = PercentToX(_peakLevel * 100.0);
            var bar = new Rectangle(track.X, track.Y + 1, Math.Max(1, peakX - track.X), track.Height - 2);
            g.FillRectangle(_peakLevel > 0.97f ? PeakLoudBrush
                          : _peakLevel > 0.80f ? PeakWarnBrush
                                               : PeakBrush, bar);
        }

        // strop hlidace: kdyz sem dosahne pruh hlasitosti, zacne se ubirat
        if (_ceilingMark > 0 && _ceilingMark <= _maximum)
        {
            int cx = PercentToX(_ceilingMark);
            g.DrawLine(CeilingPen, cx, track.Y - 3, cx, track.Y - 1);
            g.DrawLine(CeilingPen, cx, track.Bottom + 1, cx, track.Bottom + 3);
        }

        // co je opravdu ve smesovaci Windows - jen ryska, prouzek by se pral
        // s ukazatelem hlasitosti
        int winX = PercentToX(_windowsPercent);
        g.DrawLine(WindowsPen, winX, track.Y - 2, winX, track.Bottom + 2);

        // Popisky se skladaji jen kdyz se zmeni cislo. Driv se pri kazdem
        // snimku vyrabely nove retezce - pri desitkach snimku za sekundu krat
        // pocet radku to byl stály prisun odpadu do pameti.
        // Popisek i jeho rozmer se pocitaji jen kdyz se zmeni cislo. Driv se
        // pri kazdem snimku skladal novy retezec a volalo MeasureString.
        int winWhole = (int)Math.Round(_windowsPercent);
        if (winWhole != _winLabelValue || _winLabel == null)
        {
            _winLabelValue = winWhole;
            _winLabel = $"Win {winWhole} %";
            _winLabelSize = g.MeasureString(_winLabel, _smallFont);
        }
        g.DrawString(_winLabel, _smallFont, WinLabelBrush,
            Math.Clamp(winX - _winLabelSize.Width / 2, 0, Width - _winLabelSize.Width),
            track.Bottom + 4);

        // zesileni, ktere aplikace prave dostava - nad drahou, u sve pozice
        var curFont = watchdogHolding ? _labelBoldFont : _labelFont;
        if (_currentPercent != _curLabelValue || _curLabel == null || watchdogHolding != _curLabelBold)
        {
            _curLabelValue = _currentPercent;
            _curLabelBold = watchdogHolding;
            _curLabel = $"{_currentPercent} %";
            _curLabelSize = g.MeasureString(_curLabel, curFont);
        }
        g.DrawString(_curLabel, curFont, watchdogHolding ? HeldLabelBrush : CurLabelBrush,
            Math.Clamp(PercentToX(_currentPercent) - _curLabelSize.Width / 2, 0, Width - _curLabelSize.Width),
            track.Y - _curLabelSize.Height - 1);

        // jezdec
        var handle = new Rectangle(valueX - HandleRadius, track.Y + track.Height / 2 - HandleRadius,
                                   HandleRadius * 2, HandleRadius * 2);
        g.FillEllipse(HandleBrush, handle);
        g.DrawEllipse(boosted ? BoostPen : NormalPen, handle);

        if (Focused) g.DrawEllipse(FocusPen, Rectangle.Inflate(handle, 3, 3));
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _smallFont?.Dispose(); _smallFont = null;
        _labelFont?.Dispose(); _labelFont = null;
        _labelBoldFont?.Dispose(); _labelBoldFont = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _smallFont?.Dispose();
            _labelFont?.Dispose();
            _labelBoldFont?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Zaobleny obdelnik ze dvou pulkruhu a obdelniku. Puvodni verze stavela
    /// GraphicsPath, tedy novy objekt GDI+ pri kazdem prekresleni - dvakrat na
    /// snimek krat pocet radku. Tohle nealokuje nic.
    /// </summary>
    private static void FillRounded(Graphics g, Brush brush, Rectangle r, int radius)
    {
        if (r.Width <= 0 || r.Height <= 0) return;

        int d = radius * 2;
        if (r.Width <= d) { g.FillEllipse(brush, r.X, r.Y, r.Width, r.Height); return; }

        g.FillRectangle(brush, r.X + radius, r.Y, r.Width - d, r.Height);
        g.FillEllipse(brush, r.X, r.Y, d, r.Height);
        g.FillEllipse(brush, r.Right - d, r.Y, d, r.Height);
    }

    // ------------------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _dragging = true;
        Value = XToPercent(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) Value = XToPercent(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += Math.Sign(e.Delta) * 5;
    }

    protected override bool IsInputKey(Keys key) =>
        key is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(key);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left: Value -= 5; e.Handled = true; break;
            case Keys.Right: Value += 5; e.Handled = true; break;
            case Keys.Home: Value = 100; e.Handled = true; break;   // zpet na puvodni hlasitost
            case Keys.End: Value = Maximum; e.Handled = true; break;
        }
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
