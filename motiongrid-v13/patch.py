from pathlib import Path
import shutil,re
root=Path('MotionGrid')
j=root/'app/src/main/java/cz/motiongrid/app/MainActivity.java'
s=j.read_text()
def change(old,new):
 global s
 assert old in s, 'Java pattern not found: '+old[:90]
 s=s.replace(old,new)
change('private boolean includeZ = false, gpsEnabled = false, autoZero = true;',
       'private boolean includeZ = false, gpsEnabled = false, autoZero = false;')
change('getPreferences(MODE_PRIVATE).getBoolean("auto_zero", true)',
       'getPreferences(MODE_PRIVATE).getBoolean("auto_zero", false)')
change('velocity.accept(anchorVector(sensorLinear()), accel, gyro, timestampNs);',
       'velocity.accept(sensorLinear(), anchoredRotation(), accel, gyro, timestampNs);')
change('        @JavascriptInterface public void resetSpeed() { runOnUiThread(() -> velocity.reset()); }',
       '''        @JavascriptInterface public void resetSpeed() { runOnUiThread(() -> velocity.reset()); }
        @JavascriptInterface public void confirmStop() { runOnUiThread(() -> {
            if(velocity.isCalibrated()) { velocity.confirmStop(); sendStatus(); }
            else toast("Nejdříve dokonči kalibraci telefonu v klidu.");
        }); }''')
j.write_text(s)
shutil.copyfile('motiongrid-v13/VelocityIntegrator.java', root/'app/src/main/java/cz/motiongrid/app/VelocityIntegrator.java')
html=root/'app/src/main/assets/index.html'
h=html.read_text()
needle='<canvas id="space" aria-label="3D vizualizace os zařízení a referenční soustavy"></canvas>'
assert needle in h
h=h.replace(needle,needle+'''
        <div class="viz-speed" aria-label="Odhad rychlosti ze senzorů"><div class="viz-speed-label">VÝPOČET IMU · BEZ GPS</div><div class="viz-speed-number"><strong id="vizSpeed">—</strong><span>km/h</span></div><div class="viz-speed-phase" id="vizSpeedPhase">ČEKÁ NA KALIBRACI</div></div>''')
needle='<div class="autozero-row">'
assert needle in h
h=h.replace(needle,'''<div class="stop-confirm-row"><button class="btn btn-confirm-stop" id="confirmStopBtn" type="button">■ Potvrdit, že stojím (0 km/h)</button><span>Jen při skutečném zastavení</span></div>
      <div class="autozero-row">''')
h=h.replace('Automaticky nulovat při klidu IMU','Experimentální automatické zastavení po brzdění')
h=h.replace('Automatický klid senzorů nepozná pohyb stálou rychlostí – může jej mylně vynulovat.',
'''Při konstantní rychlosti mohou senzory vypadat stejně jako při stání. Automatické zastavení je proto ve výchozím stavu vypnuté.''')
h=h.replace('V1.2','V1.3').replace('v1.2','v1.3')
html.write_text(h)
js=root/'app/src/main/assets/app.js'
t=js.read_text()
needle="    $('autoZero').checked=s.autoZero!==false;"
assert needle in t
t=t.replace(needle,"    $('autoZero').checked=!!s.autoZero;")
start=t.index('  const phases={')
end=t.index('\n',start)
t=t[:start]+"""  const phases={CALIBRATION:'KALIBRACE',READY:'PŘIPRAVENO',ACCELERATING:'ZRYCHLUJE',BRAKING:'BRZDÍ',COASTING_UNCERTAIN:'BEZ ZMĚNY RYCHLOSTI',MOTION_AMBIGUOUS:'KLID IMU · NEJISTOTA',CONFIRMED_STOP:'POTVRZENÉ ZASTAVENÍ',AUTO_STOP_CANDIDATE:'AUTO STOP · ODHAD',GAP:'VÝPADEK VZORKŮ'};"""+t[end:]
a=t.index("  $('calibrationInfo').textContent=")
b=t.index("\n  const speed=p.gpsSpeedMps;",a)
t=t[:a]+"""  $('vizSpeed').textContent=Number.isFinite(est)?num(est*3.6,1):'—';
  $('vizSpeedPhase').textContent=phases[phase]||'ČEKÁ NA DATA';
  $('calibrationInfo').textContent=!native?'Webová ukázka nemá stejnou senzorovou kalibraci jako APK.':
    !p.velocityCalibrated?'Polož telefon na pevnou podložku, bez pohybu · kalibrace '+(p.calibrationPercent||0)+' %.':
    phase==='BRAKING'?'Brzdění se integruje jako záporné zrychlení ve směru aktuální rychlosti; výsledná rychlost může klesnout až k nule.':
    phase==='MOTION_AMBIGUOUS'?'Senzory nevidí změnu rychlosti: telefon může stát NEBO se pohybovat rovnoměrně. Odhad rychlosti zachovávám, automaticky ho nenuluji.':
    phase==='AUTO_STOP_CANDIDATE'?'Experimentální nulování je jen odhad a může být chybné.':
    phase==='CONFIRMED_STOP'?'Zastavení bylo ručně potvrzeno. Začíná nový odhad z nulové rychlosti.':
    'Odhad rychlosti vzniká integrací zrychlení v pevné soustavě. Bez externího měření se chyba postupně hromadí.';
""" + t[b:]
needle="$('autoZero').addEventListener('change',(e)=>{"
assert needle in t
t=t.replace(needle, """$('confirmStopBtn').addEventListener('click',()=>{
  if(native)window.MotionGridAndroid.confirmStop();
  else {state.webVelocity=[0,0,0];state.webPrevious=null;state.webPreviousTime=null;}
  showToast('Rychlost nastavena na 0. Potvrzuj jen když telefon skutečně stojí.');
});
$('autoZero').addEventListener('change',(e)=>{""")
js.write_text(t)
css=root/'app/src/main/assets/styles.css'
with css.open('a') as f:
 f.write("""
.viz-speed{position:absolute;left:15px;top:48px;z-index:3;pointer-events:none;min-width:142px;max-width:210px;padding:10px 14px 12px;border-radius:12px;background:rgba(12,26,37,.82);border:1px solid #3d6573;backdrop-filter:blur(8px);box-shadow:0 8px 30px #0004}
.viz-speed-label{font-size:8px;color:#a5d9db;letter-spacing:1px;font-weight:850}
.viz-speed-number{display:flex;align-items:baseline;gap:7px;margin:4px 0}
.viz-speed-number strong{font-size:clamp(31px,8vw,43px);font-variant-numeric:tabular-nums;color:#e8fff6;letter-spacing:-1px}
.viz-speed-number span{font-size:12px;color:#9ce6d0;font-weight:800}
.viz-speed-phase{font-size:9px;color:#b3c6d4;letter-spacing:.3px;max-width:185px}
.stop-confirm-row{display:flex;align-items:center;gap:12px;padding:5px 22px 15px}
.stop-confirm-row span{color:#9fb3c5;font-size:10px;line-height:1.4}
.btn-confirm-stop{font-size:11px;border:1px solid #486476;background:#233d4e;color:#d8f1ff;white-space:normal;text-align:center}
@media(max-width:530px){.viz-speed{top:47px;left:10px;padding:9px 11px}.stop-confirm-row{padding:4px 15px 12px}}
""")
assert "vizSpeedPhase" in t and "confirmStopBtn" in h
print('MotionGrid v1.3 patch applied successfully')
