using System;

namespace AfmLab.Core
{
    public static class MathEx
    {
        public const double Tau = 2 * Math.PI;
        public static double Clamp(double v, double lo, double hi) { return Math.Max(lo, Math.Min(hi, v)); }
        public static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        public static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        // Abramowitz-Stegun 7.1.26, absolute error < 1.5e-7.
        public static double Erf(double x)
        {
            double sign = x < 0 ? -1 : 1;
            x = Math.Abs(x);
            double t = 1 / (1 + .3275911 * x);
            return sign * (1 - (((((1.061405429*t - 1.453152027)*t) + 1.421413741)*t - .284496736)*t + .254829592)*t*Math.Exp(-x*x));
        }
    }

    public struct DVec3
    {
        public double X, Y, Z;
        public DVec3(double x, double y, double z) { X=x; Y=y; Z=z; }
        public double Magnitude { get { return Math.Sqrt(Dot(this,this)); } }
        public DVec3 Normalized { get { double m=Magnitude; if(m<1e-20) throw new ArgumentException("Zero direction"); return this/m; } }
        public static DVec3 operator +(DVec3 a,DVec3 b) { return new DVec3(a.X+b.X,a.Y+b.Y,a.Z+b.Z); }
        public static DVec3 operator -(DVec3 a,DVec3 b) { return new DVec3(a.X-b.X,a.Y-b.Y,a.Z-b.Z); }
        public static DVec3 operator *(DVec3 a,double s) { return new DVec3(a.X*s,a.Y*s,a.Z*s); }
        public static DVec3 operator /(DVec3 a,double s) { return a*(1/s); }
        public static double Dot(DVec3 a,DVec3 b) { return a.X*b.X+a.Y*b.Y+a.Z*b.Z; }
        public static DVec3 Reflect(DVec3 incoming,DVec3 normal) { return incoming-normal*(2*Dot(incoming,normal)); }
    }
}
