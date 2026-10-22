import cz.motiongrid.app.VelocityIntegrator;
public final class VelocityTest {
  static long t=1_000_000_000L;
  static final float[] I={1,0,0,0,1,0,0,0,1};
  static final float[] ROT={0,-1,0,1,0,0,0,0,1};
  static final float[] G={0,0,9.80665f},ZERO={0,0,0};
  static void step(VelocityIntegrator v,float x,float y,float z,float[] R){
    t+=20_000_000L;
    v.accept(new float[]{x,y,z},R,G,ZERO,t);
  }
  static void samples(VelocityIntegrator v,int n,float x,float y,float z,float[] R){
    for(int i=0;i<n;i++)step(v,x,y,z,R);
  }
  static void ok(boolean condition,String what){
    if(!condition)throw new AssertionError(what);
    System.out.println("PASS "+what);
  }
  static void near(float actual,float wanted,float tolerance,String what){
    ok(Math.abs(actual-wanted)<tolerance,what+" got "+actual+" expected "+wanted);
  }
  public static void main(String[] args){
    VelocityIntegrator v=new VelocityIntegrator();
    samples(v,110,.04f,0,0,I);
    ok(v.isCalibrated(),"calibrate stationary bias");
    near(v.getSpeedMps(),0,.001f,"initial velocity zero");
    samples(v,100,1.04f,0,0,I);
    near(v.getVelocity()[0],2,.08f,"2 seconds acceleration +X");
    samples(v,100,.04f,0,0,I);
    near(v.getVelocity()[0],2,.08f,"coasting preserves speed");
    ok(v.getStatus().equals("MOTION_AMBIGUOUS"),"constant velocity indistinguishable from rest");
    samples(v,100,-.96f,0,0,I);
    near(v.getVelocity()[0],0,.09f,"braking returns close to zero");
    samples(v,75,.04f,0,0,I);
    near(v.getVelocity()[0],0,.09f,"no spurious velocity in rest");
    v.confirmStop();
    near(v.getSpeedMps(),0,.0001f,"manual confirmed stop");
    v.reset();
    samples(v,110,0,0,0,I);
    samples(v,100,0,-1,0,ROT);
    near(v.getVelocity()[0],2,.08f,"rotated device -Y maps to reference +X");
    near(v.getVelocity()[1],0,.05f,"rotation creates no spurious perpendicular velocity");
    v.reset();
    samples(v,110,0,0,0,I);
    samples(v,100,0,0,2,I);
    near(v.getSpeedMps(),0,.01f,"Z disabled by default");
    v.setIncludeZ(true);
    samples(v,100,0,0,2,I);
    ok(v.getVelocity()[2]>3.5f,"optional Z velocity works");
    System.out.println("ALL VELOCITY TESTS PASSED");
  }
}
