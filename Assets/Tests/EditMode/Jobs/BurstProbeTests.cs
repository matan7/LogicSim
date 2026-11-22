#if LOGICSIM_BURST
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace LogicSim.Tests.Jobs
{
    /// <summary>
    /// Makes sure jobs really run Burst-compiled in this editor, so the parity tests compare Burst with managed
    /// code and not managed jobs with managed code.
    /// </summary>
    public class BurstProbeTests
    {
        // Same attribute as the simulation jobs.
        [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Strict)]
        private struct ProbeJob : IJob
        {
            public NativeArray<int> Result;

            public void Execute()
            {
                Result[0] = 1;
                MarkManaged(Result);
            }

            // Burst removes calls to [BurstDiscard] methods.
            [BurstDiscard]
            private static void MarkManaged(NativeArray<int> result) => result[0] = 2;
        }

        [Test]
        public void Jobs_RunBurstCompiled()
        {
            var result = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                new ProbeJob { Result = result }.Schedule().Complete();
                Assert.That(result[0], Is.EqualTo(1),
                    "The job ran as managed code. Turn on Jobs > Burst > Enable Compilation.");
            }
            finally
            {
                result.Dispose();
            }
        }
    }
}
#endif
