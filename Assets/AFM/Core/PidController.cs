using System;

namespace AfmLab.Core
{
    [Serializable]
    public sealed class PidSettings
    {
        public double Kp=8, Ki=40000, Kd=0;
        public double DerivativeFilterSeconds=.0002;
    }
    public sealed class PidController
    {
        public readonly PidSettings Settings;
        public double Integral { get; private set; }
        public double Output { get; private set; }
        public bool Saturated { get; private set; }
        double bias,previousMeasurement,derivative;
        public PidController(PidSettings settings) { Settings=settings; }
        public void Reset(double position,double measurement)
        {
            bias=position; Integral=0; previousMeasurement=measurement; derivative=0; Output=position; Saturated=false;
        }
        public double Step(double target,double measurement,double dt,double min,double max,bool enabled=true)
        {
            if(dt<=0) throw new ArgumentOutOfRangeException("dt");
            if(!enabled || !MathEx.Finite(measurement)) return Output;
            double rate=(measurement-previousMeasurement)/dt;
            derivative+=(rate-derivative)*(1-Math.Exp(-dt/Math.Max(1e-8,Settings.DerivativeFilterSeconds)));
            previousMeasurement=measurement;
            double error=target-measurement, nextIntegral=Integral+Settings.Ki*error*dt;
            double candidate=bias+Settings.Kp*error+nextIntegral-Settings.Kd*derivative;
            // Conditional integration prevents position-limit windup.
            if((candidate<=max || error<0) && (candidate>=min || error>0)) Integral=nextIntegral;
            double raw=bias+Settings.Kp*error+Integral-Settings.Kd*derivative;
            Output=MathEx.Clamp(raw,min,max); Saturated=raw!=Output;
            return Output;
        }
        // Back calculation tracks the position actually attainable under the actuator's slew limit.
        public void TrackSlewLimit(double achievable,double dt,double trackingSeconds)
        {
            Integral+=(achievable-Output)*(1-Math.Exp(-dt/Math.Max(dt,trackingSeconds)));
        }
    }
    public sealed class ZActuator
    {
        public double Position { get; private set; }
        public double TimeConstant=.00035, MaxSpeed=20000, Minimum=8, Maximum=50;
        public bool SlewLimited { get; private set; }
        public void Reset(double z) { Position=MathEx.Clamp(z,Minimum,Maximum); SlewLimited=false; }
        public void Step(double command,double dt)
        {
            double change=(MathEx.Clamp(command,Minimum,Maximum)-Position)*(1-Math.Exp(-dt/TimeConstant));
            double limited=MathEx.Clamp(change,-MaxSpeed*dt,MaxSpeed*dt);
            SlewLimited=change!=limited;
            Position=MathEx.Clamp(Position+limited,Minimum,Maximum);
        }
    }
}
