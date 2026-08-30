using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ZesilovacAplikaci;

/// <summary>Jedna session (jeden audio stream jedne aplikace).</summary>
internal sealed class SessionHandle
{
    public IAudioSessionControl2 Control;
    public ISimpleAudioVolume Volume;
    public IAudioMeterInformation Meter;
    public string InstanceId;
    public int ProcessId;
    public bool IsSystemSounds;
    public bool Seen;

    /// <summary>Naposledy zapsana hlasitost. Setri COM volani pri rychlem hlidani.</summary>
    public float LastSet = -1f;

    /// <summary>
    /// Session prestala odpovidat. Pri dalsim obnoveni ji zahodime a vytvorime
    /// znovu - lepsi nez aby tise prestala fungovat.
    /// </summary>
    public bool Broken;

    public AudioSessionState State
    {
        get
        {
            try { return Control.GetState(out var s) == 0 ? s : AudioSessionState.Expired; }
            catch { return AudioSessionState.Expired; }
        }
    }
}

/// <summary>Aplikace tak, jak ji vidi uzivatel ve smesovaci (muze mit vic session).</summary>
internal sealed class AppEntry
{
    public string Key;                 // cesta k .exe (mala pismena) nebo "@system"
    public string DisplayName;
    public string ExePath;
    public Icon Icon;
    public bool IsSystemSounds;

    public readonly List<SessionHandle> Sessions = new();

    /// <summary>Uzivatelem pozadovane zesileni. 1.0 = 100 %.</summary>
    public double Gain = 1.0;
    public bool Muted;

    /// <summary>Spicka pro metr, uz prepocitana na skutecne slysenou hlasitost.</summary>
    public float Peak;

    /// <summary>Surova spicka streamu aplikace (0-1), nezavisla na hlasitosti i preampu.</summary>
    public float RawPeak;

    /// <summary>
    /// Zesileni, ktere hlidac prave povoluje. Pri tichem zvuku se rovna Gain,
    /// pri hlasitem klesa k 1.0, aby vysledek neprelezl strop.
    /// </summary>
    public double DuckGain = 1.0;

    /// <summary>True, kdyz hlidac prave ubira (pro zobrazeni v UI).</summary>
    public bool IsDucking;

    /// <summary>Hlasitost, kterou u teto aplikace ukazuje smesovac Windows (0-1).</summary>
    public float WindowsVolume = 1f;

    /// <summary>
    /// Jak silne smi hlidac zasahovat prave do teto aplikace. 1.0 = plny zasah,
    /// 0.0 = aplikace se nehlida vubec. Mezilehle hodnoty ubiraji jen odpovidajici
    /// cast toho, co by hlidac jinak ubral - na nekterych aplikacich je totiz
    /// automaticke stahovani hlasitosti neprijemne.
    /// </summary>
    public double WatchdogStrength = 1.0;

    public bool HasActiveSession => Sessions.Any(s => s.State == AudioSessionState.Active);
}

/// <summary>
/// Drzi stav smesovace a prepocitava rozdeleni:
///   preamp (APO, spolecny pro cele zarizeni)  x  session volume (per aplikace)
///   = vysledne zesileni aplikace, ktere muze prekrocit 100 %.
/// </summary>
internal sealed class MixerEngine : IDisposable
{
    /// <summary>Strop zesileni. 400 % = +12 dB, coz uz je ve sluchatkach dost.</summary>
    public const int MaxGainPercent = 400;

    private readonly PreampBackend _preamp;
    private readonly List<IAudioSessionManager2> _managers = new();
    private readonly Dictionary<string, AppEntry> _apps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Icon> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SessionHandle> _byInstance = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _pathCache = new();
    private readonly List<SessionWatcher> _watchers = new();
    private Guid _eventContext = Guid.NewGuid();
    private bool _managersReady;

    /// <summary>
    /// Rizeni uz bylo predano zpet systemu. Od te chvile se na session nesaha -
    /// ani z casovacu, ani z pojistky pri ukonceni procesu. Kazde dalsi COM
    /// volani by uz jen zdrzovalo vypinani.
    /// </summary>
    private bool _handedOff;

    public MixerEngine(PreampBackend preamp) { _preamp = preamp; }

    /// <summary>Vystreli, kdyz se objevi nova session - UI se ma prekreslit.</summary>
    public event Action SessionsChanged;

    /// <summary>
    /// Callback z audio enginu pri vzniku nove session. Bezi na cizim COM
    /// vlakne. Hlasitost srovnavame hned tady, aby aplikace, ktera se prave
    /// spustila, nedostala plny preamp do usi driv, nez dobehne refresh.
    /// </summary>
    private sealed class SessionWatcher : IAudioSessionNotification
    {
        private readonly Action<IAudioSessionControl> _onCreated;
        public SessionWatcher(Action<IAudioSessionControl> onCreated) { _onCreated = onCreated; }

        public int OnSessionCreated(IAudioSessionControl newSession)
        {
            try { _onCreated(newSession); } catch { }
            return 0;
        }
    }

    private void OnSessionCreated(IAudioSessionControl session)
    {
        // Tady se zamerne nesmi delat vubec nic krome oznameni.
        //
        // Audio engine tenhle callback vola se svym vlastnim zamkem v ruce a
        // dokumentace zakazuje z nej volat zpet do session API nebo cokoli
        // blokujiciho. Drive se odsud cetlo PID a rovnou zapisovala hlasitost -
        // to je presne ta cesta, kde se nase vlakno zablokuje na zamku audio
        // enginu, zatimco engine ceka na nase vlakno. Vysledkem je zatuhla
        // aplikace, se kterou nejde nic delat.
        //
        // Hlasitost nove session srovna Apply() hned pri nasledujicim obnoveni
        // seznamu, tedy o par milisekund pozdeji.
        SessionsChanged?.Invoke();
    }

    public IEnumerable<AppEntry> Apps => _apps.Values;

    /// <summary>Pocty pro diagnostiku - odhali, jestli neco pribyva a neuklizi se.</summary>
    public string DiagnosticCounts
    {
        get
        {
            int sessions = 0;
            foreach (var a in _apps.Values) sessions += a.Sessions.Count;
            return $"app {_apps.Count} | sess {sessions} | idx {_byInstance.Count} " +
                   $"| path {_pathCache.Count} | ikon {_iconCache.Count}";
        }
    }

    public PreampBackend Preamp => _preamp;

    /// <summary>
    /// Rezerva, kterou si aplikace drzi sama, i kdyz zadny slider neni zvednuty.
    /// Diky ni funguji hodnoty az do teto meze okamzite a nezalezi na tom, co ma
    /// uzivatel prave v config.txt. Vychozich 3,5 dB odpovida 150 %.
    /// </summary>
    public double BaseHeadroomDb { get; set; } = 3.5;

    /// <summary>Kolikrat je potreba zvednout cele zarizeni, aby se nejhlasitejsi app vesla.</summary>
    public double Headroom { get; private set; } = 1.0;

    public double HeadroomDb => PreampBackend.GainToDb(Headroom);

    /// <summary>True pokud headroom neni z APO, ale musi ho dodat uzivatel na hardwaru.</summary>
    public bool HeadroomIsVirtual => !_preamp.IsAvailable;

    // ------------------------------------------------------------------
    // Enumerace
    // ------------------------------------------------------------------

    private void EnsureManagers()
    {
        if (_managersReady) return;

        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(ComGuids.MMDeviceEnumerator));

        if (enumerator.EnumAudioEndpoints(EDataFlow.Render, NativeMethods.DEVICE_STATE_ACTIVE,
                out var devices) != 0) return;

        devices.GetCount(out int count);
        for (int i = 0; i < count; i++)
        {
            if (devices.Item(i, out var device) != 0) continue;
            var iid = ComGuids.IAudioSessionManager2;
            if (device.Activate(ref iid, NativeMethods.CLSCTX_ALL, IntPtr.Zero, out object mgr) != 0) continue;
            _managers.Add((IAudioSessionManager2)mgr);
        }

        _managersReady = _managers.Count > 0;
    }

    /// <summary>Znovu nacte seznam session ze vsech aktivnich vystupu.</summary>
    public void Refresh()
    {
        if (_handedOff) return;
        EnsureManagers();

        foreach (var app in _apps.Values)
            foreach (var s in app.Sessions)
                s.Seen = false;

        foreach (var mgr in _managers)
        {
            if (mgr.GetSessionEnumerator(out var sessions) != 0) continue;
            if (sessions.GetCount(out int n) != 0) continue;

            for (int i = 0; i < n; i++)
            {
                if (sessions.GetSession(i, out var control) != 0) continue;

                bool kept = false;
                try
                {
                    if (control is IAudioSessionControl2 ctl2) kept = AddOrUpdate(ctl2);
                }
                catch (COMException) { /* session zmizela behem enumerace */ }

                // Co si nenechavame, musime uvolnit. Kazde vytazeni session
                // zvysi pocitadlo referenci; bez tohoto rostla nativni pamet
                // procesu dokola, protoze seznam obnovujeme porad.
                if (!kept) SafeRelease(control);
            }

            SafeRelease(sessions);
        }

        RegisterWatchers();

        // uklid zmizelych session a prazdnych aplikaci
        foreach (var app in _apps.Values.ToList())
        {
            app.Sessions.RemoveAll(s =>
            {
                if (s.Seen && !s.Broken && s.State != AudioSessionState.Expired) return false;
                _byInstance.Remove(s.InstanceId);
                return true;
            });
            if (app.Sessions.Count == 0)
            {
                app.Icon?.Dispose();
                _apps.Remove(app.Key);
            }
        }
    }

    /// <summary>
    /// Registrace musi probehnout az po prvnim vytvoreni session enumeratoru,
    /// jinak ji audio engine tise zahodi. Proto az na konci Refresh().
    /// </summary>
    private void RegisterWatchers()
    {
        if (_watchers.Count > 0 || _managers.Count == 0) return;

        foreach (var mgr in _managers)
        {
            var watcher = new SessionWatcher(OnSessionCreated);
            if (mgr.RegisterSessionNotification(watcher) == 0)
                _watchers.Add(watcher);   // drzime referenci, aby ji nesebral GC
        }
    }

    /// <summary>Uvolni COM referenci, kterou uz nedrzime. Nikdy nesmi shodit obnovu.</summary>
    private static void SafeRelease(object comObject)
    {
        try { if (comObject != null && Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject); }
        catch { }
    }

    /// <summary>
    /// Vraci true, pokud jsme si COM objekt session nechali. Pri false ho
    /// volajici uvolni.
    /// </summary>
    private bool AddOrUpdate(IAudioSessionControl2 ctl2)
    {
        if (ctl2.GetSessionInstanceIdentifier(out string instanceId) != 0 || instanceId == null) return false;

        // Uz zname? Pak jen odskrtneme a hotovo. Drive se pro kazdou session
        // pri kazdem obnoveni znovu volalo OpenProcess a hledalo se v seznamu.
        if (_byInstance.TryGetValue(instanceId, out var known))
        {
            known.Seen = true;
            return false;
        }

        bool isSystem = ctl2.IsSystemSoundsSession() == 0;
        ctl2.GetProcessId(out int pid);

        // vlastni session nechceme ridit
        if (!isSystem && pid == Environment.ProcessId) return false;

        string exePath = isSystem ? null : GetCachedProcessPath(pid);
        string key = isSystem ? "@system" : (exePath ?? "@pid:" + pid);

        if (!_apps.TryGetValue(key, out var app))
        {
            app = new AppEntry
            {
                Key = key,
                ExePath = exePath,
                IsSystemSounds = isSystem,
                DisplayName = isSystem ? S.T("System sounds", "Systémové zvuky") : FriendlyName(ctl2, exePath, pid),
                Icon = isSystem ? null : GetIcon(exePath),
            };
            _apps[key] = app;
        }

        var handle = new SessionHandle
        {
            Control = ctl2,
            InstanceId = instanceId,
            ProcessId = pid,
            IsSystemSounds = isSystem,
            Seen = true,
        };

        if (ctl2 is ISimpleAudioVolume vol) handle.Volume = vol;
        if (ctl2 is IAudioMeterInformation meter) handle.Meter = meter;

        app.Sessions.Add(handle);
        _byInstance[instanceId] = handle;
        return true;
    }

    private static string FriendlyName(IAudioSessionControl2 ctl, string exePath, int pid)
    {
        try
        {
            if (ctl.GetDisplayName(out string dn) == 0 && !string.IsNullOrWhiteSpace(dn) && !dn.StartsWith("@"))
                return dn;
        }
        catch { }

        if (exePath != null)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrWhiteSpace(info.FileDescription)) return Tidy(info.FileDescription);
            }
            catch { }
            return Tidy(Path.GetFileNameWithoutExtension(exePath));
        }

        try { return Process.GetProcessById(pid).ProcessName; }
        catch { return "PID " + pid; }
    }

    /// <summary>
    /// Uklidi nazvy, ktere vznikaji buildovacimi konvencemi enginu -
    /// treba "FortniteClient-Win64-Shipping" je proste Fortnite.
    /// </summary>
    private static string Tidy(string name)
    {
        foreach (var suffix in new[] { "-Win64-Shipping", "-Win32-Shipping", "-WinGDK-Shipping" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^suffix.Length];
        }
        if (name.EndsWith("Client", StringComparison.Ordinal) && name.Length > 6)
            name = name[..^6];
        return name;
    }

    /// <summary>
    /// Cesta k .exe podle PID. OpenProcess je systemove volani a driv se delalo
    /// pro kazdou session pri kazdem obnoveni seznamu.
    /// </summary>
    private string GetCachedProcessPath(int pid)
    {
        if (_pathCache.TryGetValue(pid, out string cached)) return cached;
        string path = NativeMethods.GetProcessPath(pid);
        if (_pathCache.Count > 256) _pathCache.Clear();   // PID se casem recykluji
        _pathCache[pid] = path;
        return path;
    }

    private Icon GetIcon(string exePath)
    {
        if (exePath == null) return null;
        if (_iconCache.TryGetValue(exePath, out var cached)) return cached;
        Icon icon = null;
        try { icon = Icon.ExtractAssociatedIcon(exePath); } catch { }
        _iconCache[exePath] = icon;
        return icon;
    }

    // ------------------------------------------------------------------
    // Prepocet a aplikace hlasitosti
    // ------------------------------------------------------------------

    public void SetGain(AppEntry app, double gain)
    {
        app.Gain = Math.Clamp(gain, 0.0, MaxGainPercent / 100.0);
        // hlidac zacina od plneho zesileni a ubere si sam, az kdyz je proc
        app.DuckGain = app.Gain;
        Apply();
    }

    public void SetMuted(AppEntry app, bool muted)
    {
        app.Muted = muted;
        Apply();
    }

    /// <summary>
    /// Nastavi silu hlidace u jedne aplikace (0 az 1). Pri nule se pripadny
    /// probihajici utlum hned pusti, at aplikace nezustane viset stazena.
    /// </summary>
    public void SetWatchdogStrength(AppEntry app, double strength)
    {
        app.WatchdogStrength = Math.Clamp(strength, 0.0, 1.0);
        if (!IsWatched(app))
        {
            app.DuckGain = app.Gain;
            app.IsDucking = false;
        }
        Apply();
    }

    /// <summary>
    /// Srdce cele veci: nejhlasitejsi aplikace urcuje headroom, ten se posle
    /// do APO jako preamp a vsechny aplikace se o nej podeli pres session volume.
    /// Vysledna hlasitost aplikace = preamp * (gain / preamp) = gain. Presne.
    /// </summary>
    public void Apply()
    {
        if (_handedOff) return;
        _preamp.RefreshExternalDb();

        double maxGain = 1.0;
        foreach (var app in _apps.Values)
        {
            if (app.Muted) continue;
            if (app.Gain > maxGain) maxGain = app.Gain;
        }

        // Kolik zesileni bude zarizeni mit. Uzivateluv vlastni preamp se
        // zapocita, protoze Equalizer APO preampy scita.
        // Drzime aspon zakladni rezervu, at je porad z ceho brat.
        double neededDb = Math.Max(PreampBackend.GainToDb(maxGain), BaseHeadroomDb);
        double achievedDb = _preamp.IsAvailable
            ? Math.Max(neededDb, _preamp.ExternalDb)
            : neededDb;

        double newHeadroom = Math.Max(1.0, PreampBackend.DbToGain(achievedDb));

        // POZOR na poradi. Preamp a session hlasitosti se nasobi, takze
        // mezistav, kdy uz je zvednuty preamp ale jeste nejsou stazene
        // hlasitosti, znamena rana do usi. Proto vzdy nejdriv ten krok,
        // ktery jde smerem dolu:
        //   headroom roste  -> nejdriv stahnout hlasitosti, pak zvednout preamp
        //   headroom klesa  -> nejdriv snizit preamp, pak povolit hlasitosti
        bool rising = newHeadroom > Headroom + 1e-6;

        if (rising)
        {
            Headroom = newHeadroom;
            ApplySessionVolumes();
            if (_preamp.IsAvailable) _preamp.EnsureHeadroomDb(neededDb);
        }
        else
        {
            if (_preamp.IsAvailable) _preamp.EnsureHeadroomDb(neededDb);
            Headroom = newHeadroom;
            ApplySessionVolumes();
        }
    }

    private void ApplySessionVolumes()
    {
        foreach (var app in _apps.Values)
        {
            float target = app.Muted ? 0f : (float)Math.Clamp(EffectiveGain(app) / Headroom, 0.0, 1.0);
            foreach (var s in app.Sessions)
            {
                if (s.Volume == null) continue;
                try
                {
                    // tady ctenim overujeme i to, jestli hlasitost nezmenil nekdo jiny
                    if (s.Volume.GetMasterVolume(out float cur) == 0 && Math.Abs(cur - target) < 0.0005f)
                    {
                        s.LastSet = target;
                        continue;
                    }
                    s.Volume.SetMasterVolume(target, ref _eventContext);
                    s.LastSet = target;
                }
                catch (COMException) { }
            }
        }
    }

    // ------------------------------------------------------------------
    // Hlidac hlasitosti
    // ------------------------------------------------------------------

    /// <summary>Zapina automaticke ubirani zesileni u hlasitych zvuku.</summary>
    public bool WatchdogEnabled { get; set; }

    /// <summary>
    /// Strop vysledne hlasitosti. 1.0 znamena "nikdy hlasiteji, nez kdyby
    /// aplikace hrala na 100 %" - presne to, aby vystrel nebo auto neprelezly,
    /// zatimco tiche zvuky si boost uzijou naplno. Niz = prisnejsi hlidac.
    /// </summary>
    public double Ceiling { get; set; } = 1.0;

    /// <summary>
    /// Nejnizsi hlasitost, na kterou smi hlidac stahnout. 1.0 znamena, ze
    /// nikdy nejde pod puvodni hlasitost - hlidac pak jen ubira boost a tim
    /// pusobi vyhradne na zesilene aplikace. Nizsi hodnota ho pousti i na
    /// aplikace bez zesileni, protoze u nich neni co ubirat nez samotnou
    /// hlasitost.
    /// </summary>
    public double Floor { get; set; } = 1.0;

    /// <summary>Hlidac zasahuje i do aplikaci, ktere nejsou zesilene.</summary>
    private bool WatchesEverything => Floor < 0.999;

    /// <summary>Za jak dlouho utlum plne nastoupi (ms).</summary>
    public int AttackMs { get; set; } = 100;

    /// <summary>Za jak dlouho se zesileni vrati po odezneni hlasiteho zvuku (ms).</summary>
    public int ReleaseMs { get; set; } = 1000;

    /// <summary>
    /// Tvar prechodu, 0 az 1. Casy rikaji jak dlouho, tohle rika jak.
    /// 0 = rovnomerna rampa: konstantni rychlost, dojede presne v zadanem case,
    ///     ale zacatek i konec jsou ostre.
    /// 1 = exponencialni: rychly rozjezd a mekky dojezd, prirozenejsi, zato
    ///     se k cili jen asymptoticky bliží.
    /// </summary>
    public double Smoothness { get; set; } = 1.0;

    /// <summary>
    /// Nejrychlejsi perioda vzorkovani. Niz uz to nema smysl - WinForms casovac
    /// stoji na WM_TIMER s rozlisenim kolem 15,6 ms.
    /// </summary>
    public const int TickMs = 16;

    /// <summary>Klidova perioda, kdyz neni co hlidat.</summary>
    public const int IdleTickMs = 40;

    /// <summary>True, kdyz nekterou aplikaci opravdu hlidame.</summary>
    public bool NeedsFastTick
    {
        get
        {
            if (!WatchdogEnabled) return false;
            foreach (var app in _apps.Values)
            {
                if (app.Muted || app.WatchdogStrength <= 0.001) continue;
                if (WatchesEverything || app.Gain > 1.0) return true;
            }
            return false;
        }
    }

    // Skutecne uplynuly cas mezi vzorky. Casovac ve Windows nechodi presne a
    // tempo navic menime podle toho, jestli je co hlidat - pocitat s pevnou
    // periodou by znamenalo, ze zadane casy nesedi.
    private readonly System.Diagnostics.Stopwatch _tickClock = System.Diagnostics.Stopwatch.StartNew();
    private double _lastTickMs;

    // Prevod "za jak dlouho" na exponencialni koeficient. Delime tremi,
    // protoze po trech casovych konstantach je zmena z 95 % hotova.
    private static double CoefFromMs(int ms, double dt)
        => Math.Min(1.0, 1.0 - Math.Exp(-dt / Math.Max(0.5, ms / 3.0)));

    /// <summary>
    /// O kolik se posunout k cili za jeden vzorek. Michame rovnomernou rampu
    /// s exponencialou podle nastavene plynulosti.
    /// </summary>
    private double StepToward(double current, double target, int ms, double range, double dt)
    {
        double distance = target - current;
        if (Math.Abs(distance) < 1e-6) return 0;

        double exponential = distance * CoefFromMs(ms, dt);

        // rampa ujede za zadany cas cely pouzitelny rozsah, nikdy neprestreli
        double perTick = Math.Max(range, 0.05) * dt / Math.Max(1.0, ms);
        double linear = Math.Sign(distance) * Math.Min(perTick, Math.Abs(distance));

        double s = Math.Clamp(Smoothness, 0.0, 1.0);
        return linear * (1.0 - s) + exponential * s;
    }

    /// <summary>Hlida se u teto aplikace vubec neco?</summary>
    private bool IsWatched(AppEntry app)
        => WatchdogEnabled && app.WatchdogStrength > 0.001 && (app.Gain > 1.0 || WatchesEverything);

    /// <summary>Zesileni, ktere ma aplikace prave dostat (s ohledem na hlidac).</summary>
    private double EffectiveGain(AppEntry app)
        => IsWatched(app) ? app.DuckGain : app.Gain;

    /// <summary>
    /// Je videt seznam aplikaci? Kdyz ne (okno je v oznamovaci oblasti), staci
    /// vzorkovat jen to, co potrebuje hlidac. Zbytek je jen pro kresleni a
    /// kazdy dotaz do audio enginu neco stoji - pri sesti aplikacich to bylo
    /// pres tisic COM volani za sekundu do prazdna.
    /// </summary>
    public bool MetersVisible { get; set; } = true;

    // Hlasitost ze smesovace Windows se meni rukou uzivatele, ne po vzorcich.
    // Cist ji sedesatkrat za sekundu nema smysl - staci nekolikrat za sekundu.
    private double _volumeReadAge;
    private const double VolumeReadPeriodMs = 250;

    /// <summary>Aktualizuje metry a hlidac. Volat casto (~30x za sekundu).</summary>
    public void UpdateMeters()
    {
        if (_handedOff) return;

        double now = _tickClock.Elapsed.TotalMilliseconds;
        double dt = Math.Clamp(now - _lastTickMs, 1.0, 200.0);
        _lastTickMs = now;

        _volumeReadAge += dt;
        bool readVolumes = MetersVisible && _volumeReadAge >= VolumeReadPeriodMs;
        if (readVolumes) _volumeReadAge = 0;

        foreach (var app in _apps.Values)
        {
            bool watched = IsWatched(app);

            float peak = 0f;
            if (watched || MetersVisible)
            {
                for (int i = 0; i < app.Sessions.Count; i++)
                {
                    var s = app.Sessions[i];
                    if (s.Meter == null) continue;
                    try { if (s.Meter.GetPeakValue(out float p) == 0 && p > peak) peak = p; }
                    catch (COMException) { s.Broken = true; }
                }
            }
            app.RawPeak = peak;

            // bez LINQ - tohle bezi desitky krat za sekundu
            if (readVolumes)
            {
                for (int i = 0; i < app.Sessions.Count; i++)
                {
                    var s = app.Sessions[i];
                    if (s.Volume == null) continue;
                    try { if (s.Volume.GetMasterVolume(out float v) == 0) app.WindowsVolume = v; }
                    catch (COMException) { s.Broken = true; }
                    break;
                }
            }

            if (watched)
            {
                // Nikdy nezesilujeme nad to, co si uzivatel nastavil, a nikdy
                // netlumime pod podlahu. Kdyz je slider niz nez podlaha,
                // rozhoduje slider - hlidac hlasitost nikdy nezvysuje.
                double lo = Math.Min(Floor, app.Gain);

                // Kolik zesileni jeste projde, aniz by vysledek prelezl strop.
                // Metr je PRE-volume, takze peak je primo surova hlasitost zvuku
                // a vysledek je peak * zesileni - zadny headroom se do toho neplete.
                double allowed = peak > 0.0005f
                    ? Math.Clamp(Ceiling / peak, lo, app.Gain)
                    : app.Gain;

                // Sila hlidace u teto aplikace. Zasah se meri od zesileni, ktere
                // si uzivatel nastavil - pri 25 % se ubere jen ctvrtina toho, co
                // by hlidac ubral naplno, a stejne se zvedne i podlaha, aby se
                // utlum nedostal hloubeji, nez sila povoluje.
                double strength = Math.Clamp(app.WatchdogStrength, 0.0, 1.0);
                if (strength < 1.0)
                {
                    allowed = app.Gain + (allowed - app.Gain) * strength;
                    lo = app.Gain + (lo - app.Gain) * strength;
                }

                int ms = allowed < app.DuckGain ? AttackMs : ReleaseMs;
                app.DuckGain += StepToward(app.DuckGain, allowed, ms, app.Gain - lo, dt);
                app.DuckGain = Math.Clamp(app.DuckGain, lo, app.Gain);
                app.IsDucking = app.DuckGain < app.Gain - 0.02;
            }
            else
            {
                app.DuckGain = app.Gain;
                app.IsDucking = false;
            }

            // metr ukazuje, co je opravdu slyset = surovy zvuk krat platne zesileni
            app.Peak = (float)Math.Min(1.0, peak * EffectiveGain(app));
        }

        if (WatchdogEnabled) PushDuckedVolumes();
    }

    /// <summary>
    /// Zapise hlasitosti podle aktualniho utlumu. Zamerne nesaha na preamp -
    /// ten se zapisuje do souboru a prepisovat ho 30x za sekundu by rozsekalo
    /// zvuk i disk. Utlum se resi vyhradne hlasitosti session.
    /// </summary>
    private void PushDuckedVolumes()
    {
        foreach (var app in _apps.Values)
        {
            if (app.Muted || !IsWatched(app)) continue;

            float target = (float)Math.Clamp(EffectiveGain(app) / Headroom, 0.0, 1.0);
            foreach (var s in app.Sessions)
            {
                if (s.Volume == null) continue;
                if (Math.Abs(s.LastSet - target) < 0.002f) continue;
                try
                {
                    s.Volume.SetMasterVolume(target, ref _eventContext);
                    s.LastSet = target;
                }
                catch (COMException) { }
            }
        }
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// Predani rizeni pri ukonceni. Cilem je, aby zvuk znel dal presne stejne
    /// i bez bezici aplikace.
    ///
    /// Nas vlastni preamp jde na nulu a hlasitosti session se prepocitaji proti
    /// preampu, ktery ma uzivatel v Equalizer APO sam. Vysledek kazde aplikace
    /// tim zustava nezmeneny - jen uz ho nedrzime my.
    ///
    /// Nas preamp zamerne nenechavame nahore: nemel by ho kdo kompenzovat a
    /// prvni nove spustena aplikace by dostala plny boost do usi. Proto se
    /// udrzi jen zesileni, ktere se vejde do uzivatelova vlastniho preampu;
    /// cokoli nad to se srovna dolu.
    /// </summary>
    public void HandOff()
    {
        if (_handedOff) return;
        _handedOff = true;

        // Zakladni rezervu drzime jen dokud bezime; po ukonceni ji nema kdo
        // kompenzovat, takze zustava jen to, co ma uzivatel sam v config.txt.
        _preamp.RefreshExternalDb();
        double external = Math.Max(1.0, PreampBackend.DbToGain(_preamp.ExternalDb));

        // nejdriv dolu s preampem, teprve pak nahoru s hlasitostmi
        _preamp.Reset();
        Headroom = external;

        foreach (var app in _apps.Values)
        {
            float target = app.Muted ? 0f : (float)Math.Clamp(app.Gain / external, 0.0, 1.0);
            foreach (var s in app.Sessions)
            {
                if (s.Volume == null) continue;
                try { s.Volume.SetMasterVolume(target, ref _eventContext); }
                catch { }
            }
        }
    }

    /// <summary>
    /// Ukonceni pri vypinani nebo odhlaseni Windows. Zamerne se nesaha na
    /// zadnou session:
    ///
    /// Pri vypinani se audio engine boura soubezne s nami a kazde COM volani do
    /// nej muze cekat vteriny. Windows mezitim ceka, az nase okno odpovi na
    /// WM_ENDSESSION - a kdyz neodpovi, drzi celou obrazovku "aplikace brani
    /// vypnuti". Hlasitosti session stejne zanikaji s procesy, ktere je vlastni,
    /// takze jedine, co musi zustat srovnane, je nas preamp - a to je zapis do
    /// souboru, ktery trva zlomek milisekundy.
    /// </summary>
    public void ShutdownFast()
    {
        _handedOff = true;
        try { _preamp.Reset(); } catch { }
    }

    /// <summary>
    /// Uplny navrat pred zasah aplikace - vsechny session na 100 % a nas
    /// preamp pryc. Pro tlacitko "Odebrat z Equalizer APO".
    /// </summary>
    public void RevertToDefaults()
    {
        _preamp.Reset();
        Headroom = 1.0;

        foreach (var app in _apps.Values)
        {
            foreach (var s in app.Sessions)
            {
                if (s.Volume == null) continue;
                try { s.Volume.SetMasterVolume(1f, ref _eventContext); }
                catch { }
            }
        }
    }

    public void Dispose()
    {
        // Po rychlem ukonceni uz je audio engine mimo hru - odregistrovavat se
        // z nej by znamenalo cekat na volani, ktere uz nema kdo obslouzit.
        if (_handedOff)
        {
            _watchers.Clear();
            _apps.Clear();
            _managers.Clear();
            foreach (var icon in _iconCache.Values) icon?.Dispose();
            _iconCache.Clear();
            return;
        }

        for (int i = 0; i < _watchers.Count && i < _managers.Count; i++)
        {
            try { _managers[i].UnregisterSessionNotification(_watchers[i]); } catch { }
        }
        _watchers.Clear();

        HandOff();
        foreach (var icon in _iconCache.Values) icon?.Dispose();
        _iconCache.Clear();
        _apps.Clear();
        _managers.Clear();
    }
}
