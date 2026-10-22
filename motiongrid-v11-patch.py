from pathlib import Path
root = Path('MotionGrid')
j = root/'app/src/main/java/cz/motiongrid/app/MainActivity.java'
s=j.read_text()
def change(a,b):
 global s
 assert a in s, 'Java pattern missing: '+a[:60]
 s=s.replace(a,b)
change('    private boolean recording = false;', '''    private boolean recording = false;
    private final VelocityIntegrator velocity = new VelocityIntegrator();
    private boolean includeZ = false, gpsEnabled = false;''')
change('        recordFile = new File(getFilesDir(), "motiongrid-last.csv");', '''        includeZ = getPreferences(MODE_PRIVATE).getBoolean("include_z", false);
        gpsEnabled = getPreferences(MODE_PRIVATE).getBoolean("gps_enabled", false);
        velocity.setIncludeZ(includeZ);
        recordFile = new File(getFilesDir(), "motiongrid-last.csv");''')
change('                if (orientationSensor == null) calculateFallbackRotation();',
       '''                if (orientationSensor == null) calculateFallbackRotation();
                if (linearSensor == null) updateVelocity(event.timestamp);''')
change('                System.arraycopy(event.values, 0, linear, 0, 3); hasLinear = true; break;',
       '''                System.arraycopy(event.values, 0, linear, 0, 3); hasLinear = true;
                updateVelocity(event.timestamp);
                break;''')
change('        anchorReady = true;\n    }','''        anchorReady = true;
        velocity.reset();
    }''')
change('    private void emitPacket(long timestampNs) {','''    private void updateVelocity(long timestampNs) {
        if (rotationReady && anchorReady) velocity.accept(anchorVector(sensorLinear()), timestampNs);
    }
    private void setIncludeZ(boolean enabled) {
        includeZ = enabled; velocity.setIncludeZ(enabled);
        getPreferences(MODE_PRIVATE).edit().putBoolean("include_z", enabled).apply(); sendStatus();
    }
    private void setGpsEnabled(boolean enabled) {
        gpsEnabled = enabled;
        getPreferences(MODE_PRIVATE).edit().putBoolean("gps_enabled", enabled).apply();
        if (enabled) requestLocation();
        else { locationManager.removeUpdates(this); lastLocation = null; }
        sendStatus();
    }
    private void emitPacket(long timestampNs) {''')
change('            p.put("rotationAvailable", rotationReady);', '''            p.put("rotationAvailable", rotationReady);
            p.put("estimatedVelocity", rotationReady ? arr(velocity.getVelocity()) : JSONObject.NULL);
            p.put("estimatedSpeedMps", rotationReady && velocity.hasSamples() ? velocity.getSpeedMps() : JSONObject.NULL);
            p.put("includeZ", includeZ);
            p.put("gpsEnabled", gpsEnabled);''')
change('            if (loc != null) {','            if (gpsEnabled && loc != null) {')
change('            p.put("gpsAllowed", hasLocationPermission());','''            p.put("gpsAllowed", hasLocationPermission());
            p.put("gpsEnabled", gpsEnabled);
            p.put("includeZ", includeZ);''')
change('        if (!hasLocationPermission()) return;','        if (!gpsEnabled || !hasLocationPermission()) return;')
change('            else toast("Bez polohy není dostupná GPS rychlost.");','''            else { gpsEnabled = false; getPreferences(MODE_PRIVATE).edit().putBoolean("gps_enabled", false).apply();
                toast("GPS je vypnutá. Výpočet z akcelerometru funguje bez ní."); }''')
change('            recordWriter.write("elapsed_s,ax_device,ay_device,az_device,ax_reference,ay_reference,az_reference,gx_rad_s,gy_rad_s,gz_rad_s,gps_m_s,r00,r01,r02,r10,r11,r12,r20,r21,r22\\n");',
       '            recordWriter.write("elapsed_s,ax_device,ay_device,az_device,ax_reference,ay_reference,az_reference,gx_rad_s,gy_rad_s,gz_rad_s,gps_m_s,r00,r01,r02,r10,r11,r12,r20,r21,r22,vx,vy,vz,estimated_speed_m_s,include_z\\n");')
change('            appendArray(line, p.optJSONArray("matrix"), 9);','''            appendArray(line, p.optJSONArray("matrix"), 9);
            appendArray(line, p.optJSONArray("estimatedVelocity"), 3);
            appendNumber(line, p.optDouble("estimatedSpeedMps", Double.NaN));
            appendNumber(line, includeZ ? 1 : 0);''')
change('        @JavascriptInterface public void requestGps() { runOnUiThread(() -> requestLocation()); }', '''        @JavascriptInterface public void setGpsEnabled(boolean enabled) { runOnUiThread(() -> MainActivity.this.setGpsEnabled(enabled)); }
        @JavascriptInterface public void setIncludeZ(boolean enabled) { runOnUiThread(() -> MainActivity.this.setIncludeZ(enabled)); }
        @JavascriptInterface public void resetSpeed() { runOnUiThread(() -> velocity.reset()); }''')
j.write_text(s)

velocity = """package cz.motiongrid.app;
public final class VelocityIntegrator {
    private final float[] v = new float[3], previous = new float[3];
    private boolean includeZ, hasPrevious;
    private long lastNs;
    private int samples;
    public void reset() {
        for (int i=0;i<3;i++) v[i]=previous[i]=0f;
        hasPrevious=false;lastNs=0L;samples=0;
    }
    public void setIncludeZ(boolean enabled) {
        if(includeZ!=enabled) {includeZ=enabled;v[2]=previous[2]=0f;}
    }
    public float[] getVelocity(){return v.clone();}
    public boolean hasSamples(){return samples>0;}
    public float getSpeedMps(){
        return (float)Math.sqrt(v[0]*v[0]+v[1]*v[1]+(includeZ?v[2]*v[2]:0));
    }
    public void accept(float[] referenceAcceleration,long timestampNs) {
        if(referenceAcceleration==null||referenceAcceleration.length!=3||timestampNs<=0)return;
        for(int i=0;i<3;i++)if(!Float.isFinite(referenceAcceleration[i]))return;
        if(lastNs>0&&timestampNs<=lastNs)return;
        float[] a=new float[3];
        for(int i=0;i<3;i++){
            float raw=(i==2&&!includeZ)?0:referenceAcceleration[i];
            a[i]=Math.abs(raw)<=0.12f?0:raw-Math.copySign(0.12f,raw);
        }
        if(hasPrevious){
            double dt=(timestampNs-lastNs)*1e-9;
            if(dt>=0.001&&dt<=0.2){
                for(int i=0;i<3;i++)v[i]+=(float)(0.5*(previous[i]+a[i])*dt);
                samples++;
            }
        }
        System.arraycopy(a,0,previous,0,3);lastNs=timestampNs;hasPrevious=true;
    }
}
"""
(root/'app/src/main/java/cz/motiongrid/app/VelocityIntegrator.java').write_text(velocity)

html=root/'app/src/main/assets/index.html'
h=html.read_text()
a=h.index('    <section class="panel speed-panel">')
b=h.index('    <section class="panel session-panel">',a)
h=h[:a]+'''    <section class="panel speed-panel">
      <div class="panel-heading"><div><div class="section-tag">04 / INERCIÁLNÍ RYCHLOST</div><h2>Rychlost z X/Y</h2></div><div class="status-chip ready" id="speedStatus">BEZ GPS</div></div>
      <div class="speed-display"><div class="speed-main"><span id="calcSpeed">—</span><span class="speed-unit">km/h</span></div><div class="speed-alt" id="calcMps">— m/s</div></div>
      <div class="speed-explainer">Rychlost se sčítá ze zrychlení X/Y po kompenzaci rotace. Výsledek je odhad, ne přesné měření.</div>
      <div class="velocity-grid"><div><span>Vx · ±X</span><strong id="velX">—</strong><small>m/s</small></div>
      <div><span>Vy · ±Y</span><strong id="velY">—</strong><small>m/s</small></div>
      <div id="velocityZRow" class="dimmed"><span>Vz · ±Z</span><strong id="velZ">—</strong><small>m/s</small></div></div>
      <div class="speed-controls"><button class="btn btn-record" id="resetSpeedBtn">◎ Vynulovat rychlost</button>
      <label class="switch-line">Počítat Z / −Z <input id="includeZ" type="checkbox" role="switch"></label></div>
      <p class="notice"><span>i</span> Bez GPS je počáteční rychlost předpokládaná nulová. Chyba zrychlení a náklonu se časem sčítá. Rychlost vynuluj, když telefon skutečně stojí.</p>
    </section>
    <section class="panel speed-panel optional-gps">
      <div class="panel-heading"><div><div class="section-tag">05 / VOLITELNĚ</div><h2>GPS – porovnání</h2></div><div class="gps-indicator" id="gpsIndicator">VYPNUTO</div></div>
      <div class="gps-line"><div><strong id="gpsSpeed">—</strong> <span>km/h</span><div class="small-note" id="gpsDetail">GPS je vypnutá.</div></div><button class="btn btn-accent" id="gpsBtn">Zapnout GPS</button></div>
    </section>
''' + h[b:]
h=h.replace('V1.0','V1.1').replace('05 / DATA','06 / DATA')
h=h.replace('Skutečná data telefonu. Zarovnané osy, kompenzace gravitace a sledování rotace v reálném čase.',
            'Akcelerometr počítá rychlost z X/Y, gyroskop kompenzuje rotace a GPS je jen volitelná.')
html.write_text(h)

js=root/'app/src/main/assets/app.js'
s=js.read_text()
s=s.replace('  webPermissionPending:false','  webPermissionPending:false, includeZ:false, webVelocity:[0,0,0], webPrevious:null, webPreviousTime:null, webGpsWatchId:null')
s=s.replace("    $('gpsBtn').textContent = s.gpsAllowed ? 'GPS povoleno ✓' : 'Zapnout GPS ↗';",
'''    state.includeZ=!!s.includeZ;
    $('includeZ').checked=state.includeZ;
    $('velocityZRow').classList.toggle('dimmed',!state.includeZ);
    $('gpsBtn').textContent=s.gpsEnabled?'Vypnout GPS':'Zapnout GPS';''')
s=s.replace('  if (!native && state.recording) {','''  if (!native && finiteVec(p.linearReference,3)) {
    const t=p.t;
    const dt=state.webPreviousTime==null?null:t-state.webPreviousTime;
    const f=p.linearReference.map((x,i)=>(i===2&&!state.includeZ)?0:(Math.abs(x)<=0.12?0:x-Math.sign(x)*0.12));
    if(dt!=null&&dt>=0.001&&dt<=0.2&&state.webPrevious)
      for(let i=0;i<3;i++)state.webVelocity[i]+=0.5*(f[i]+state.webPrevious[i])*dt;
    state.webPrevious=f;state.webPreviousTime=t;
    p.estimatedVelocity=state.webVelocity.slice();p.estimatedSpeedMps=Math.hypot(...state.webVelocity);
  }
  if (!native && state.recording) {''')
s=s.replace('    state.recordRows.push([p.t,...acc,...g,p.gpsSpeedMps??null,...r]);',
'''    state.recordRows.push([p.t,...acc,...g,p.gpsSpeedMps??null,...r,...(p.estimatedVelocity||[null,null,null]),p.estimatedSpeedMps??null,state.includeZ?1:0]);''')
a=s.index('  const speed=p.gpsSpeedMps;')
b=s.index("  $('hzIndicator').textContent",a)
s=s[:a]+'''  const est=p.estimatedSpeedMps;
  $('calcSpeed').textContent=Number.isFinite(est)?num(est*3.6,1):'—';
  $('calcMps').textContent=Number.isFinite(est)?num(est,2)+' m/s':'— m/s';
  const vv=finiteVec(p.estimatedVelocity,3)?p.estimatedVelocity:null;
  ['X','Y','Z'].forEach((axis,i)=>{$('vel'+axis).textContent=num(vv?.[i]);});
  $('speedStatus').textContent=Number.isFinite(est)?'INERCIÁLNÍ ODHAD':'ČEKÁ NA SENZORY';
  const speed=p.gpsSpeedMps;
  const hasSpeed=Number.isFinite(speed)&&speed>=0;
  $('gpsSpeed').textContent=hasSpeed?num(speed*3.6,1):'—';
  $('gpsIndicator').textContent=!p.gpsEnabled?'VYPNUTO':hasSpeed?'GPS FIX · ŽIVĚ':'HLEDÁM GPS';
  $('gpsIndicator').classList.toggle('fix',hasSpeed&&p.gpsEnabled);
  $('gpsDetail').textContent=!p.gpsEnabled?'Nepoužívá se pro výpočet.':hasSpeed?'Přesnost polohy: '+num(p.gpsAccuracyM,0)+' m':'Čeká se na GPS.';
  $('gpsBtn').textContent=p.gpsEnabled?'Vypnout GPS':'Zapnout GPS';
''' +s[b:]
s=s.replace("    window.MotionGridAndroid.align();showToast('Osy zarovnány k aktuální poloze telefonu.');",
            "    window.MotionGridAndroid.align();showToast('Osy zarovnány, rychlost vynulována.');")
s=s.replace("  if(native){window.MotionGridAndroid.align();showToast('Osy zarovnány k aktuální poloze telefonu.');}",
"  if(native){window.MotionGridAndroid.align();showToast('Osy zarovnány, rychlost vynulována.');}")
s=s.replace("  else if(state.webReady){state.webZero=state.webR.slice();showToast('Referenční soustava zarovnána.');}",
'''  else if(state.webReady){state.webZero=state.webR.slice();state.webVelocity=[0,0,0];state.webPrevious=null;state.webPreviousTime=null;showToast('Osy a rychlost vynulovány.');}''')
s=s.replace("  const head='sensor_t_s,ax_reference,ay_reference,az_reference,gx_rad_s,gy_rad_s,gz_rad_s,gps_m_s,r00,r01,r02,r10,r11,r12,r20,r21,r22';",
"  const head='sensor_t_s,ax_reference,ay_reference,az_reference,gx_rad_s,gy_rad_s,gz_rad_s,gps_m_s,r00,r01,r02,r10,r11,r12,r20,r21,r22,vx,vy,vz,estimated_speed_m_s,include_z';")
a=s.index("$('gpsBtn').addEventListener('click',()=>{")
b=s.index("$('recBtn').addEventListener('click',toggleRecording);",a)
s=s[:a]+'''$('resetSpeedBtn').addEventListener('click',()=>{
  if(native)window.MotionGridAndroid.resetSpeed();
  else {state.webVelocity=[0,0,0];state.webPrevious=null;state.webPreviousTime=null;}
  showToast('Rychlost vynulována. Předpoklad: telefon stojí.');
});
$('includeZ').addEventListener('change',(e)=>{
  state.includeZ=!!e.target.checked;
  $('velocityZRow').classList.toggle('dimmed',!state.includeZ);
  if(native)window.MotionGridAndroid.setIncludeZ(state.includeZ);
  else {state.webVelocity[2]=0;state.webPrevious=null;state.webPreviousTime=null;}
});
$('gpsBtn').addEventListener('click',()=>{
  const currentlyOn=state.packet?.gpsEnabled||state.status?.gpsEnabled||state.webGpsAllowed;
  const enable=!currentlyOn;
  if(native){window.MotionGridAndroid.setGpsEnabled(enable);return;}
  if(!enable){if(state.webGpsWatchId!=null)navigator.geolocation.clearWatch(state.webGpsWatchId);state.webGpsWatchId=null;state.webGps=null;state.webGpsAllowed=false;}
  else if(navigator.geolocation){state.webGpsAllowed=true;state.webGpsWatchId=navigator.geolocation.watchPosition(p=>{state.webGps=p.coords.speed;state.webGpsAccuracy=p.coords.accuracy;},e=>showToast('GPS: '+e.message),{enableHighAccuracy:true,maximumAge:1000,timeout:15000});}
});
''' +s[b:]
s=s.replace("rotationAvailable:!!rotated,orientationSource:'WEB_ORIENTATION',gpsAllowed:state.webGpsAllowed};",
"rotationAvailable:!!rotated,orientationSource:'WEB_ORIENTATION',gpsAllowed:state.webGpsAllowed,gpsEnabled:state.webGpsAllowed};")
js.write_text(s)

css=root/'app/src/main/assets/styles.css'
with css.open('a') as f:
 f.write('''
.speed-explainer{color:#91a9b7;padding:2px 24px 20px;font-size:11px;line-height:1.6}
.velocity-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;border-top:1px solid #263447;padding:15px 22px}
.velocity-grid>div{min-width:0;border-radius:9px;background:#17283a;padding:12px 8px}
.velocity-grid span{display:block;font-size:10px;color:#91b3c4}
.velocity-grid strong{display:inline-block;margin-top:10px;font-size:21px;color:#ebf6ff;font-variant-numeric:tabular-nums}
.velocity-grid small{font-size:9px;color:#8fa3b1;margin-left:4px}
.velocity-grid .dimmed{opacity:.4}
.speed-controls{display:flex;justify-content:space-between;align-items:center;gap:12px;padding:14px 24px;border-top:1px solid #273747}
.switch-line{display:flex;align-items:center;gap:9px;color:#d5edef;font-size:11px}
.switch-line input{accent-color:#73daba;width:21px;height:21px}
.optional-gps .gps-line{border-top:0}
.optional-gps .gps-line strong{font-size:28px;font-variant-numeric:tabular-nums}
.optional-gps .small-note{line-height:1.5;max-width:180px}
@media(max-width:530px){.speed-explainer{padding:2px 18px 16px}.velocity-grid{padding:12px;gap:6px}.velocity-grid>div{padding:10px 5px}.velocity-grid strong{font-size:17px}.speed-controls{padding:14px 18px;gap:7px}.speed-controls .btn{font-size:10px;padding:10px 9px}}
''')
assert 'VelocityIntegrator' in j.read_text()
assert 'calcSpeed' in html.read_text()
assert 'setIncludeZ' in js.read_text()
print('Patched MotionGrid v1.1')
