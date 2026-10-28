using System;

namespace AfmLab.Core
{
    // nm, nN, nN*nm. These convenient units are converted consistently by Oscillator.
    // Positive force points UP (away from the surface). The LJ continuation is C2 in U.
    public sealed class TipSampleForce
    {
        public readonly double SigmaNm, EpsilonNnNm, CutoffNm;
        readonly double cutoffU, cutoffDerivative, cutoffCurvature;
        public TipSampleForce(double sigmaNm=5, double epsilonNnNm=3.75, double cutoffFraction=.85)
        {
            if(sigmaNm<=0 || epsilonNnNm<0 || cutoffFraction<=0 || cutoffFraction>=1)
                throw new ArgumentOutOfRangeException("Invalid force parameters");
            SigmaNm=sigmaNm; EpsilonNnNm=epsilonNnNm; CutoffNm=sigmaNm*cutoffFraction;
            double sr=SigmaNm/CutoffNm, s6=Math.Pow(sr,6), s12=s6*s6;
            cutoffU=4*EpsilonNnNm*(s12-s6);
            cutoffDerivative=-24*EpsilonNnNm/CutoffNm*(2*s12-s6);
            cutoffCurvature=24*EpsilonNnNm/(CutoffNm*CutoffNm)*(26*s12-7*s6);
        }
        public double Potential(double gapNm)
        {
            if(gapNm<CutoffNm)
            {
                double dx=gapNm-CutoffNm;
                return cutoffU+cutoffDerivative*dx+.5*cutoffCurvature*dx*dx;
            }
            double r=SigmaNm/gapNm, r2=r*r, r6=r2*r2*r2;
            return 4*EpsilonNnNm*(r6*r6-r6);
        }
        public double Force(double gapNm)
        {
            if(gapNm<CutoffNm) return -cutoffDerivative-cutoffCurvature*(gapNm-CutoffNm);
            double r=SigmaNm/gapNm,r2=r*r,r6=r2*r2*r2;
            return 24*EpsilonNnNm/gapNm*(2*r6*r6-r6);
        }
        public bool InPhysicalDomain(double gapNm) { return gapNm>=CutoffNm && MathEx.Finite(gapNm); }
    }
}
