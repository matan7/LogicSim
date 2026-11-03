using System;
using System.Diagnostics;
using LogicSim.Simulation;

namespace LogicSim.Benchmark
{
    internal static class Program
    {
        private static void Main()
        {
            Console.WriteLine("LogicSim managed backend benchmark");
            Measure("Adder bank, 64 x 32-bit ripple adders, inputs toggled", BenchmarkCircuits.AdderBank(64, 32), 20_000, toggleInputs: true);
            Measure("Random feedback circuit, 10k gates", BenchmarkCircuits.Random(1, 10_000, 32), 2_000, toggleInputs: false);
            Measure("Random feedback circuit, 200k gates", BenchmarkCircuits.Random(2, 200_000, 64), 200, toggleInputs: false);
        }

        private static void Measure(string title, Circuit circuit, long ticks, bool toggleInputs)
        {
            var stopwatch = Stopwatch.StartNew();
            var compiled = CircuitCompiler.Compile(circuit);
            double compileMs = stopwatch.Elapsed.TotalMilliseconds;
            Console.WriteLine($"\n{title}: {compiled.GateCount:N0} gates, {compiled.NetCount:N0} nets (compiled in {compileMs:N0} ms)");
            foreach (var mode in new[] { SimulationMode.EventDriven, SimulationMode.FullSweep })
            {
                using var engine = new ManagedSimulationEngine { Mode = mode };
                engine.Load(compiled);
                engine.Run(10);
                stopwatch.Restart();
                long chunk = Math.Max(1, ticks / 20);
                for (int i = 0; i < 20; i++)
                {
                    if (toggleInputs)
                        for (int slot = 0; slot < compiled.InputSlotCount; slot += 7)
                            engine.SetInput(slot, (i + slot) % 2 == 0);
                    engine.Run(chunk);
                }
                double seconds = stopwatch.Elapsed.TotalSeconds;
                Console.WriteLine($"  {mode,-12} {chunk * 20 / seconds,14:N0} ticks/s  {engine.LastRunEvaluations * 20 / seconds / 1e6,8:N1} M gate evals/s");
            }
        }
    }
}
