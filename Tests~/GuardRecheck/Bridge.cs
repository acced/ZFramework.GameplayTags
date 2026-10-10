using System;
using System.Threading;

namespace EvolutionAdjacent
{
    // Shared identity used by the host and each separately named runtime+harness assembly.
    public sealed class Request
    {
        public string Id;
        public Action Action;
        public Func<long> Checksum;
        public int SuggestedIterations;
        public bool Complete;
        public long FinalChecksum;
        public int RegistryCount;
        public Exception Error;
        public Sample Result;
    }
    public sealed class Endpoint
    {
        public readonly AutoResetEvent Ready=new AutoResetEvent(false);
        public readonly AutoResetEvent Resume=new AutoResetEvent(false);
        public Request Pending;
    }
    public sealed class Sample
    {
        public string Id;
        public int Iterations;
        public double Ns, Bytes, WallMilliseconds, CpuMilliseconds;
        public int Gen0,Gen1,Gen2;
        public long WarmupChecksum,MeasuredChecksum;
    }
    public static class Bridge
    {
        [ThreadStatic] public static Endpoint Current;
        private static Request Exchange(Request request)
        {
            Current.Pending=request;
            // Publish the descriptor and park this producer. No preparation may execute
            // again until the host has finished both variants' measurements.
            WaitHandle.SignalAndWait(Current.Ready,Current.Resume);
            return request;
        }
        public static Sample Measure(string id,Action action,int iterations,Func<long> checksum)
        {
            return Exchange(new Request{Id=id,Action=action,SuggestedIterations=iterations,Checksum=checksum}).Result;
        }
        public static void Complete(long checksum,int count)
        {
            Exchange(new Request{Complete=true,FinalChecksum=checksum,RegistryCount=count});
        }
        public static void Fail(Exception error)
        {
            Current.Pending=new Request{Error=error};Current.Ready.Set();
        }
    }
}
