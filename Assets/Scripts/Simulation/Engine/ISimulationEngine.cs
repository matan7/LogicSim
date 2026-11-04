using System;

namespace LogicSim.Simulation
{
    public enum SimulationMode
    {
        /// <summary>Engine picks event-driven or full sweep based on circuit size and activity.</summary>
        Auto,

        /// <summary>Only gates whose inputs changed are evaluated. Best for typical circuits.</summary>
        EventDriven,

        /// <summary>All gates are evaluated every tick. Parallelizes well for very large, very active circuits.</summary>
        FullSweep
    }

    /// <summary>A backend that executes a <see cref="CompiledCircuit"/>.</summary>
    public interface ISimulationEngine : IDisposable
    {
        string Name { get; }

        SimulationMode Mode { get; set; }

        /// <summary>Mode used by the last run (resolves <see cref="SimulationMode.Auto"/>).</summary>
        SimulationMode ActiveMode { get; }

        CompiledCircuit Circuit { get; }

        long Tick { get; }

        /// <summary>No gate is waiting for evaluation (clocks may still produce future edges).</summary>
        bool IsStable { get; }

        long TotalEvaluations { get; }

        /// <summary>True between <see cref="Schedule"/> and <see cref="Complete"/> for asynchronous backends.</summary>
        bool IsRunning { get; }

        void Load(CompiledCircuit circuit);

        /// <summary>Clears all nets and gate state and restores the initial inputs.</summary>
        void Reset();

        /// <summary>Sets an input slot. Takes effect at the next tick.</summary>
        void SetInput(int slot, bool value);

        bool GetInput(int slot);

        bool GetNet(int net);

        /// <summary>Membrane potential of a spiking neuron (index from the compiled circuit).</summary>
        float GetNeuronPotential(int neuron);

        /// <summary>Starts simulating <paramref name="ticks"/> ticks. May run asynchronously.</summary>
        void Schedule(long ticks);

        /// <summary>Waits for the work started by <see cref="Schedule"/>.</summary>
        void Complete();

        /// <summary>Runs until no gate is pending or <paramref name="maxTicks"/> elapsed. Synchronous.</summary>
        /// <returns>True when the circuit settled.</returns>
        bool Settle(long maxTicks);
    }

    public static class SimulationEngineExtensions
    {
        public static void Run(this ISimulationEngine engine, long ticks)
        {
            engine.Schedule(ticks);
            engine.Complete();
        }
    }
}
