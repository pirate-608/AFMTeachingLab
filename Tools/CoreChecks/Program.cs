using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using AfmLab.Core;

static class Program
{
    static int failures, checks;
    static readonly List<object> outcomes=new List<object>();
    static void Check(string label,bool ok,string detail="")
    {
        checks++; if(!ok)failures++;
        outcomes.Add(new {name=label,passed=ok,detail=detail});
        Console.WriteLine((ok?"PASS ":"FAIL ")+label+(detail.Length>0?" : "+detail:""));
    }
    static bool Near(double a,double b,double tolerance) { return Math.Abs(a-b)<=tolerance; }
    static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="sweep") { Sweep(args.Length>1?args[1]:"Reports/approach-retract.csv"); return 0; }
        if(args.Length>0 && args[0]=="convergence") { Convergence(); return 0; }
        if(args.Length>0 && args[0]=="scan") { ScanTrials(); return 0; }
        if(args.Length>0 && args[0]=="matrix") { return Matrix(args.Length>1?args[1]:"Reports/scan-matrix.json"); }
        var optical=new OpticalLeverModel { LaserFraction=1 };
        Check("F/k",Near(optical.DeflectionM(10e-9),10e-9,1e-20));
        // Fraction <1 avoids beam falling off the edge when the positive load shifts the intersection.
        optical.LaserFraction=.95;
        var a=optical.Read(10e-9); var b=optical.Read(20e-9); var zero=optical.Read(0);
        Check("Optical ray valid and zero centred",a.Valid && zero.Valid && Near(zero.SpotVerticalM,0,1e-15));
        Check("Small-angle optical gain",Near(Math.Abs(a.SpotVerticalM),2*.05*10e-9/200e-6*OpticalLeverModel.StaticSlopeFactor(.95),2e-8),a.SpotVerticalM.ToString("G8"));
        Check("Small-load linearity",Near(b.SpotVerticalM/a.SpotVerticalM,2,.002));
        optical.CalibrateZero(10e-9);
        Check("Zero persists across new readings",Near(optical.DisplacementFromZeroM(optical.Read(10e-9)),0,1e-15) && Math.Abs(optical.DisplacementFromZeroM(zero))>1e-6);
        Check("Out-of-range optical reading invalid",!optical.Read(8e-6).Valid);
        Check("Modal free end normalized",Near(OpticalLeverModel.ModeShape(1),1,1e-10));
        var dynamicOptics=new OpticalLeverModel();
        double detectorGain=(dynamicOptics.Read(.1e-9,true).VerticalSignal-dynamicOptics.Read(-.1e-9,true).VerticalSignal)/(.2e-9);
        double maxLinearReadoutError=0;
        foreach(double qNm in new[]{-45.0,-30,-20,-10,10,20,30,45})
            maxLinearReadoutError=Math.Max(maxLinearReadoutError,Math.Abs(dynamicOptics.Read(qNm*1e-9,true).VerticalSignal/detectorGain/(qNm*1e-9)-1));
        Check("Calibrated dynamic optical approximation within 1 percent at +/-45 nm",maxLinearReadoutError<.01,maxLinearReadoutError.ToString("G5"));
        var force=new TipSampleForce();
        double equilibrium=Math.Pow(2,1.0/6)*force.SigmaNm;
        Check("LJ equilibrium",Math.Abs(force.Force(equilibrium))<1e-12);
        Check("LJ signs",force.Force(5)>0 && force.Force(7)<0);
        foreach(double d in new[]{3.5,4.24,4.26,5.0,6.0,12.0})
        {
            double eps=1e-5, derivative=(force.Potential(d+eps)-force.Potential(d-eps))/(2*eps);
            Check("Force is minus potential derivative @ "+d,Math.Abs(derivative+force.Force(d))<1e-4);
        }
        Check("Cutoff continuation continuous",Math.Abs(force.Force(force.CutoffNm-1e-8)-force.Force(force.CutoffNm+1e-8))<1e-4);
        var p=new CantileverParameters();
        foreach(double ratio in new[]{.95,1.0,1.05})
        {
            p.DriveRatio=ratio;
            var osc=new CantileverModel(p) { InteractionEnabled=false };
            CycleReading reading=default(CycleReading);
            for(int n=0;n<500;n++)reading=osc.StepCycle();
            Check("Driven amplitude analytic ratio="+ratio,Near(reading.AmplitudeNm,p.FreeSteadyAmplitudeNm,.001),reading.AmplitudeNm.ToString("F6"));
        }
        var ring=new CantileverModel {DriveEnabled=false,InteractionEnabled=false};
        ring.Reset(20); var r1=ring.StepCycle(256); CycleReading r2=default(CycleReading);
        for(int n=0;n<20;n++)r2=ring.StepCycle(256);
        Check("Free ringdown damping",Near(r2.AmplitudeNm/r1.AmplitudeNm,Math.Exp(-20*Math.PI/100),.0001));
        CycleReading coarse=Approach(128,.5,22), fine=Approach(256,.5,22);
        Check("Interacting amplitude timestep convergence",Math.Abs(coarse.AmplitudeNm/fine.AmplitudeNm-1)<.001);
        Check("Peak force timestep convergence separately",Math.Abs(coarse.PeakForceNn/fine.PeakForceNn-1)<.02);
        foreach(double gap in new[]{14.0,18,26,36,42})
        {
            var a128=Approach(128,.5,gap);var a256=Approach(256,.5,gap);
            if(gap>36){var far=new CantileverModel{GapNm=gap};for(int n=0;n<500;n++)a128=far.StepCycle(128);far=new CantileverModel{GapNm=gap};for(int n=0;n<500;n++)a256=far.StepCycle(256);}
            Check("Approach convergence at gap="+gap,a128.Valid && a256.Valid && Math.Abs(a128.AmplitudeNm/a256.AmplitudeNm-1)<.01 && Math.Abs(a128.PeakForceNn/a256.PeakForceNn-1)<.02,$"A err={Math.Abs(a128.AmplitudeNm/a256.AmplitudeNm-1):G3}; force err={Math.Abs(a128.PeakForceNn/a256.PeakForceNn-1):G3}");
        }
        var jumped=Approach(256,2,22);
        Check("LJ loading-path dependence retained",Math.Abs(jumped.AmplitudeNm-fine.AmplitudeNm)>.5 && jumped.MeanNm>0 && fine.MeanNm<0);
        var pid=new PidController(new PidSettings {Kp=2,Ki=100,Kd=0}); pid.Reset(0,0);
        for(int n=0;n<1000;n++)pid.Step(1,0,.001,-.1,.1);
        Check("PID saturation anti-windup",Math.Abs(pid.Integral)<.11 && pid.Saturated);
        Check("PID recovers after saturation",Math.Abs(pid.Step(0,0,.001,-.1,.1))<.11);
        var dpid=new PidController(new PidSettings {Kp=0,Ki=0,Kd=1});dpid.Reset(4,.8);
        Check("Derivative has no setpoint kick",Near(dpid.Step(.9,.8,.0001,0,10),4,1e-12));
        var act=new ZActuator {MaxSpeed=100};act.Reset(20);act.Step(40,.001);
        Check("Actuator has finite slew",Near(act.Position,20.1,1e-12) && act.SlewLimited);
        ScanSimulator good=RunRows(8,40000,.08), fast=RunRows(8,40000,.008), weak=RunRows(3,1000,.08), high=RunRows(40,60000,.08);
        Check("Stable scan valid",good.State==ScanState.Scanning && good.ValidFraction==1);
        Check("Stable scan height RMSE < 5 percent of 4 nm range",good.Rmse<.2,good.Rmse.ToString("F6"));
        Check("Speed produces tracking distortion",fast.Rmse>good.Rmse*3,$"slow={good.Rmse:F4} fast={fast.Rmse:F4}");
        Check("Low gains produce lag",weak.Rmse>good.Rmse*3,weak.Rmse.ToString("F4"));
        Check("High gains degrade tracking",high.Rmse>good.Rmse*3,high.Rmse.ToString("F4"));
        Check("Comparison presets share reference",Near(good.ReferenceZ,high.ReferenceZ,1e-12) && Near(good.ReferenceZ,weak.ReferenceZ,1e-12));
        ScanSimulator baseline=null;
        foreach(int fps in new[]{30,60,120})
        {
            var s=new ScanSimulator(new ScanSettings()); var clock=new SimulationClock();
            for(int frame=0;frame<fps;frame++)clock.Advance(1.0/fps,.3,s.CycleSeconds,10000,s.Step);
            while(clock.PendingSeconds>=s.CycleSeconds)clock.Advance(0,0,s.CycleSeconds,10000,s.Step);
            if(baseline==null)baseline=s;
            Check("Frame-rate independent @ "+fps,Near(s.Stage.Position,baseline.Stage.Position,1e-10) && s.CompletedPixels==baseline.CompletedPixels && Near(s.Rmse,baseline.Rmse,1e-10));
        }
        var budgetClock=new SimulationClock();int ticks=0;
        budgetClock.Advance(.1,1,.001,10,()=>ticks++);
        Check("Work budget preserves pending simulation time",ticks==10 && Near(budgetClock.PendingSeconds,.09,1e-12));
        Check("Clock reports integrated time, not requested time",Near(budgetClock.SimulatedSeconds,.01,1e-12));
        var oneLine=new ScanSimulator(new ScanSettings{SingleLine=true});
        while(oneLine.State==ScanState.Calibrating || oneLine.State==ScanState.Scanning)oneLine.Step();
        int populated=0;foreach(bool valid in oneLine.Valid)if(valid)populated++;
        Check("Single-line scan acquires one complete centre row",oneLine.State==ScanState.Complete && oneLine.CompletedPixels==64 && populated==64 && oneLine.CurrentRow==32 && oneLine.Rmse<.2);
        Check("Retrace samples use spatial coordinates",MathEx.Finite(oneLine.Retrace[0]) && MathEx.Finite(oneLine.Retrace[63]) && oneLine.Retrace[32]>3.8 && oneLine.Retrace[0]<.2);
        var manual=new ScanSimulator(new ScanSettings{SingleLine=true,LineSeconds=.2,Feedback=false,ManualZOffsetNm=1});
        while(manual.State==ScanState.Calibrating)manual.Step();
        double frozenIntegral=manual.Controller.Integral;
        for(int n=0;n<750;n++)manual.Step();
        Check("Manual Z follows actuator dynamics while PID is frozen",Near(manual.Stage.Position,manual.ReferenceZ+1,.001) && Near(manual.Controller.Integral,frozenIntegral,1e-12));
        manual.Settings.Feedback=true;
        for(int n=0;n<1500;n++)manual.Step();
        Check("Feedback resumes from manual position",manual.State==ScanState.Scanning && Math.Abs(manual.AmplitudeError)<.05 && Math.Abs(manual.Stage.Position-manual.ReferenceZ)<.1);
        var invalid=new ScanSimulator(new ScanSettings{SingleLine=true});
        while(invalid.State==ScanState.Calibrating)invalid.Step();
        invalid.Stage.Reset(8);invalid.Step();
        double stoppedTime=invalid.SimulationSeconds,stoppedIntegral=invalid.Controller.Integral;
        for(int n=0;n<100;n++)invalid.Step();
        Check("Invalid force domain stops sampling and integration",invalid.State==ScanState.Fault && invalid.CompletedPixels==0 && invalid.SimulationSeconds==stoppedTime && invalid.Controller.Integral==stoppedIntegral);
        Console.WriteLine("RESULT "+(checks-failures)+"/"+checks+" passed");
        if(args.Length>1 && args[0]=="verify")
        {
            string path=Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonSerializer.Serialize(new {utc=DateTime.UtcNow,passed=checks-failures,total=checks,tests=outcomes},new JsonSerializerOptions{WriteIndented=true}));
        }
        return failures==0?0:1;
    }
    static CycleReading Approach(int steps,double spacing,double last)
    {
        var osc=new CantileverModel();CycleReading r=default(CycleReading);
        for(double gap=36;gap>=last;gap-=spacing)
        {
            osc.GapNm=gap;
            for(int n=0;n<500;n++)r=osc.StepCycle(steps);
        }
        return r;
    }
    static ScanSimulator RunRows(double p,double i,double line)
    {
        var s=new ScanSimulator(new ScanSettings {Pid=new PidSettings{Kp=p,Ki=i},LineSeconds=line});
        while(s.State!=ScanState.Complete && s.State!=ScanState.Fault && s.CurrentRow<3)s.Step();
        return s;
    }
    static int Matrix(string path)
    {
        var results=new List<object>();bool passed=true;
        foreach(SampleKind sample in Enum.GetValues(typeof(SampleKind)))
        foreach(var pair in new[]{new[]{8.0,40000,.08},new[]{8.0,40000,.008},new[]{3.0,1000,.08},new[]{40.0,60000,.08}})
        {
            var s=new ScanSimulator(new ScanSettings{Sample=sample,LineSeconds=pair[2],Pid=new PidSettings{Kp=pair[0],Ki=pair[1]}});
            var timer=Stopwatch.StartNew();
            while(s.State==ScanState.Calibrating || s.State==ScanState.Scanning)s.Step();
            bool ok=s.State==ScanState.Complete && s.CompletedPixels==4096 && s.ValidFraction>.99;
            passed&=ok;
            results.Add(new {sample=sample.ToString(),p=pair[0],i=pair[1],lineSeconds=pair[2],state=s.State.ToString(),rmseNm=s.Rmse,validFraction=s.ValidFraction,pixels=s.CompletedPixels,wallSeconds=timer.Elapsed.TotalSeconds,passed=ok});
            Console.WriteLine($"{sample} P={pair[0]} I={pair[1]} line={pair[2]} {s.State} rmse={s.Rmse:F4} valid={s.ValidFraction:P1} pixels={s.CompletedPixels}");
        }
        File.WriteAllText(path,JsonSerializer.Serialize(new {utc=DateTime.UtcNow,passed=passed,scans=results},new JsonSerializerOptions{WriteIndented=true}));
        return passed?0:1;
    }
    static void Sweep(string path)
    {
        var osc=new CantileverModel();
        var csv=new System.Text.StringBuilder("direction,gap_nm,amplitude_nm,mean_nm,min_gap_nm,peak_force_nn,valid\n");
        foreach(string direction in new[]{"approach","retract"})
        for(int point=0;point<=42;point++)
        {
            double gap=direction=="approach"?36-point*.5:15+point*.5;
            osc.GapNm=gap; CycleReading r=default(CycleReading);
            for(int n=0;n<500;n++) r=osc.StepCycle(512);
            csv.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1:F2},{2:F6},{3:F6},{4:F6},{5:F6},{6}",direction,gap,r.AmplitudeNm,r.MeanNm,r.MinGapNm,r.PeakForceNn,r.Valid));
        }
        File.WriteAllText(path,csv.ToString());Console.WriteLine("Saved approach/retract curves: "+path);
    }
    static void Convergence()
    {
        foreach(int steps in new[]{64,128,256,512,1024})
        {
            var sw=Stopwatch.StartNew();
            var osc=new CantileverModel();
            Console.WriteLine("steps="+steps);
            for(double g=36;g>=18;g-=2)
            {
                osc.GapNm=g; CycleReading r=default(CycleReading);
                for(int n=0;n<500;n++)r=osc.StepCycle(steps);
                Console.WriteLine($"{g:F1}: A={r.AmplitudeNm:F8} mean={r.MeanNm:F8} gap={r.MinGapNm:F8} F={r.PeakForceNn:F8} valid={r.Valid}");
            }
            Console.WriteLine("ms="+sw.Elapsed.TotalMilliseconds.ToString("F1"));
        }
    }
    static void ScanTrials()
    {
        foreach(var trial in new[]{new[]{12.0,12000,.2},new[]{12.0,12000,.08},new[]{12.0,12000,.02},new[]{12.0,12000,.005},new[]{3.0,1000,.08},new[]{20.0,30000,.08},new[]{8.0,40000,.08},new[]{40.0,60000,.08},new[]{80.0,90000,.08}})
        {
            var settings=new ScanSettings { Resolution=64,LineSeconds=trial[2],Pid=new PidSettings {Kp=trial[0],Ki=trial[1]} };
            var scan=new ScanSimulator(settings);
            var timer=Stopwatch.StartNew();
            while(scan.State!=ScanState.Complete && scan.State!=ScanState.Fault && scan.CurrentRow<3)scan.Step();
            Console.WriteLine($"P={trial[0]} I={trial[1]} line={trial[2]} state={scan.State} rmse={scan.Rmse:F4} valid={scan.ValidFraction:P1} ref={scan.ReferenceZ:F4} sim={scan.SimulationSeconds:F3} wall={timer.Elapsed.TotalSeconds:F3} fault={scan.Fault}");
        }
    }
}
