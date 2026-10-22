package cz.motiongrid.app;
/** Relative velocity estimate. IMU alone cannot determine absolute ground speed or distinguish steady travel from rest. */
public final class VelocityIntegrator {
    private static final long CALIBRATION_NS=1_700_000_000L,QUIET_NS=1_100_000_000L,MAX_GAP_NS=160_000_000L;
    private final float[] v=new float[3], bias=new float[3], previous=new float[3];
    private final double[] sums=new double[3];
    private boolean includeZ,autoZero,calibrated,hasPrev,quiet;
    private int calibrationCount,integratedSamples;
    private long lastNs,quietSinceNs,recentBrakingNs,stationaryConfirmedUntilNs;
    private String phase="CALIBRATION";
    public void reset(){
        for(int i=0;i<3;i++){v[i]=bias[i]=previous[i]=0;sums[i]=0;}
        calibrated=hasPrev=quiet=false;calibrationCount=integratedSamples=0;
        lastNs=quietSinceNs=recentBrakingNs=stationaryConfirmedUntilNs=0;phase="CALIBRATION";
    }
    public void confirmStop(){
        for(int i=0;i<3;i++){v[i]=0;previous[i]=0;}
        hasPrev=false;stationaryConfirmedUntilNs=lastNs+3_000_000_000L;phase="CONFIRMED_STOP";
    }
    public void setIncludeZ(boolean enabled){if(includeZ!=enabled){includeZ=enabled;v[2]=previous[2]=0;}}
    public void setAutoZero(boolean enabled){autoZero=enabled;}
    public boolean isAutoZero(){return autoZero;}
    public boolean isCalibrated(){return calibrated;}
    public boolean hasSamples(){return calibrated&&integratedSamples>0;}
    public float[] getVelocity(){return v.clone();}
    public float[] getBias(){return bias.clone();}
    public String getStatus(){return phase;}
    public boolean isStill(){return quiet;}
    public int calibrationPercent(long ns){return calibrated?100:quietSinceNs>0?Math.min(99,(int)Math.max(0,(ns-quietSinceNs)*100/CALIBRATION_NS)):0;}
    public float getSpeedMps(){return (float)Math.sqrt(v[0]*v[0]+v[1]*v[1]+(includeZ?v[2]*v[2]:0));}
    /** orientation is the row-major transform from device axes to user-aligned axes. */
    public void accept(float[] deviceLinear,float[] orientation,float[] rawAccel,float[] rawGyro,long ns){
        if(ns<=0||deviceLinear==null||orientation==null||rawAccel==null||rawGyro==null||
           deviceLinear.length!=3||orientation.length!=9||rawAccel.length!=3||rawGyro.length!=3)return;
        if(lastNs>0&&ns<=lastNs)return;
        for(float x:deviceLinear)if(!Float.isFinite(x))return;
        for(float x:orientation)if(!Float.isFinite(x))return;
        for(float x:rawAccel)if(!Float.isFinite(x))return;
        for(float x:rawGyro)if(!Float.isFinite(x))return;
        double an=norm(rawAccel),gn=norm(rawGyro),ln=norm(deviceLinear);
        boolean imuQuiet=Math.abs(an-9.80665)<0.23&&gn<0.045&&ln<0.26;
        if(imuQuiet){if(quietSinceNs==0)quietSinceNs=ns;}else quietSinceNs=0;
        quiet=imuQuiet&&ns-quietSinceNs>=QUIET_NS;
        if(!calibrated){
            if(!imuQuiet){calibrationCount=0;for(int i=0;i<3;i++)sums[i]=0;}
            else{
                calibrationCount++;for(int i=0;i<3;i++)sums[i]+=deviceLinear[i];
                if(calibrationCount>=35&&ns-quietSinceNs>=CALIBRATION_NS){
                    for(int i=0;i<3;i++)bias[i]=(float)(sums[i]/calibrationCount);
                    calibrated=true;hasPrev=false;phase="READY";
                }
            }
            lastNs=ns;return;
        }
        final long gap=lastNs==0?0:ns-lastNs;lastNs=ns;
        if(gap>MAX_GAP_NS){hasPrev=false;phase="GAP";return;}
        // Calibrate in device coordinates, then rotate so orientation changes do not rotate the bias incorrectly.
        float[] corrected=new float[3];
        for(int i=0;i<3;i++)corrected[i]=deviceLinear[i]-bias[i];
        float[] a=new float[3];
        for(int i=0;i<3;i++)for(int j=0;j<3;j++)a[i]+=orientation[3*i+j]*corrected[j];
        if(!includeZ)a[2]=0;
        double dt=gap*1.e-9;
        if(dt>0&&dt<=0.16){
            if(hasPrev)for(int i=0;i<3;i++)v[i]+=(float)(.5*(a[i]+previous[i])*dt);
            else for(int i=0;i<3;i++)v[i]+=(float)(a[i]*dt);
            integratedSamples++;
        }
        System.arraycopy(a,0,previous,0,3);hasPrev=true;
        float speed=getSpeedMps();
        double dot=v[0]*a[0]+v[1]*a[1]+(includeZ?v[2]*a[2]:0);
        double projected=speed>0.08?dot/speed:0;
        if(projected<-.13){recentBrakingNs=ns;phase="BRAKING";}
        else if(projected>.13)phase="ACCELERATING";
        else phase="COASTING_UNCERTAIN";
        if(quiet){
            if(ns<=stationaryConfirmedUntilNs){
                for(int i=0;i<3;i++)bias[i]=.999f*bias[i]+.001f*deviceLinear[i];
            }
            // Opt-in heuristic: only after observed braking AND small computed speed; never simply when a=0.
            if(autoZero&&speed<0.45f&&recentBrakingNs>0&&ns-recentBrakingNs<4_000_000_000L){
                for(int i=0;i<3;i++)v[i]=previous[i]=0;
                hasPrev=false;phase="AUTO_STOP_CANDIDATE";
            }else if(ns<=stationaryConfirmedUntilNs)phase="CONFIRMED_STOP";
            else phase="MOTION_AMBIGUOUS";
        }
    }
    private static double norm(float[] x){return Math.sqrt(x[0]*x[0]+x[1]*x[1]+x[2]*x[2]);}
}
