using System.Diagnostics;
using System.Text;
using LogicSim.Simulation;
using LogicSim.Simulation.Jobs;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace LogicSim.EditorTools
{
    /// <summary>Compares the managed backend with the Jobs (+ Burst) backend on synthetic circuits.</summary>
    public static class SimulationBenchmark
    {
        [MenuItem("Tools/LogicSim/Run Simulation Benchmark")]
        public static void Run()
        {
            var report = new StringBuilder("LogicSim benchmark (ticks per second, higher is better)\n");
            Measure(report, "Adder bank, 64 x 32 bit, settle after input change", BenchmarkCircuits.AdderBank(64, 32), 2_000, toggleInputs: true);
            Measure(report, "Random feedback circuit, 10k gates", BenchmarkCircuits.Random(1, 10_000, 32), 2_000, toggleInputs: false);
            Measure(report, "Random feedback circuit, 200k gates", BenchmarkCircuits.Random(2, 200_000, 64), 200, toggleInputs: false);
            Debug.Log(report.ToString());
        }

        private static void Measure(StringBuilder report, string title, Circuit circuit, long ticks, bool toggleInputs)
        {
            var compiled = CircuitCompiler.Compile(circuit);
            report.AppendLine($"\n{title}: {compiled.GateCount} gates, {compiled.NetCount} nets");
            report.AppendLine("  " + Time(new ManagedSimulationEngine(), compiled, ticks, toggleInputs));
            report.AppendLine("  " + Time(new JobsSimulationEngine { Mode = SimulationMode.EventDriven }, compiled, ticks, toggleInputs));
            report.AppendLine("  " + Time(new JobsSimulationEngine { Mode = SimulationMode.FullSweep }, compiled, ticks, toggleInputs));
        }

        private static string Time(ISimulationEngine engine, CompiledCircuit compiled, long ticks, bool toggleInputs)
        {
            using (engine)
            {
                engine.Load(compiled);
                engine.Run(10); // warm-up (Burst compiles asynchronously in the editor; first runs may be managed)

                var stopwatch = Stopwatch.StartNew();
                long chunk = ticks / 20;
                for (int i = 0; i < 20; i++)
                {
                    if (toggleInputs)
                        for (int slot = 0; slot < compiled.InputSlotCount; slot += 7)
                            engine.SetInput(slot, (i + slot) % 2 == 0);
                    engine.Run(chunk);
                }
                stopwatch.Stop();

                double seconds = stopwatch.Elapsed.TotalSeconds;
                var mode = engine.ActiveMode;
                return $"{engine.Name,-12} {mode,-12} {ticks / seconds,12:N0} ticks/s  {engine.TotalEvaluations / seconds / 1e6,8:N1} M gate evals/s";
            }
        }
    }
}
