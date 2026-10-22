from pathlib import Path
root=Path('MotionGrid')
j=root/'app/src/main/java/cz/motiongrid/app/MainActivity.java'
s=j.read_text()
def change(a,b):
 global s
 assert a in s, 'Missing Java fragment: '+a[:65]
 s=s.replace(a,b)
change('    private boolean includeZ = false, gpsEnabled = false;', '''    private boolean includeZ = false, gpsEnabled = false, autoZero = true;
    private long lastAccelerometerNs = 0, lastGyroNs = 0, lastOrientationNs = 0;''')
change('        velocity.setIncludeZ(includeZ);', '''        velocity.setIncludeZ(includeZ);
        autoZero = getPreferences(MODE_PRIVATE).getBoolean("auto_zero", true);
        velocity.setAutoZero(autoZero);''')
change('        startLocationIfAllowed();', '''        velocity.reset();
        startLocationIfAllowed();''')
change('System.arraycopy(event.values, 0, accel, 0, 3); hasAccel = true;',
       'System.arraycopy(event.values, 0, accel, 0, 3); hasAccel = true; lastAccelerometerNs = event.timestamp;')
change('System.arraycopy(event.values, 0, gyro, 0, 3); hasGyro = true;',
       'System.arraycopy(event.values, 0, gyro, 0, 3); hasGyro = true; lastGyroNs = event.timestamp;')
change('                    rotationReady = true;\n                    if (!anchorReady)', '''                    rotationReady = true;
                    lastOrientationNs = event.timestamp;
                    if (!anchorReady)''')
change('            rotationReady = true;\n            if (!anchorReady)', '''            rotationReady = true;
            lastOrientationNs = lastAccelerometerNs;
            if (!anchorReady)''')
change('if (rotationReady && anchorReady) velocity.accept(anchorVector(sensorLinear()), timestampNs);',
       '''if (!rotationReady || !anchorReady || !hasAccel || !hasGyro) return;
        if (Math.abs(timestampNs - lastOrientationNs) > 180_000_000L
                || Math.abs(timestampNs - lastAccelerometerNs) > 180_000_000L
                || Math.abs(timestampNs - lastGyroNs) > 180_000_000L) return;
        velocity.accept(anchorVector(sensorLinear()), accel, gyro, timestampNs);''')
change('    private void setIncludeZ(boolean enabled) {', '''    private void setAutoZero(boolean enabled) {
        autoZero = enabled; velocity.setAutoZero(enabled);
        getPreferences(MODE_PRIVATE).edit().putBoolean("auto_zero", enabled).apply();
        sendStatus();
    }
    private void setIncludeZ(boolean enabled) {''')
change('p.put("estimatedSpeedMps", rotationReady && velocity.hasSamples() ? velocity.getSpeedMps() : JSONObject.NULL);',
       '''p.put("estimatedSpeedMps", rotationReady && velocity.isCalibrated() ? velocity.getSpeedMps() : JSONObject.NULL);
            p.put("velocityState", velocity.getStatus());
            p.put("velocityCalibrated", velocity.isCalibrated());
            p.put("calibrationPercent", velocity.calibrationPercent(timestampNs));
            p.put("velocityBias", arr(velocity.getBias()));
            p.put("autoZero", autoZero);''')
change('            p.put("recording", recording);', '''            p.put("autoZero", autoZero);
            p.put("velocityCalibrated", velocity.isCalibrated());
            p.put("recording", recording);''')
change('        @JavascriptInterface public void resetSpeed() { runOnUiThread(() -> velocity.reset()); }',
       '''        @JavascriptInterface public void resetSpeed() { runOnUiThread(() -> velocity.reset()); }
        @JavascriptInterface public void setAutoZero(boolean enabled) { runOnUiThread(() -> MainActivity.this.setAutoZero(enabled)); }''')
j.write_text(s)
(root/'app/src/main/java/cz/motiongrid/app/VelocityIntegrator.java').write_text("package cz.motiongrid.app;\npublic final class VelocityIntegrator {\n    private static final double G = 9.80665;\n    private static final long CALIBRATION_NS = 1_500_000_000L;\n    private static final long AUTO_ZERO_NS = 950_000_000L;\n    private final float[] velocity = new float[3];\n    private final float[] bias = new float[3];\n    private final float[] previousAcceleration = new float[3];\n    private final double[] calibrationSum = new double[3];\n    private long lastNs, stationarySinceNs;\n    private int calibrationCount, integratedSamples;\n    private boolean hasPrevious, calibrated, includeZ, autoZero = true, still;\n    private String status = \"CALIBRATION\";\n    public void reset() {\n        for(int i=0;i<3;i++){velocity[i]=bias[i]=previousAcceleration[i]=0;calibrationSum[i]=0;}\n        lastNs=stationarySinceNs=0;\n        calibrationCount=integratedSamples=0;\n        calibrated=hasPrevious=still=false;\n        status=\"CALIBRATION\";\n    }\n    public void setIncludeZ(boolean enabled) {\n        if(includeZ!=enabled){includeZ=enabled;velocity[2]=0;previousAcceleration[2]=0;}\n    }\n    public void setAutoZero(boolean enabled){autoZero=enabled;}\n    public boolean isAutoZero(){return autoZero;}\n    public float[] getVelocity(){return velocity.clone();}\n    public float[] getBias(){return bias.clone();}\n    public boolean hasSamples(){return calibrated&&integratedSamples>0;}\n    public boolean isCalibrated(){return calibrated;}\n    public boolean isStill(){return still;}\n    public String getStatus(){return status;}\n    public float getSpeedMps(){return (float)Math.sqrt(velocity[0]*velocity[0]+velocity[1]*velocity[1]+(includeZ?velocity[2]*velocity[2]:0));}\n    public int calibrationPercent(long nowNs){\n        if(calibrated)return 100;\n        if(stationarySinceNs==0||nowNs<=stationarySinceNs)return 0;\n        return Math.min(99,(int)((nowNs-stationarySinceNs)*100/CALIBRATION_NS));\n    }\n    public void accept(float[] referenceLinear,float[] rawAccelerometer,float[] rawGyro,long timeNs){\n        if(timeNs<=0||referenceLinear==null||rawAccelerometer==null||rawGyro==null||\n            referenceLinear.length!=3||rawAccelerometer.length!=3||rawGyro.length!=3)return;\n        for(int i=0;i<3;i++)if(!Float.isFinite(referenceLinear[i])||!Float.isFinite(rawAccelerometer[i])||!Float.isFinite(rawGyro[i]))return;\n        if(lastNs>0&&timeNs<=lastNs)return;\n        double accNorm=norm(rawAccelerometer),gyroNorm=norm(rawGyro),linearNorm=norm(referenceLinear);\n        // An inertially quiet phone might still move at a constant speed.\n        boolean quiet=Math.abs(accNorm-G)<0.22&&gyroNorm<0.045&&linearNorm<0.23;\n        if(quiet){if(stationarySinceNs==0)stationarySinceNs=timeNs;}\n        else stationarySinceNs=0;\n        still=quiet&&timeNs-stationarySinceNs>=AUTO_ZERO_NS;\n        if(!calibrated){\n            if(!quiet){calibrationCount=0;for(int i=0;i<3;i++)calibrationSum[i]=0;}\n            else{\n                calibrationCount++;\n                for(int i=0;i<3;i++)calibrationSum[i]+=referenceLinear[i];\n                if(timeNs-stationarySinceNs>=CALIBRATION_NS&&calibrationCount>=30){\n                    for(int i=0;i<3;i++)bias[i]=(float)(calibrationSum[i]/calibrationCount);\n                    calibrated=true;for(int i=0;i<3;i++)velocity[i]=0;\n                    hasPrevious=false;status=\"TRACKING\";\n                }\n            }\n            lastNs=timeNs;\n            if(!calibrated)status=\"CALIBRATION\";\n            return;\n        }\n        final double dt=lastNs==0?0:(timeNs-lastNs)*1e-9;\n        if(dt>0.15){hasPrevious=false;status=\"GAP\";lastNs=timeNs;return;}\n        if(still){\n            for(int i=0;i<3;i++)bias[i]=0.99f*bias[i]+0.01f*referenceLinear[i];\n            if(autoZero){for(int i=0;i<3;i++)velocity[i]=0;hasPrevious=false;}\n            status=autoZero?\"AUTO_ZERO\":\"IMU_QUIET\";\n        }else status=\"TRACKING\";\n        float[] corrected=new float[3];\n        for(int i=0;i<3;i++){\n            if(i==2&&!includeZ){corrected[i]=0;continue;}\n            float v=referenceLinear[i]-bias[i];\n            corrected[i]=Math.abs(v)>0.035f?(v-Math.copySign(0.035f,v)):0;\n        }\n        if(dt>=0.001&&dt<=0.15){\n            if(hasPrevious){for(int i=0;i<3;i++)velocity[i]+=(float)(0.5*(previousAcceleration[i]+corrected[i])*dt);}\n            else if(!still){for(int i=0;i<3;i++)velocity[i]+=(float)(corrected[i]*dt);}\n            integratedSamples++;\n        }\n        System.arraycopy(corrected,0,previousAcceleration,0,3);\n        hasPrevious=!still||!autoZero;lastNs=timeNs;\n    }\n    private static double norm(float[] v){return Math.sqrt(v[0]*v[0]+v[1]*v[1]+v[2]*v[2]);}\n}\n")
html=root/'app/src/main/assets/index.html'
s=html.read_text()
assert 'id="speedStatus"' in s
s=s.replace('Rychlost se sčítá ze zrychlení X/Y po kompenzaci rotace. Výsledek je odhad, ne přesné měření.',
'''Po kalibraci v klidu se rychlost integruje v zarovnané soustavě. Údaj je inerciální odhad, ne objektivně změřená rychlost.
        <div class="calibration-info" id="calibrationInfo">Polož telefon na 2 sekundy do klidu pro kalibraci odchylky.</div>''')
s=s.replace('<label class="switch-line">Počítat Z / −Z <input id="includeZ" type="checkbox" role="switch"></label></div>',
'''<label class="switch-line">Počítat Z / −Z <input id="includeZ" type="checkbox" role="switch"></label></div>
      <div class="autozero-row"><label class="switch-line">Automaticky nulovat při klidu IMU <input type="checkbox" id="autoZero" checked role="switch"></label></div>''')
s=s.replace('Rychlost vynuluj, když telefon skutečně stojí.',
'''Kalibruj a nuluj jen při skutečném stání. Automatický klid senzorů nepozná pohyb stálou rychlostí – může jej mylně vynulovat.''')
s=s.replace('◎ Vynulovat rychlost','◎ Vynulovat a kalibrovat')
s=s.replace('V1.1','V1.2')
html.write_text(s)
js=root/'app/src/main/assets/app.js'
s=js.read_text()
s=s.replace("    $('includeZ').checked=state.includeZ;", """    $('includeZ').checked=state.includeZ;
    $('autoZero').checked=s.autoZero!==false;""")
s=s.replace("  $('speedStatus').textContent=Number.isFinite(est)?'INERCIÁLNÍ ODHAD':'ČEKÁ NA SENZORY';", """  const phase=p.velocityState||'CALIBRATION';
  const phases={CALIBRATION:'KALIBRACE',TRACKING:'ODHAD RYCHLOSTI',AUTO_ZERO:'IMU V KLIDU',IMU_QUIET:'KLID SENZORŮ',GAP:'VÝPADEK VZORKŮ'};
  $('speedStatus').textContent=native?(phases[phase]||'ČEKÁ NA SENZORY'):'WEBOVÝ ODHAD';
  $('calibrationInfo').textContent=!native?'Webový režim není kalibrován stejně jako Android APK.':
       !p.velocityCalibrated?'Drž telefon skutečně v klidu · kalibrace '+(p.calibrationPercent||0)+' %':
       phase==='AUTO_ZERO'?'Klid IMU: rychlost se automaticky vynulovala. Pozor na pohyb konstantní rychlostí.':
       'Kalibrace hotová. Při dlouhém pohybu poroste integrační odchylka.';
""")
s=s.replace("  showToast('Rychlost vynulována. Předpoklad: telefon stojí.');",
            "  showToast('Nová kalibrace: drž telefon asi 2 s bez pohybu.');")
assert "$('includeZ').addEventListener" in s
s=s.replace("$('includeZ').addEventListener", """$('autoZero').addEventListener('change',(e)=>{
  if(native)window.MotionGridAndroid.setAutoZero(!!e.target.checked);
  else showToast('Automatická kalibrace a nulování jsou dostupné pouze v APK.');
});
$('includeZ').addEventListener""")
js.write_text(s)
css=root/'app/src/main/assets/styles.css'
with css.open('a') as f:
 f.write("""\n.calibration-info{margin-top:10px;padding:10px 13px;border-radius:10px;color:#bce7ed;background:#172d3e;font-size:11px;line-height:1.6}.autozero-row{border-top:1px solid #263447;padding:12px 24px;display:flex;align-items:center}.autozero-row input{flex:none}\n""")
print('MotionGrid v1.2 precision calibration patch applied')
