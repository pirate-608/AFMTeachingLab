using System;

namespace AfmLab.Core
{
    public enum SampleKind { Terrace, Grating, Islands }
    public enum ScanState { Calibrating, Scanning, Complete, Fault }
    public enum HistorySignal { Amplitude, Target, Error, Z }

    public static class Samples
    {
        // x,y are normalized scan coordinates. Output is nm. Point-tip, uniform rigid material.
        public static double Height(SampleKind sample,double x,double y)
        {
            if(sample==SampleKind.Terrace) return 2*(Math.Tanh((x-.28)/.025)-Math.Tanh((x-.70)/.025));
            if(sample==SampleKind.Grating) return 1.5*(1-Math.Cos(MathEx.Tau*3*x));
            double a=3.5*Math.Exp(-((x-.32)*(x-.32)/.010+(y-.38)*(y-.38)/.018));
            double b=4*Math.Exp(-((x-.68)*(x-.68)/.015+(y-.66)*(y-.66)/.012));
            double c=2.2*Math.Exp(-((x-.7)*(x-.7)/.004+(y-.25)*(y-.25)/.005));
            return a+b+c;
        }
    }

    [Serializable]
    public sealed class ScanSettings
    {
        public int Resolution=64;
        public SampleKind Sample=SampleKind.Terrace;
        public double LineSeconds=.08, SetpointRatio=.8;
        public bool Feedback=true;
        public bool SingleLine;
        public double ManualZOffsetNm;
        public int StepsPerCycle=128;
        public double DetectorSeconds=.00008;
        public PidSettings Pid=new PidSettings();
    }

    public sealed class ScanSimulator
    {
        public readonly ScanSettings Settings;
        public readonly CantileverModel Oscillator;
        public readonly PidController Controller;
        public readonly ZActuator Stage=new ZActuator();
        public readonly double[] TrueHeight, Reconstructed, Residual, Trace, Retrace;
        public readonly bool[] Valid;
        readonly double[] accum;
        readonly int[] counts,validCounts;
        readonly double[][] history={new double[180],new double[180],new double[180],new double[180]};
        int historyHead,historyCount,historyTicks;
        public int HistoryRevision { get; private set; }
        public ScanState State { get; private set; }
        public string Fault { get; private set; }="";
        public double MeasuredAmplitude { get; private set; }
        public double ReferenceZ { get; private set; }
        public double SimulationSeconds { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double SurfaceHeight { get; private set; }
        public double AmplitudeError { get { return Settings.SetpointRatio*Oscillator.Parameters.FreeAmplitudeNm-MeasuredAmplitude; } }
        public CycleReading LastCycle { get; private set; }
        public int CompletedPixels { get; private set; }
        public int TotalPixels { get { return Settings.Resolution*(Settings.SingleLine?1:Settings.Resolution); } }
        public int Revision { get; private set; }
        public int CurrentRow { get; private set; }
        public bool IsRetrace { get; private set; }
        public double Rmse { get; private set; }
        public double ValidFraction { get { return CompletedPixels==0?0:(double)goodPixels/CompletedPixels; } }
        public double CycleSeconds { get { return 1/(Oscillator.Parameters.FrequencyHz*Oscillator.Parameters.DriveRatio); } }
        int initializationCycles,stableCycles,goodPixels,lastIndex=-1;
        double scanTime,squaredError;
        bool previousFeedback=true;

        public ScanSimulator(ScanSettings settings)
        {
            if(settings.Resolution<4 || settings.Resolution>256 || settings.LineSeconds<=0 || settings.SetpointRatio<.6 || settings.SetpointRatio>.95)
                throw new ArgumentOutOfRangeException("Invalid scan settings");
            Settings=settings; Oscillator=new CantileverModel(); Controller=new PidController(new PidSettings { Kp=12,Ki=12000 });
            int count=settings.Resolution*settings.Resolution;
            TrueHeight=new double[count]; Reconstructed=new double[count]; Residual=new double[count]; Valid=new bool[count];
            accum=new double[count]; counts=new int[count]; validCounts=new int[count];
            Trace=new double[settings.Resolution]; Retrace=new double[settings.Resolution];
            for(int i=0;i<count;i++) { Reconstructed[i]=double.NaN; Residual[i]=double.NaN; }
            for(int i=0;i<Trace.Length;i++) { Trace[i]=double.NaN; Retrace[i]=double.NaN; }
            Stage.Reset(36); Oscillator.GapNm=36; MeasuredAmplitude=20;
            Controller.Reset(36,1); State=ScanState.Calibrating;
            // Ground truth is evaluated independently for the display/metrics. Never passed to PID.
            for(int row=0;row<settings.Resolution;row++)for(int col=0;col<settings.Resolution;col++)
                TrueHeight[row*settings.Resolution+col]=Samples.Height(settings.Sample,(col+.5)/settings.Resolution,(row+.5)/settings.Resolution);
        }

        public void Step()
        {
            if(State==ScanState.Complete || State==ScanState.Fault) return;
            double dt=CycleSeconds;
            bool acquiring=false;
            int column=0;
            if(State==ScanState.Scanning)
            {
                double turnaround=Math.Max(.002,Settings.LineSeconds*.08);
                double rowDuration=2*Settings.LineSeconds+2*turnaround;
                int row=(int)(scanTime/rowDuration);
                if(row>=(Settings.SingleLine?1:Settings.Resolution)) { FinishPixel(lastIndex); State=ScanState.Complete; return; }
                int surfaceRow=Settings.SingleLine?Settings.Resolution/2:row;
                if(surfaceRow!=CurrentRow)
                {
                    CurrentRow=surfaceRow;
                    for(int i=0;i<Trace.Length;i++) { Trace[i]=double.NaN; Retrace[i]=double.NaN; }
                }
                double phase=scanTime-row*rowDuration;
                Y=(surfaceRow+.5)/Settings.Resolution;
                if(phase<Settings.LineSeconds) { X=phase/Settings.LineSeconds; IsRetrace=false; acquiring=true; }
                else if(phase<Settings.LineSeconds+turnaround) { X=1; IsRetrace=false; }
                else if(phase<2*Settings.LineSeconds+turnaround) { X=1-(phase-Settings.LineSeconds-turnaround)/Settings.LineSeconds; IsRetrace=true; acquiring=true; }
                else
                {
                    X=0; IsRetrace=true;
                    // Finite Y transition during blanked turnaround; state and integral persist.
                    double t=(phase-2*Settings.LineSeconds-turnaround)/turnaround;
                    if(!Settings.SingleLine)Y=(row+.5+MathEx.Clamp(t,0,1))/Settings.Resolution;
                }
                SurfaceHeight=Samples.Height(Settings.Sample,X,Y);
                column=Math.Min(Settings.Resolution-1,(int)(X*Settings.Resolution));
            }
            else { X=0; Y=0; SurfaceHeight=0; }
            double acquiredZ=Stage.Position;
            Oscillator.GapNm=acquiredZ-SurfaceHeight;
            LastCycle=Oscillator.StepCycle(Settings.StepsPerCycle);
            // Calibrated, ideal linear optical readout. Finite detector bandwidth is explicit here.
            MeasuredAmplitude+=(LastCycle.AmplitudeNm-MeasuredAmplitude)*(1-Math.Exp(-dt/Settings.DetectorSeconds));
            SimulationSeconds+=dt;
            if(++historyTicks>=40)
            {
                historyTicks=0;history[0][historyHead]=MeasuredAmplitude;
                history[1][historyHead]=Settings.SetpointRatio*20;history[2][historyHead]=AmplitudeError;
                history[3][historyHead]=acquiredZ;historyHead=(historyHead+1)%180;
                historyCount=Math.Min(180,historyCount+1);HistoryRevision++;
            }
            bool signalValid=LastCycle.Valid && MathEx.Finite(MeasuredAmplitude);
            if(!signalValid) { Fault="针尖进入正则化区，已停止采样；请复位并降低扫描速度或增益。"; State=ScanState.Fault; return; }
            bool feedback=State==ScanState.Calibrating || Settings.Feedback;
            // Re-enter automatic control from the actual manual position. Turnarounds do not reset PID.
            if(feedback && !previousFeedback)Controller.Reset(Stage.Position,MeasuredAmplitude/20);
            previousFeedback=feedback;
            if(State==ScanState.Scanning)
            {
                Controller.Settings.Kp=Settings.Pid.Kp; Controller.Settings.Ki=Settings.Pid.Ki;
                Controller.Settings.Kd=Settings.Pid.Kd; Controller.Settings.DerivativeFilterSeconds=Settings.Pid.DerivativeFilterSeconds;
            }
            if(State!=ScanState.Calibrating || initializationCycles>600)
            {
                double command=Controller.Step(Settings.SetpointRatio,MeasuredAmplitude/20,dt,Stage.Minimum,Stage.Maximum,feedback);
                if(feedback) Stage.Step(command,dt);
                else Stage.Step(ReferenceZ+Settings.ManualZOffsetNm,dt);
                if(feedback && Stage.SlewLimited) Controller.TrackSlewLimit(Stage.Position,dt,.001);
            }
            if(State==ScanState.Calibrating)
            {
                initializationCycles++;
                // All comparison presets initialize with the same stable baseline gains.
                if(initializationCycles>600 && Math.Abs(AmplitudeError)<.004 && Math.Abs(Controller.Output-Stage.Position)<.002) stableCycles++;
                else stableCycles=0;
                if(stableCycles>=300)
                {
                    ReferenceZ=Stage.Position; State=ScanState.Scanning;
                    scanTime=0; Controller.Reset(Stage.Position,MeasuredAmplitude/20);
                }
                if(initializationCycles>20000) { Fault="初始反馈未能稳定。请使用稳定预设后重新扫描。"; State=ScanState.Fault; }
                return;
            }
            if(acquiring)
            {
                int index=CurrentRow*Settings.Resolution+column;
                double height=acquiredZ-ReferenceZ;
                if(!IsRetrace)
                {
                    if(index!=lastIndex) { FinishPixel(lastIndex); lastIndex=index; }
                    accum[index]+=height; counts[index]++;
                    if(signalValid && !Controller.Saturated && !Stage.SlewLimited) validCounts[index]++;
                    Reconstructed[index]=accum[index]/counts[index];
                    Trace[column]=Reconstructed[index];
                }
                else { Retrace[column]=height; }
            }
            else if(!IsRetrace) { FinishPixel(lastIndex); lastIndex=-1; }
            scanTime+=dt;
        }
        void FinishPixel(int index)
        {
            if(index<0 || counts[index]==0 || Valid[index] || !double.IsNaN(Residual[index])) return;
            CompletedPixels++;
            Valid[index]=validCounts[index]==counts[index];
            Residual[index]=Reconstructed[index]-TrueHeight[index];
            if(Valid[index]) { squaredError+=Residual[index]*Residual[index]; goodPixels++; Rmse=Math.Sqrt(squaredError/goodPixels); }
            Revision++;
        }
        public void CopyHistory(HistorySignal signal,double[] destination)
        {
            if(destination.Length!=180)throw new ArgumentException("History buffers contain 180 samples");
            for(int n=0;n<180;n++)
                destination[n]=n<180-historyCount?double.NaN:history[(int)signal][(historyHead+n)%180];
        }
    }
}
