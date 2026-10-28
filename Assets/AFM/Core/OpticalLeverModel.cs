using System;

namespace AfmLab.Core
{
    public struct OpticalReading
    {
        public DVec3 ReflectionPoint, ReflectedDirection, Hit;
        public double SpotVerticalM, SpotHorizontalM, VerticalSignal, TotalPower;
        public bool Valid;
    }

    // All geometry here is SI. Visualization scaling never enters this model.
    public sealed class OpticalLeverModel
    {
        public double LengthM = 200e-6;
        public double StiffnessNPerM = 1;
        public double LaserFraction = .95;
        public double DetectorDistanceM = .05;
        public double DetectorHalfSizeM = .0015;
        public double BeamSigmaM = .00015;
        public double ZeroSpotM { get; private set; }
        public double DeflectionM(double forceN) { return forceN / StiffnessNPerM; }
        public static double StaticShape(double s) { return s*s*(3-s)*.5; }
        public static double StaticSlopeFactor(double s) { return 1.5*s*(2-s); }
        public static double ModeShape(double s)
        {
            const double b=1.875104068711961;
            double a=(Math.Cosh(b)+Math.Cos(b))/(Math.Sinh(b)+Math.Sin(b));
            return (Math.Cosh(b*s)-Math.Cos(b*s)-a*(Math.Sinh(b*s)-Math.Sin(b*s)))*.5;
        }
        public static double ModeSlopeFactor(double s)
        {
            const double b=1.875104068711961;
            double a=(Math.Cosh(b)+Math.Cos(b))/(Math.Sinh(b)+Math.Sin(b));
            return b*(Math.Sinh(b*s)+Math.Sin(b*s)-a*(Math.Cosh(b*s)-Math.Cos(b*s)))*.5;
        }
        public void CalibrateZero(double tipDeflectionM) { ZeroSpotM=Read(tipDeflectionM).SpotVerticalM; }
        public double DisplacementFromZeroM(OpticalReading r) { return r.SpotVerticalM-ZeroSpotM; }

        public OpticalReading Read(double tipDeflectionM, bool dynamicMode=false)
        {
            if(LengthM<=0 || StiffnessNPerM<=0 || BeamSigmaM<=0 || DetectorDistanceM<=0)
                throw new ArgumentOutOfRangeException("Optical dimensions must be positive");
            double s=MathEx.Clamp(LaserFraction,0,1);
            // Fixed beam aimed at the undeformed point. Solve x/y intersection with curved beam.
            var incident=new DVec3(.6,-.8,0);
            double targetX=s*LengthM;
            double x=targetX;
            for(int j=0;j<8;j++)
            {
                double f=x/LengthM;
                double w=tipDeflectionM*(dynamicMode?ModeShape(f):StaticShape(f));
                double slope=tipDeflectionM/LengthM*(dynamicMode?ModeSlopeFactor(f):StaticSlopeFactor(f));
                double residual=x-targetX-incident.X/incident.Y*w;
                x-=residual/(1-incident.X/incident.Y*slope);
            }
            double fraction=x/LengthM;
            double y=tipDeflectionM*(dynamicMode?ModeShape(fraction):StaticShape(fraction));
            double gradient=tipDeflectionM/LengthM*(dynamicMode?ModeSlopeFactor(fraction):StaticSlopeFactor(fraction));
            var point=new DVec3(x,y,0);
            var normal=new DVec3(-gradient,1,0).Normalized;
            var ray=DVec3.Reflect(incident,normal);
            var baseline=new DVec3(.6,.8,0);
            var detectorUp=new DVec3(-.8,.6,0);
            var center=new DVec3(targetX,0,0)+baseline*DetectorDistanceM;
            double denom=DVec3.Dot(ray,baseline);
            var reading=new OpticalReading { ReflectionPoint=point, ReflectedDirection=ray };
            if(Math.Abs(denom)<1e-12 || fraction<0 || fraction>1) return reading;
            double distance=DVec3.Dot(center-point,baseline)/denom;
            if(distance<=0) return reading;
            reading.Hit=point+ray*distance;
            reading.SpotVerticalM=DVec3.Dot(reading.Hit-center,detectorUp);
            reading.SpotHorizontalM=(reading.Hit-center).Z;
            double v=reading.SpotVerticalM, h=reading.SpotHorizontalM, edge=DetectorHalfSizeM;
            double upper=GaussianIntegral(0,edge,v), lower=GaussianIntegral(-edge,0,v);
            double horizontal=GaussianIntegral(-edge,edge,h);
            reading.TotalPower=(upper+lower)*horizontal;
            reading.Valid=reading.TotalPower>.2 && Math.Abs(v)<edge && Math.Abs(h)<edge;
            reading.VerticalSignal=reading.TotalPower>1e-12?(upper-lower)/(upper+lower):0;
            return reading;
        }
        double GaussianIntegral(double a,double b,double mean)
        {
            double scale=Math.Sqrt(2)*BeamSigmaM;
            return .5*(MathEx.Erf((b-mean)/scale)-MathEx.Erf((a-mean)/scale));
        }
    }
}
