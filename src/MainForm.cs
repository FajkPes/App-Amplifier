using System.Drawing;
using System.Text.Json;

namespace ZesilovacAplikaci;

internal sealed class MainForm : Form
{
    private readonly PreampBackend _preamp = new();
    private readonly MixerEngine _engine;
    private readonly Dictionary<string, AppRow> _rows = new(StringComparer.OrdinalIgnoreCase);

    private readonly FlowLayoutPanel _list = new();
    private readonly Panel _columns = new();
    private readonly Label _status = new();
    private readonly Label _hint = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly System.Windows.Forms.Timer _meterTimer = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new();
    private readonly System.Windows.Forms.Timer _gcTimer = new();

    private readonly CheckBox _protect = new();
    private readonly CheckBox _watchdog = new();
    private readonly CheckBox _autostart = new();
    private readonly CheckBox _background = new();
    private readonly CheckBox _peakOnSlider = new();
    private readonly NumericUpDown _limitBox = new();
    private readonly NumericUpDown _reserveBox = new();
    private readonly ComboBox _language = new();
    private readonly NotifyIcon _tray = new();
    private readonly GainStore _store = new();
    private bool _loading;
    private bool _exiting;
    private bool _systemShutdown;
    private bool _refreshQueued;

    // Vsechno, co nese text, si drzime - pri prepnuti jazyka se prepisuje na miste.
    private Label _capSound, _capWatchdog, _capDisplay, _capApp, _capApo;
    private Label _reserveLabel, _reserveUnit, _languageLabel;
    private Label _colApp, _colGain, _colMute, _colGuard;
    private Button _tweakButton, _setupButton, _devicesButton, _guideButton, _downloadButton;
    private Button _resetButton, _removeButton;
    private ToolStripMenuItem _trayShow, _trayReset, _trayExit;

    public MainForm()
    {
        // Jazyk musime znat driv, nez se postavi rozhrani.
        _store.Load();
        S.Current = _store.Language;

        _engine = new MixerEngine(_preamp);

        Text = "App Amplifier";
        ClientSize = new Size(700, 640);
        MinimumSize = new Size(716, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.FromArgb(248, 248, 248) };
        _status.SetBounds(12, 8, 660, 18);
        _status.Font = new Font(Font, FontStyle.Bold);
        _hint.SetBounds(12, 28, 660, 24);
        _hint.ForeColor = Color.FromArgb(90, 90, 90);
        header.Controls.Add(_status);
        header.Controls.Add(_hint);

        _list.Dock = DockStyle.Fill;
        _list.AutoScroll = true;
        _list.FlowDirection = FlowDirection.TopDown;
        _list.WrapContents = false;
        _list.BackColor = Color.White;

        BuildColumns();
        var footer = BuildFooter();

        // Poradi rozhoduje o zakotveni: co se prida pozdeji, sedne blize k okraji.
        Controls.Add(_list);
        Controls.Add(_columns);
        Controls.Add(footer);
        Controls.Add(header);

        SetupTray();
        ApplyTexts();

        // Uplna enumerace session je draha (COM objekty na kazdem vystupu).
        // Nove aplikace hlasi IAudioSessionNotification okamzite, takze tohle
        // uz jen uklizi zmizele - jednou za sekundu bohate staci.
        _refreshTimer.Interval = 1000;
        _refreshTimer.Tick += (_, _) => RefreshList();

        // Hlidac vzorkuje rychle, ale prekreslovat UI stejne casto nema smysl.
        _meterTimer.Interval = MixerEngine.TickMs;
        _meterTimer.Tick += (_, _) => UpdateMeters();

        _uiTimer.Interval = 40;
        _uiTimer.Tick += (_, _) =>
        {
            // schovane okno nema co prekreslovat; v tray tak nic nestoji
            bool visible = Visible && WindowState != FormWindowState.Minimized;
            _engine.MetersVisible = visible;
            if (!visible) return;
            foreach (var row in _rows.Values) row.UpdateMeter();
        };

        // Prekreslovani UI vyrabi drobny kratkodoby odpad. Neni to unik - pocty
        // objektu jsou stabilni - ale .NET s uklidem ceka, az narazi na svuj
        // limit, a do te doby pamet procesu roste. U programu, ktery ma cely den
        // sedet v oznamovaci oblasti, to vypada spatne, tak si o uklid rekneme
        // sami. Uklid gen0/gen1 par megabajtu trva zlomek milisekundy a nic
        // nezdrzi - zvuk beztak resi audio engine, ne nase vlakno.
        _gcTimer.Interval = 30000;
        _gcTimer.Tick += (_, _) =>
            GC.Collect(1, GCCollectionMode.Forced, blocking: false, compacting: false);

        // Diagnostika pameti se zapina prepinacem --diag. Ukazuje spravovanou
        // haldu a pocty objektu, aby bylo poznat, jestli neco pribyva.
        if (Environment.GetCommandLineArgs().Any(a =>
                a.Equals("--diag", StringComparison.OrdinalIgnoreCase)))
        {
            var diag = new System.Windows.Forms.Timer { Interval = 1000 };
            diag.Tick += (_, _) =>
            {
                double managed = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
                Text = $"GC {managed:0.00} MB | gen0 {GC.CollectionCount(0)} " +
                       $"gen2 {GC.CollectionCount(2)} | {_engine.DiagnosticCounts} " +
                       $"| radku {_rows.Count}";
            };
            diag.Start();
        }

        // Zamerne zadny casovac na ukladani. Na disk se sahne jen pri potvrzeni
        // nastavení a pri ukonceni - zadny zapis na pozadi.
    }

    /// <summary>
    /// Zahlavi seznamu. Bez nej byla sila hlidace jen dalsi cislo v rade -
    /// popisky sedi presne nad sloupci, protoze radky maji pevne pozice.
    /// </summary>
    private void BuildColumns()
    {
        _columns.Dock = DockStyle.Top;
        _columns.Height = 22;
        _columns.BackColor = Color.FromArgb(245, 245, 245);

        var small = new Font("Segoe UI", 8f, FontStyle.Bold);
        var color = Color.FromArgb(115, 115, 115);

        Label Column(int left, int width, ContentAlignment align) => new()
        {
            Bounds = new Rectangle(left, 3, width, 16),
            Font = small,
            ForeColor = color,
            TextAlign = align,
        };

        _colApp = Column(52, 200, ContentAlignment.MiddleLeft);
        _colGain = Column(260, 220, ContentAlignment.MiddleCenter);
        _colMute = Column(486, 86, ContentAlignment.MiddleRight);
        _colGuard = Column(AppRow.WatchdogX - 6, 60, ContentAlignment.MiddleCenter);

        _columns.Controls.AddRange(new Control[] { _colApp, _colGain, _colMute, _colGuard });
        _columns.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(222, 222, 222));
            e.Graphics.DrawLine(pen, 0, _columns.Height - 1, _columns.Width, _columns.Height - 1);
            // stejna delici cara jako v radcich, aby sloupec hlidace drzel pohromade
            e.Graphics.DrawLine(pen, AppRow.WatchdogX - 12, 2, AppRow.WatchdogX - 12, _columns.Height - 2);
        };
    }

    /// <summary>
    /// Dolni lista. Rozdelena na radky podle toho, ceho se nastaveni tyka
    /// (zvuk / hlidac / zobrazeni / chovani / Equalizer APO), akce jsou zvlast
    /// dole vpravo.
    /// </summary>
    private Panel BuildFooter()
    {
        const int CaptionX = 14;
        const int ContentX = 110;

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 220 };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(222, 222, 222));
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            int y = footer.Height - 42;
            e.Graphics.DrawLine(pen, CaptionX, y, footer.Width - CaptionX, y);
        };

        Label Caption(int top) => new()
        {
            Left = CaptionX,
            Top = top,
            Width = 90,
            ForeColor = Color.FromArgb(120, 120, 120),
            Font = new Font(Font, FontStyle.Bold),
        };

        // --- radek 1: zvuk -------------------------------------------------
        _capSound = Caption(16);
        _protect.SetBounds(ContentX, 14, 128, 20);
        _protect.Checked = true;
        _protect.CheckedChanged += (_, _) => OnProtectionToggled();

        _limitBox.SetBounds(242, 12, 56, 24);
        _limitBox.Minimum = 100;
        _limitBox.Maximum = MixerEngine.MaxGainPercent;
        _limitBox.Increment = 10;
        _limitBox.TextAlign = HorizontalAlignment.Right;
        _limitBox.ValueChanged += (_, _) => ApplyLimitToRows();

        var percentSign = new Label { Text = "%", Left = 302, Top = 16, Width = 16 };

        _reserveLabel = new Label { Left = 344, Top = 16, Width = 108 };
        _reserveBox.SetBounds(456, 12, 62, 24);
        _reserveBox.Minimum = 0;
        _reserveBox.Maximum = 12;
        _reserveBox.DecimalPlaces = 1;
        _reserveBox.Increment = 0.5m;
        _reserveBox.TextAlign = HorizontalAlignment.Right;
        _reserveBox.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _store.BaseHeadroomDb = (double)_reserveBox.Value;
            _engine.BaseHeadroomDb = _store.BaseHeadroomDb;
            _engine.Apply();
            UpdateStatus();
        };
        _reserveUnit = new Label { Text = "dB", Left = 522, Top = 16, Width = 24 };

        // --- radek 2: hlidac ------------------------------------------------
        _capWatchdog = Caption(48);
        _watchdog.SetBounds(ContentX, 46, 172, 20);
        _watchdog.CheckedChanged += (_, _) =>
        {
            _engine.WatchdogEnabled = _watchdog.Checked;
            if (!_loading) _store.WatchdogOn = _watchdog.Checked;
            int mark = CeilingMark;
            foreach (var row in _rows.Values)
            {
                row.SetCeilingMark(mark);
                row.SetWatchdogAvailable(_watchdog.Checked);
            }
            _engine.Apply();
            UpdateStatus();
        };

        _tweakButton = new Button { Bounds = new Rectangle(290, 43, 92, 26) };
        _tweakButton.Click += (_, _) => OpenWatchdogSettings();

        // --- radek 3: zobrazeni a jazyk -------------------------------------
        _capDisplay = Caption(80);
        _peakOnSlider.SetBounds(ContentX, 78, 214, 20);
        _peakOnSlider.Checked = true;
        _peakOnSlider.CheckedChanged += (_, _) =>
        {
            if (!_loading) _store.PeakOnSlider = _peakOnSlider.Checked;
            foreach (var row in _rows.Values) row.SetShowPeakOnSlider(_peakOnSlider.Checked);
        };

        _languageLabel = new Label { Left = 344, Top = 80, Width = 72 };
        _language.SetBounds(420, 76, 126, 24);
        _language.DropDownStyle = ComboBoxStyle.DropDownList;
        _language.Items.AddRange(new object[] { "English", "Čeština" });
        _language.SelectedIndex = S.Current == Lang.Czech ? 1 : 0;
        _language.SelectedIndexChanged += (_, _) => OnLanguageChanged();

        // --- radek 4: chovani aplikace -------------------------------------
        _capApp = Caption(112);
        _autostart.SetBounds(ContentX, 110, 152, 20);
        _autostart.CheckedChanged += (_, _) => OnAutostartToggled();

        _background.SetBounds(274, 110, 200, 20);
        _background.CheckedChanged += (_, _) =>
        {
            if (!_loading) _store.BackgroundOn = _background.Checked;
        };

        // --- radek 5: Equalizer APO -----------------------------------------
        _capApo = Caption(146);
        _setupButton = new Button { Bounds = new Rectangle(ContentX, 142, 112, 26) };
        _setupButton.Click += (_, _) => SetUpEqualizerApo();

        _devicesButton = new Button { Bounds = new Rectangle(226, 142, 122, 26) };
        _devicesButton.Click += (_, _) =>
        {
            if (!_preamp.LaunchDeviceSelector(out string problem))
                Warn(problem);
        };

        _guideButton = new Button { Bounds = new Rectangle(352, 142, 92, 26) };
        _guideButton.Click += (_, _) =>
        {
            using var guide = new GuideForm(_preamp);
            guide.ShowDialog(this);
        };

        _downloadButton = new Button { Bounds = new Rectangle(448, 142, 136, 26) };
        _downloadButton.Click += (_, _) => OpenUrl(PreampBackend.DownloadUrl);

        // --- akce ------------------------------------------------------------
        // Rucni kotveni doprava tu nefunguje: panel jeste nema konecnou sirku,
        // takze by se odstup od praveho okraje spocital ze spatne hodnoty a
        // tlacitka by pri roztazeni vyletela mimo okno. Layout si to spocita sam.
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 6, CaptionX, 0),
            WrapContents = false,
        };

        _removeButton = new Button { Width = 190, Height = 26 };
        _removeButton.Click += (_, _) => RemoveConfig();

        _resetButton = new Button { Width = 120, Height = 26, Margin = new Padding(0, 0, 8, 0) };
        _resetButton.Click += (_, _) => ResetAll();

        actions.Controls.Add(_removeButton);
        actions.Controls.Add(_resetButton);

        footer.Controls.AddRange(new Control[]
        {
            _capSound, _protect, _limitBox, percentSign, _reserveLabel, _reserveBox, _reserveUnit,
            _capWatchdog, _watchdog, _tweakButton,
            _capDisplay, _peakOnSlider, _languageLabel, _language,
            _capApp, _autostart, _background,
            _capApo, _setupButton, _devicesButton, _guideButton, _downloadButton,
            actions,
        });

        return footer;
    }

    /// <summary>
    /// Prepise vsechny texty podle zvoleneho jazyka. Radky seznamu nesou jen
    /// cisla, takze staci prekreslit bubliny - o ty se postara samotny AppRow.
    /// </summary>
    private void ApplyTexts()
    {
        Text = S.T("App Amplifier", "Zesilovač aplikací");

        _capSound.Text = S.T("Sound", "Zvuk");
        _capWatchdog.Text = S.T("Watchdog", "Hlídač");
        _capDisplay.Text = S.T("Display", "Zobrazení");
        _capApp.Text = S.T("App", "Aplikace");
        _capApo.Text = "Equalizer APO";

        _protect.Text = S.T("Volume protection", "Ochrana hlasitosti");
        _reserveLabel.Text = S.T("Base headroom", "Základní rezerva");
        _watchdog.Text = S.T("Loud sound watchdog", "Hlídač hlasitých zvuků");
        _tweakButton.Text = S.T("Tweaks…", "Tweaky…");
        _peakOnSlider.Text = S.T("Level meter on the slider", "Ukazatel hlasitosti na slideru");
        _languageLabel.Text = S.T("Language", "Jazyk");
        _autostart.Text = S.T("Start with Windows", "Spouštět s Windows");
        _background.Text = S.T("Closing keeps it running", "Zavřením běžet na pozadí");

        _setupButton.Text = S.T("Set up", "Nastavit");
        _devicesButton.Text = S.T("Devices…", "Zařízení…");
        _guideButton.Text = S.T("Guide", "Návod");
        _downloadButton.Text = S.T("Download APO", "Stáhnout APO");
        _resetButton.Text = S.T("All to 100 %", "Vše na 100 %");
        _removeButton.Text = S.T("Remove from Equalizer APO", "Odebrat z Equalizer APO");

        _colApp.Text = S.T("Application", "Aplikace");
        _colGain.Text = S.T("Gain", "Zesílení");
        _colMute.Text = S.T("Volume · mute", "Hlasitost · ztlumení");
        _colGuard.Text = S.T("Watchdog", "Hlídač");

        _tray.Text = Text;
        _trayShow.Text = S.T("Show", "Zobrazit");
        _trayReset.Text = _resetButton.Text;
        _trayExit.Text = S.T("Exit", "Ukončit");

        UpdateStatus();
    }

    private void OnLanguageChanged()
    {
        S.Current = _language.SelectedIndex == 1 ? Lang.Czech : Lang.English;
        if (!_loading)
        {
            _store.Language = S.Current;
            _store.Save();
        }
        ApplyTexts();
    }

    private void SetupTray()
    {
        try { _tray.Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); }
        catch { _tray.Icon = SystemIcons.Application; }

        _tray.Visible = true;

        // Jedno kliknuti staci. Pravym tlacitkem otevrit nabidku a teprve v ni
        // dat Zobrazit je o dva kroky vic, nez by clovek cekal.
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowFromTray();
        };

        _trayShow = new ToolStripMenuItem(string.Empty, null, (_, _) => ShowFromTray());
        _trayReset = new ToolStripMenuItem(string.Empty, null, (_, _) => ResetAll());
        _trayExit = new ToolStripMenuItem(string.Empty, null, (_, _) => ExitApplication());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_trayShow);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_trayReset);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_trayExit);
        _tray.ContextMenuStrip = menu;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        BringToFront();
        Activate();
    }

    private void HideToTray(string reason)
    {
        Hide();
        ShowInTaskbar = false;
        if (!_store.TrayHintShown)
        {
            _tray.ShowBalloonTip(4000, Text,
                reason + " " + S.T("It keeps running in the background and volumes stay watched.",
                                   "Běží dál na pozadí, hlasitosti se hlídají."),
                ToolTipIcon.Info);
            _store.TrayHintShown = true;
        }
    }

    private void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    /// <summary>Otevre odkaz ve vychozim prohlizeci.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
        }
        catch { /* bez prohlizece uz nemame co nabidnout */ }
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        _loading = true;
        _limitBox.Value = _store.Limit;
        _protect.Checked = _store.ProtectionOn;
        _limitBox.Enabled = _protect.Checked;
        _watchdog.Checked = _store.WatchdogOn;
        _background.Checked = _store.BackgroundOn;
        _peakOnSlider.Checked = _store.PeakOnSlider;
        _reserveBox.Value = (decimal)_store.BaseHeadroomDb;
        _engine.BaseHeadroomDb = _store.BaseHeadroomDb;
        _autostart.Checked = Autostart.IsEnabled;
        _engine.WatchdogEnabled = _watchdog.Checked;
        ApplyWatchdogSettings();
        _loading = false;

        _devicesButton.Enabled = _preamp.IsAvailable;
        _setupButton.Enabled = _preamp.IsAvailable;

        // Callback chodi z audio enginu a musi se vratit okamzite, takze obnovu
        // jen posleme na UI vlakno. Zaroven ji slucujeme: kdyz aplikace jako
        // prohlizec otevre deset streamu za sebou, nema smysl desetkrat projit
        // celou enumeraci - stejne by videla stejny vysledek.
        _engine.SessionsChanged += () =>
        {
            if (_refreshQueued || _systemShutdown) return;
            _refreshQueued = true;
            try
            {
                BeginInvoke(() =>
                {
                    _refreshQueued = false;
                    if (!_systemShutdown) RefreshList();
                });
            }
            catch { _refreshQueued = false; }
        };

        if (!_preamp.IsAvailable)
        {
            var answer = MessageBox.Show(this,
                S.T("Equalizer APO was not found.\n\n" +
                    "Without it the app cannot go above 100 % without adding latency — it will run " +
                    "in ratio mode: other apps get quieter and you have to raise the master volume " +
                    "yourself by the difference.\n\nOpen the download page?",
                    "Equalizer APO nebylo nalezeno.\n\n" +
                    "Bez něj nelze jít nad 100 % bez přidané latence — aplikace poběží " +
                    "v poměrovém režimu: ostatní aplikace se ztlumí a o rozdíl si musíš " +
                    "sám zvýšit hlavní hlasitost.\n\nOtevřít stránku ke stažení?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (answer == DialogResult.Yes) OpenUrl(PreampBackend.DownloadUrl);
        }
        else if (!_preamp.EnsureIncluded(out string problem))
        {
            Warn(S.T("Could not write to the Equalizer APO configuration:\n",
                     "Nepodařilo se zapsat do konfigurace Equalizer APO:\n") + problem);
        }

        Autostart.RefreshPathIfEnabled();

        RefreshList();
        _refreshTimer.Start();
        _meterTimer.Start();
        _uiTimer.Start();
        _gcTimer.Start();

        // spusteno autostartem -> rovnou na pozadi, at neskace okno pri prihlaseni
        if (Environment.GetCommandLineArgs().Any(a =>
                a.Equals(Autostart.TrayArgument, StringComparison.OrdinalIgnoreCase)))
        {
            BeginInvoke(() => { Hide(); ShowInTaskbar = false; });
        }
    }

    /// <summary>
    /// Zapoji zesilovac do Equalizer APO a zapne v jeho editoru okamzity rezim.
    /// Vyber zarizeni zamerne nedelame za uzivatele - bezi v samostatnem
    /// nastroji s pravy spravce a nikdo jiny nevi, ktery vystup chce zesilovat.
    /// </summary>
    private void SetUpEqualizerApo()
    {
        if (!_preamp.IsAvailable)
        {
            Warn(S.T("Equalizer APO is not installed. Use the Download button first.",
                     "Equalizer APO není nainstalované. Použij nejdřív tlačítko Stáhnout."));
            return;
        }

        var report = new System.Text.StringBuilder();

        if (_preamp.EnsureIncluded(out string problem))
        {
            report.AppendLine(S.T("• Amplifier hooked into config.txt (your own EQ and preamp kept).",
                                  "• Zesilovač zapojen do config.txt (tvůj EQ i preamp zůstávají)."));
        }
        else
        {
            report.AppendLine(S.T("• Could not write to config.txt: ",
                                  "• Nepodařilo se zapsat do config.txt: ") + problem);
        }

        if (PreampBackend.SetEditorInstantMode(out string regProblem))
        {
            report.AppendLine(S.T("• Instant mode turned on — the editor now applies and saves changes " +
                                  "as you type. If the editor is open, restart it.",
                                  "• Okamžitý režim zapnut — editor teď ukládá a promítá změny hned " +
                                  "při psaní. Pokud je editor otevřený, spusť ho znovu."));
        }
        else
        {
            report.AppendLine(S.T("• Could not turn on instant mode: ",
                                  "• Nepodařilo se zapnout okamžitý režim: ") + regProblem);
        }

        if (!_preamp.CanWrite())
        {
            report.AppendLine(S.T("• Warning: the config folder is not writable. Run the app as " +
                                  "administrator, or reinstall Equalizer APO into a writable folder.",
                                  "• Pozor: do složky s konfigurací nejde zapisovat. Spusť aplikaci " +
                                  "jako správce, nebo Equalizer APO přeinstaluj do zapisovatelné složky."));
        }

        report.AppendLine();
        report.AppendLine(S.T("Still to do by hand: tick the output device you want amplified " +
                              "(Devices… button) — Equalizer APO does nothing on devices that are " +
                              "not ticked.",
                              "Zbývá udělat ručně: zaškrtnout výstup, který chceš zesilovat " +
                              "(tlačítko Zařízení…) — na nezaškrtnutých zařízeních Equalizer APO " +
                              "nedělá nic."));

        _engine.Apply();
        UpdateStatus();

        MessageBox.Show(this, report.ToString(), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OpenWatchdogSettings()
    {
        using var dialog = new WatchdogSettingsForm(
            _store.WatchdogCeiling, _store.WatchdogFloor,
            _store.WatchdogAttackMs, _store.WatchdogReleaseMs, _store.WatchdogSmoothness);

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _store.WatchdogCeiling = dialog.CeilingPercent;
        _store.WatchdogFloor = dialog.FloorPercent;
        _store.WatchdogAttackMs = dialog.AttackMs;
        _store.WatchdogReleaseMs = dialog.ReleaseMs;
        _store.WatchdogSmoothness = dialog.SmoothnessPercent;
        ApplyWatchdogSettings();
        _store.Save();   // po potvrzeni dialogu zapisujeme rovnou
    }

    private void ApplyWatchdogSettings()
    {
        _engine.Ceiling = _store.WatchdogCeiling / 100.0;
        _engine.Floor = _store.WatchdogFloor / 100.0;
        _engine.AttackMs = _store.WatchdogAttackMs;
        _engine.ReleaseMs = _store.WatchdogReleaseMs;
        _engine.Smoothness = _store.WatchdogSmoothness / 100.0;

        int mark = CeilingMark;
        foreach (var row in _rows.Values) row.SetCeilingMark(mark);

        _engine.Apply();
    }

    /// <summary>Znacka stropu se ukazuje jen kdyz hlidac opravdu bezi.</summary>
    private int CeilingMark => _watchdog.Checked ? _store.WatchdogCeiling : 0;

    private void OnAutostartToggled()
    {
        if (_loading) return;

        if (!Autostart.Set(_autostart.Checked, out string problem))
        {
            Warn(S.T("Could not change the Windows startup entry:\n",
                     "Nepodařilo se změnit spouštění s Windows:\n") + problem);
            _loading = true;
            _autostart.Checked = Autostart.IsEnabled;
            _loading = false;
        }
    }

    // ------------------------------------------------------------------

    private void RefreshList()
    {
        _engine.Refresh();

        // Uzivatel muze menit preamp v editoru Equalizer APO za behu. Kdybychom
        // ho cetli jen pri zmene seznamu aplikaci, pocitali bychom se starou
        // hodnotou a vysledna hlasitost by nesedela.
        double externalBefore = _preamp.ExternalDb;
        _preamp.RefreshExternalDb();
        bool changed = Math.Abs(externalBefore - _preamp.ExternalDb) > 0.01;
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in _engine.Apps.OrderByDescending(a => a.HasActiveSession)
                                        .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            live.Add(app.Key);
            if (_rows.ContainsKey(app.Key)) continue;

            // obnoveni drive nastavene hlasitosti pro tuto aplikaci
            if (_store.TryGet(app.Key, out double saved)) app.Gain = saved;
            if (_store.TryGetStrength(app.Key, out double strength)) app.WatchdogStrength = strength;

            var row = new AppRow(app) { Width = _list.ClientSize.Width - 24 };
            row.SetLimit(EffectiveLimit);
            row.SetShowPeakOnSlider(_peakOnSlider.Checked);
            row.SetCeilingMark(CeilingMark);
            row.SetWatchdogAvailable(_watchdog.Checked);
            row.GainChanged += OnGainChanged;
            row.MuteChanged += OnMuteChanged;
            row.WatchdogStrengthChanged += OnWatchdogStrengthChanged;
            _rows[app.Key] = row;
            _list.Controls.Add(row);
            changed = true;
        }

        foreach (var key in _rows.Keys.ToList())
        {
            if (live.Contains(key)) continue;
            var row = _rows[key];
            _list.Controls.Remove(row);
            row.Dispose();
            _rows.Remove(key);
            changed = true;
        }

        if (changed) _engine.Apply();
        UpdateStatus();
    }

    /// <summary>Kam az smi dojet slider. Pri zapnute ochrane je to nastaveny strop.</summary>
    private int EffectiveLimit => _protect.Checked ? (int)_limitBox.Value : MixerEngine.MaxGainPercent;

    private void OnProtectionToggled()
    {
        if (!_loading && !_protect.Checked)
        {
            double db = PreampBackend.GainToDb(MixerEngine.MaxGainPercent / 100.0);
            var answer = MessageBox.Show(this,
                S.T($"Turn volume protection off?\n\n" +
                    $"Sliders will then go up to {MixerEngine.MaxGainPercent} % (+{db:0.0} dB). " +
                    "In headphones that can be unpleasantly loud and loud material starts to clip.",
                    $"Vypnout ochranu hlasitosti?\n\n" +
                    $"Slidery pak půjdou až na {MixerEngine.MaxGainPercent} % (+{db:0.0} dB). " +
                    "Ve sluchátkách to může být nepříjemně hlasité a hlasitý materiál začne ořezávat."),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
            {
                _protect.Checked = true;   // vrati se zpet, ApplyLimitToRows dobehne odtud
                return;
            }
        }

        _limitBox.Enabled = _protect.Checked;
        _store.ProtectionOn = _protect.Checked;
        _store.Save();
        ApplyLimitToRows();
    }

    private void ApplyLimitToRows()
    {
        if (_loading) return;
        _store.Limit = (int)_limitBox.Value;
        int limit = EffectiveLimit;
        foreach (var row in _rows.Values) row.SetLimit(limit);
        UpdateStatus();
    }

    private void OnGainChanged(AppEntry app, double gain)
    {
        _engine.SetGain(app, gain);
        _store.Set(app.Key, gain);   // na disk az pri ukonceni
        UpdateStatus();
    }

    private void OnMuteChanged(AppEntry app, bool muted)
    {
        _engine.SetMuted(app, muted);
        UpdateStatus();
    }

    private void OnWatchdogStrengthChanged(AppEntry app, double strength)
    {
        _engine.SetWatchdogStrength(app, strength);
        _store.SetStrength(app.Key, strength);   // na disk az pri ukonceni
        UpdateStatus();
    }

    /// <summary>Jen vypocet hlidace, zadne kresleni - to obstara _uiTimer.</summary>
    private void UpdateMeters()
    {
        _engine.UpdateMeters();

        // Kdyz neni co hlidat, nema smysl vzorkovat sedesatkrat za sekundu.
        int wanted = _engine.NeedsFastTick ? MixerEngine.TickMs : MixerEngine.IdleTickMs;
        if (_meterTimer.Interval != wanted) _meterTimer.Interval = wanted;
    }

    private void UpdateStatus()
    {
        double totalDb = _preamp.IsAvailable ? _preamp.TotalDb : _engine.HeadroomDb;
        int headroomPct = (int)Math.Round(_engine.Headroom * 100);

        if (!_preamp.IsAvailable)
        {
            _status.Text = S.T($"Ratio mode — headroom needed {headroomPct} % ({totalDb:0.0} dB)",
                               $"Poměrový režim — potřebná rezerva {headroomPct} % ({totalDb:0.0} dB)");
            _hint.Text = S.T("Equalizer APO is missing. Raise your master or headphone volume by that much.",
                             "Equalizer APO chybí. Zvyš si o tolik hlavní hlasitost nebo hlasitost na sluchátkách.");
            return;
        }

        _status.Text = S.T(
            $"Device headroom {headroomPct} %  ({totalDb:0.0} dB)  •  " +
            $"your preamp {_preamp.ExternalDb:0.0} dB + app {_preamp.OwnDb:0.0} dB",
            $"Rezerva zařízení {headroomPct} %  ({totalDb:0.0} dB)  •  " +
            $"tvůj preamp {_preamp.ExternalDb:0.0} dB + aplikace {_preamp.OwnDb:0.0} dB");

        int reservePct = (int)Math.Round(PreampBackend.DbToGain(_store.BaseHeadroomDb) * 100);
        _hint.Text = _preamp.OwnDb > 0.1
            ? S.T($"The app holds {reservePct} % headroom itself and writes the difference above it. Latency stays zero.",
                  $"Rezerva {reservePct} % drží aplikace sama; nad ni si dopisuje rozdíl. Latence zůstává nulová.")
            : S.T($"Your own preamp already covers the {reservePct} % headroom, the app adds nothing yet.",
                  $"Rezervu {reservePct} % pokrývá tvůj vlastní preamp, aplikace zatím nic nepřidává.");
    }

    private void ResetAll()
    {
        foreach (var row in _rows.Values) row.SetGain(1.0, notify: false);
        foreach (var app in _engine.Apps) { app.Gain = 1.0; app.Muted = false; _store.Set(app.Key, 1.0); }
        _engine.Apply();
        UpdateStatus();
    }

    private void RemoveConfig()
    {
        var answer = MessageBox.Show(this,
            S.T("Remove the changes made to Equalizer APO?\n\n" +
                "The Include line will be deleted from config.txt and ZesilovacAplikaci.txt removed. " +
                "Your own preamp and EQ stay. The app will then close.",
                "Odebrat zásah do Equalizer APO?\n\n" +
                "Z config.txt se smaže řádek Include a smaže se soubor ZesilovacAplikaci.txt. " +
                "Tvůj vlastní preamp a EQ zůstanou. Aplikace se poté ukončí."),
            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        _engine.RevertToDefaults();
        _preamp.RemoveFromConfig();
        _exiting = true;
        Close();
    }

    // Vypinani a odhlaseni Windows. Na obe zpravy musime odpovedet hned;
    // dokud neodpovime, Windows ceka a uzivateli sviti obrazovka s tim, ze
    // aplikace brani vypnuti.
    private const int WM_QUERYENDSESSION = 0x0011;
    private const int WM_ENDSESSION = 0x0016;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg is WM_QUERYENDSESSION or WM_ENDSESSION) PrepareForShutdown();
        base.WndProc(ref m);
    }

    /// <summary>
    /// Sklidit vsechno, co by pri vypinani mohlo blokovat: casovace prestanou
    /// sahat do audio enginu, nastaveni se ulozi a preamp se srovna zapisem do
    /// souboru. Zadne dalsi COM volani uz nenasleduje.
    /// </summary>
    private void PrepareForShutdown()
    {
        if (_systemShutdown) return;
        _systemShutdown = true;
        _exiting = true;

        _refreshTimer.Stop();
        _meterTimer.Stop();
        _uiTimer.Stop();
        _gcTimer.Stop();

        _engine.ShutdownFast();
        _store.Save();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
            PrepareForShutdown();

        // Krizek pri zapnutem behu na pozadi jen schova okno. Ukoncuje se
        // z tray menu - jinak by se hlasitosti necekane vratily do puvodniho stavu.
        if (!_exiting && _background.Checked && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray(S.T("The amplifier stayed running.", "Zesilovač zůstal spuštěný."));
            _store.Save();
            return;
        }

        _refreshTimer.Stop();
        _meterTimer.Stop();
        _uiTimer.Stop();
        _store.Save();
        _tray.Visible = false;
        _tray.Dispose();
        _engine.Dispose();
        base.OnFormClosing(e);
    }

    /// <summary>
    /// Predani rizeni pri ukonceni i pri padu - nas preamp jde pryc a hlasitosti
    /// se prepocitaji tak, aby zvuk znel dal stejne i bez bezici aplikace.
    /// </summary>
    public void RestoreEverything() => _engine.HandOff();

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (WindowState == FormWindowState.Minimized && _background.Checked)
        {
            HideToTray(S.T("The amplifier keeps running.", "Zesilovač běží dál."));
            return;
        }

        foreach (var row in _rows.Values) row.Width = _list.ClientSize.Width - 24;
    }
}

/// <summary>Pamatuje si nastavene hlasitosti podle cesty k .exe.</summary>
internal sealed class GainStore
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ZesilovacAplikaci", "hlasitosti.json");

    // Klice jsou cesty k .exe, tyhle jsou rezervovane a nemohou s nimi kolidovat.
    private const string LimitKey = "@@limit";
    private const string ProtectKey = "@@ochrana";
    private const string WatchdogKey = "@@hlidac";
    private const string BackgroundKey = "@@pozadi";
    private const string TrayHintKey = "@@trayhint";
    private const string CeilingKey = "@@strop";
    private const string FloorKey = "@@podlaha";
    private const string PeakSliderKey = "@@metrslider";
    private const string SmoothKey = "@@plynulost";
    private const string ReserveKey = "@@rezerva";
    private const string AttackKey = "@@najezd";
    private const string ReleaseKey = "@@navrat";
    private const string LanguageKey = "@@jazyk";

    private Dictionary<string, double> _values = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirty;

    /// <summary>Strop hlasitosti v procentech. Slider za nej nepusti.</summary>
    public int Limit
    {
        get => _values.TryGetValue(LimitKey, out double v)
            ? (int)Math.Clamp(v, 100, MixerEngine.MaxGainPercent)
            : 150;
        set { _values[LimitKey] = Math.Clamp(value, 100, MixerEngine.MaxGainPercent); _dirty = true; }
    }

    /// <summary>Jazyk rozhrani. Vychozi je anglictina.</summary>
    public Lang Language
    {
        get => _values.TryGetValue(LanguageKey, out double v) && v == 1 ? Lang.Czech : Lang.English;
        set { _values[LanguageKey] = value == Lang.Czech ? 1 : 0; _dirty = true; }
    }

    /// <summary>Ochrana hlasitosti. Vychozi je zapnuta.</summary>
    public bool ProtectionOn
    {
        get => !_values.TryGetValue(ProtectKey, out double v) || v != 0;
        set { _values[ProtectKey] = value ? 1 : 0; _dirty = true; }
    }

    /// <summary>Hlidac hlasitych zvuku. Vychozi je zapnuty.</summary>
    public bool WatchdogOn
    {
        get => !_values.TryGetValue(WatchdogKey, out double v) || v != 0;
        set { _values[WatchdogKey] = value ? 1 : 0; _dirty = true; }
    }

    /// <summary>Zavreni okna nechava aplikaci bezet v oznamovaci oblasti.</summary>
    public bool BackgroundOn
    {
        get => !_values.TryGetValue(BackgroundKey, out double v) || v != 0;
        set { _values[BackgroundKey] = value ? 1 : 0; _dirty = true; }
    }

    /// <summary>Rezerva, kterou si aplikace drzi trvale (dB). Vychozi 3,5 = 150 %.</summary>
    public double BaseHeadroomDb
    {
        get => _values.TryGetValue(ReserveKey, out double v) ? Math.Clamp(v, 0, 12) : 3.5;
        set { _values[ReserveKey] = Math.Clamp(value, 0, 12); _dirty = true; }
    }

    /// <summary>Strop hlidace v procentech.</summary>
    public int WatchdogCeiling
    {
        get => _values.TryGetValue(CeilingKey, out double v) ? (int)Math.Clamp(v, 10, 120) : 100;
        set { _values[CeilingKey] = Math.Clamp(value, 10, 120); _dirty = true; }
    }

    /// <summary>Nejnizsi hlasitost, na kterou smi hlidac stahnout (%).</summary>
    public int WatchdogFloor
    {
        get => _values.TryGetValue(FloorKey, out double v) ? (int)Math.Clamp(v, 10, 100) : 100;
        set { _values[FloorKey] = Math.Clamp(value, 10, 100); _dirty = true; }
    }

    /// <summary>Nabeh utlumu v ms.</summary>
    public int WatchdogAttackMs
    {
        get => _values.TryGetValue(AttackKey, out double v) ? (int)Math.Clamp(v, 1, 500) : 100;
        set { _values[AttackKey] = Math.Clamp(value, 1, 500); _dirty = true; }
    }

    /// <summary>Navrat zesileni v ms.</summary>
    public int WatchdogReleaseMs
    {
        get => _values.TryGetValue(ReleaseKey, out double v) ? (int)Math.Clamp(v, 100, 3000) : 1000;
        set { _values[ReleaseKey] = Math.Clamp(value, 100, 3000); _dirty = true; }
    }

    /// <summary>Tvar prechodu hlidace v procentech (0 rampa, 100 exponencialni).</summary>
    public int WatchdogSmoothness
    {
        get => _values.TryGetValue(SmoothKey, out double v) ? (int)Math.Clamp(v, 0, 100) : 100;
        set { _values[SmoothKey] = Math.Clamp(value, 0, 100); _dirty = true; }
    }

    /// <summary>Ukazatel hlasitosti primo v draze slideru. Vychozi zapnuty.</summary>
    public bool PeakOnSlider
    {
        get => !_values.TryGetValue(PeakSliderKey, out double v) || v != 0;
        set { _values[PeakSliderKey] = value ? 1 : 0; _dirty = true; }
    }

    /// <summary>Bublinu o behu na pozadi ukazujeme jen jednou.</summary>
    public bool TrayHintShown
    {
        get => _values.TryGetValue(TrayHintKey, out double v) && v != 0;
        set { _values[TrayHintKey] = value ? 1 : 0; _dirty = true; }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(_path));
            if (loaded != null) _values = new Dictionary<string, double>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch { /* poskozeny soubor proste ignorujeme */ }
    }

    public bool TryGet(string key, out double gain)
    {
        gain = 1.0;
        if (key.StartsWith("@@", StringComparison.Ordinal)) return false;
        return _values.TryGetValue(key, out gain);
    }

    /// <summary>
    /// Sila hlidace se uklada pod vlastni predponou, aby nekolidovala s hlasitosti
    /// tehoz .exe. Cesty k souborum takovy prefix mit nemuzou.
    /// </summary>
    private const string StrengthPrefix = "@sila|";

    public bool TryGetStrength(string key, out double strength)
    {
        strength = 1.0;
        if (key.StartsWith("@@", StringComparison.Ordinal)) return false;
        if (!_values.TryGetValue(StrengthPrefix + key, out strength)) return false;
        strength = Math.Clamp(strength, 0.0, 1.0);
        return true;
    }

    public void SetStrength(string key, double strength)
    {
        _values[StrengthPrefix + key] = Math.Clamp(strength, 0.0, 1.0);
        _dirty = true;
    }

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // Zapis pres docasny soubor, aby vypadek proudu uprostred zapisu
            // nenechal na disku useknuty JSON a nastaveni se neztratilo.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_values,
                new JsonSerializerOptions { WriteIndented = true }));

            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);

            _dirty = false;
        }
        catch { }
    }

    public void Set(string key, double gain)
    {
        _values[key] = gain;
        _dirty = true;
    }
}
