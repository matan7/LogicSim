using System.Collections.Generic;
using System.Linq;
using LogicSim.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace LogicSim.Tests.App
{
    /// <summary>The bundled 8-bit computer (Tools/generate_8bit_computer.py) runs its Fibonacci program.</summary>
    public class ExampleComputerTests
    {
        [Test]
        public void TheEightBitComputer_PrintsTheFibonacciNumbers()
        {
            var asset = Resources.Load<TextAsset>("Examples/8-bit-computer");
            Assert.That(asset, Is.Not.Null);
            var library = new SubcircuitLibrary();
            var document = CircuitSerializer.Deserialize(asset.text, library);
            Assert.That(document.Warnings, Is.Empty);
            var circuit = new Circuit(library);
            document.Snapshot.Restore(circuit);

            var cpu = circuit.Components.Single(c => c.Label == "CPU");
            int Pin(string name) => Enumerable.Range(0, circuit.GetOutputCount(cpu)).Single(p => circuit.GetOutputName(cpu, p) == name);
            var outputs = Enumerable.Range(0, 8).Select(i => Pin("OUT" + i)).ToArray();
            int halt = Pin("HLT");
            int half = circuit.Components.Single(c => c.Kind == ComponentKind.Clock).ClockHalfPeriod;

            using var session = new SimulationSession(circuit, new ManagedSimulationEngine());
            session.Reset();
            int Output() => outputs.Select((pin, bit) => session.GetOutputValue(cpu.Id, pin) ? 1 << bit : 0).Sum();

            // Five clock cycles per instruction. The first OUT is the fifth instruction; after that the loop prints
            // every nine instructions. Sample right after each OUT.
            session.Run(half + 2 * half * 23);
            var printed = new List<int> { Output() };
            for (int i = 0; i < 14; i++)
            {
                session.Run(2 * half * 45);
                printed.Add(Output());
            }

            Assert.That(printed, Is.EqualTo(new[] { 0, 1, 1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233, 121 }), "8-bit Fibonacci");
            Assert.That(session.GetOutputValue(cpu.Id, halt), Is.False, "the program loops without halting");
        }
    }
}
