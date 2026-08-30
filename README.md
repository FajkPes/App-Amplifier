# App Amplifier

App Amplifier makes one quiet Windows application louder while keeping other
applications under control. It combines per-application volume from the Windows
audio mixer with Equalizer APO headroom, allowing a selected app to go above
the normal 100% session limit without adding another audio-buffering layer.

I created it for programs, games, calls, and media that are still too quiet at
100%. Instead of permanently raising everything, the app lowers other sessions,
adds matching device headroom through Equalizer APO, and gives the selected app
room to be boosted on its own slider.

## Main features

- Per-application gain controls up to 500%.
- Automatic headroom calculation through Equalizer APO.
- Keeps the relative loudness of other applications under control.
- Loud-sound watchdog with adjustable ceiling, floor, attack, release, and
  transition shape.
- Per-application watchdog strength.
- Live level meters and mute controls.
- Optional start with Windows and tray operation.
- English by default, with Czech available in the in-app Language setting.
- Restores session volumes and resets its own preamp contribution when exiting.

## Download and start

1. Download and extract the release ZIP.
2. Run **`START App Amplifier.bat`**.
3. The release is self-contained, so a separate .NET installation is not
   required.
4. If Equalizer APO is missing, the launcher explains the limitation and can
   open its official download page. You may still start in limited ratio mode.

## Equalizer APO setup

1. Install [Equalizer APO](https://sourceforge.net/projects/equalizerapo/).
2. In App Amplifier, open **Guide** and select **Devices…**.
3. In the Equalizer APO Configurator, enable only the playback devices you want
   the amplifier to affect.
4. Reconnect that device or restart Windows if the Configurator requests it.
5. Select **Set up** in App Amplifier. The app adds one `Include:` line to
   `config.txt` and manages its own `ZesilovacAplikaci.txt` preamp file.

The legacy internal filename is intentionally retained for compatibility with
existing installations. Its generated content is English. App Amplifier backs
up `config.txt` to `config.txt.zesilovac-backup` before adding the include and
does not edit your own EQ filters or preamp lines.

Use **Remove from Equalizer APO** to remove the include line and the managed
file. Your own Equalizer APO configuration remains in place.

## How the gain works

Windows exposes each application session from 0 to 100%. If one app needs 200%
gain, App Amplifier can add approximately +6 dB of device headroom and lower
the other sessions proportionally. The target session can then use that
headroom while other apps keep approximately their previous perceived level.

This does not recover detail that is absent from the source. Large boosts can
clip already loud content or become uncomfortable, so increase gain gradually
and keep the loud-sound watchdog enabled.

## Safety and recovery

- Start with low listening volume and increase one slider gradually.
- Keep **Volume protection** and **Loud sound watchdog** enabled.
- The app restores controlled session volumes during a normal exit and also
  registers an emergency process-exit restore path.
- App Amplifier writes only its managed include file and one include line; it
  does not replace the user's Equalizer APO configuration.
- If audio behaves unexpectedly, select **All to 100%**, exit the app, and use
  **Remove from Equalizer APO** if needed.

## Requirements

- Windows 10 or 11, x64
- An ordinary Windows playback device
- Equalizer APO for gain above 100% without an added buffering layer
- Applications must use the normal Windows shared audio path. ASIO and WASAPI
  exclusive mode bypass the system-effects path used by Equalizer APO.

The release includes the .NET 8 desktop runtime. Building from source requires
the .NET 8 SDK or newer.

## Build and verification

```powershell
dotnet restore AppAmplifier.csproj
dotnet build AppAmplifier.csproj -c Release --no-restore
dotnet run --project AppAmplifier.csproj -- --check
```

`--check` exits before the mixer and Equalizer APO backend are initialized, so
it can verify that the executable starts without changing system audio.

## Disclaimer

High gain can cause clipping and dangerously loud output. Equalizer APO is a
separate third-party project. App Amplifier is not affiliated with it or with
Microsoft. Use conservative levels and protect your hearing.

## Credits

Created by **FajkPes** with assistance from **ChatGPT Codex** and
**Claude Code**.
