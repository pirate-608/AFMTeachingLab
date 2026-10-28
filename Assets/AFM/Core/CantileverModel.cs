using System;

namespace AfmLab.Core
{
    [Serializable]
    public sealed class CantileverParameters
    {
        public double Stiffness=3, FrequencyHz=75000, Q=100, FreeAmplitudeNm=20, DriveRatio=1, DriveForceNn=.6;
        public double Omega { get { return MathEx.Tau*FrequencyHz; } }
        public double MassKg { get { return Stiffness/(Omega*Omega); } }
        public double Damping { get { return Stiffness/(Omega*Q); } }
        public double RingdownSeconds { get { return 2*Q/Omega; } }
        public double FreeSteadyAmplitudeNm
        {
            get { double r=DriveRatio; return DriveForceNn/Stiffness/Math.Sqrt((1-r*r)*(1-r*r)+r*r/(Q*Q)); }
        }
        public CantileverParameters Copy() { return (CantileverParameters)MemberwiseClone(); }
        public void Validate()
        {
            if(Stiffness<=0 || FrequencyHz<=0 || Q<=.5 || FreeAmplitudeNm<=0 || DriveRatio<=0 || DriveForceNn<0)
                throw new ArgumentOutOfRangeException("Invalid cantilever parameters");
        }
    }

    public struct CycleReading
    {
        public double AmplitudeNm, PhaseRad, MeanNm, MinGapNm, PeakForceNn;
        public bool Valid;
    }

    public sealed class CantileverModel
    {
        public readonly CantileverParameters Parameters;
        public readonly TipSampleForce Interaction;
        public double GapNm=60;
        public bool DriveEnabled=true, InteractionEnabled=true;
        public double[] LastWaveformNm { get; private set; } = new double[128];
        public double PositionNm { get { return q*Parameters.FreeAmplitudeNm; } }
        public double VelocityNmPerSecond { get { return v*Parameters.FreeAmplitudeNm*Parameters.Omega; } }
        public double TimeSeconds { get { return tau/Parameters.Omega; } }
        public double InstantGapNm { get { return GapNm+PositionNm; } }
        public double InstantForceNn { get { return InteractionEnabled?Interaction.Force(InstantGapNm):0; } }
        double q,v,tau;
        public CantileverModel(CantileverParameters p=null,TipSampleForce force=null)
        {
            Parameters=(p??new CantileverParameters()).Copy(); Parameters.Validate();
            Interaction=force??new TipSampleForce();
        }
        public void Reset(double positionNm=0,double velocityNmPerSecond=0)
        {
            q=positionNm/Parameters.FreeAmplitudeNm;
            v=velocityNmPerSecond/(Parameters.FreeAmplitudeNm*Parameters.Omega); tau=0;
        }
        double Acceleration(double position,double velocity,double time)
        {
            double force=InteractionEnabled?Interaction.Force(GapNm+position*Parameters.FreeAmplitudeNm)/(Parameters.Stiffness*Parameters.FreeAmplitudeNm):0;
            return -position-velocity/Parameters.Q+(DriveEnabled?Parameters.DriveForceNn/(Parameters.Stiffness*Parameters.FreeAmplitudeNm)*Math.Cos(Parameters.DriveRatio*time):0)+force;
        }
        public void StepDimensionless(double h)
        {
            double a1=v,b1=Acceleration(q,v,tau);
            double a2=v+h*b1*.5,b2=Acceleration(q+h*a1*.5,a2,tau+h*.5);
            double a3=v+h*b2*.5,b3=Acceleration(q+h*a2*.5,a3,tau+h*.5);
            double a4=v+h*b3,b4=Acceleration(q+h*a3,a4,tau+h);
            q+=h/6*(a1+2*a2+2*a3+a4); v+=h/6*(b1+2*b2+2*b3+b4); tau+=h;
            if(!MathEx.Finite(q) || Math.Abs(q)>1000) throw new ArithmeticException("Oscillator left numerical domain");
        }
        // Synchronous single-cycle Fourier measurement, including its one-cycle acquisition delay.
        public CycleReading StepCycle(int steps=512)
        {
            if(LastWaveformNm.Length!=steps) LastWaveformNm=new double[steps];
            double dt=MathEx.Tau/(Parameters.DriveRatio*steps), i=0,j=0,mean=0,min=double.MaxValue,peak=0;
            for(int n=0;n<steps;n++)
            {
                StepDimensionless(dt);
                double pos=PositionNm, phase=tau*Parameters.DriveRatio;
                LastWaveformNm[n]=pos;
                i+=pos*Math.Cos(phase); j+=pos*Math.Sin(phase); mean+=pos;
                min=Math.Min(min,InstantGapNm); peak=Math.Max(peak,Math.Abs(InstantForceNn));
            }
            return new CycleReading { AmplitudeNm=2*Math.Sqrt(i*i+j*j)/steps, PhaseRad=Math.Atan2(-j,i), MeanNm=mean/steps, MinGapNm=min, PeakForceNn=peak, Valid=Interaction.InPhysicalDomain(min) };
        }
    }
}
