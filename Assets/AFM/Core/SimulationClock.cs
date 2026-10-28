using System;

namespace AfmLab.Core
{
    // A bounded work budget postpones integration; it never changes the simulation step or drops debt.
    public sealed class SimulationClock
    {
        public double PendingSeconds { get; private set; }
        public long CompletedCycles { get; private set; }
        public void Reset() { PendingSeconds=0; CompletedCycles=0; }
        public int Advance(double frameSeconds,double speed,double cycleSeconds,int maxCycles,Action step)
        {
            if(frameSeconds<0 || speed<0 || cycleSeconds<=0 || maxCycles<0) throw new ArgumentOutOfRangeException();
            PendingSeconds+=frameSeconds*speed;
            int n=0;
            while(PendingSeconds+1e-14>=cycleSeconds && n<maxCycles)
            {
                step(); PendingSeconds-=cycleSeconds; n++; CompletedCycles++;
            }
            if(PendingSeconds<0)PendingSeconds=0;
            return n;
        }
    }
}
