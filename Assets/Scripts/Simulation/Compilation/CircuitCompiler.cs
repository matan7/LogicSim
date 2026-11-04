using System;
using System.Collections.Generic;

namespace LogicSim.Simulation
{
    /// <summary>
    /// Turns a <see cref="Circuit"/> (with nested subcircuits) into a flat <see cref="CompiledCircuit"/>.
    /// Subcircuit boundaries are resolved as net aliases, so they add no gate delay.
    /// </summary>
    public static class CircuitCompiler
    {
        public const int MaxNestingDepth = 32;

        public static CompiledCircuit Compile(Circuit circuit)
        {
            if (circuit == null)
                throw new ArgumentNullException(nameof(circuit));
            return new Builder(circuit).Build();
        }

        public static OpCode? GetOpCode(ComponentKind kind) => ComponentSpec.Get(kind).SimulationOp;

        private sealed class Builder
        {
            private readonly Circuit _root;
            private readonly CompiledCircuit _result = new CompiledCircuit();
            private readonly List<CompiledScope> _scopes = new List<CompiledScope>();
            private readonly NetResolver _resolver;
            private int _nextNet = 1;

            private readonly List<byte> _op = new List<byte>();
            private readonly List<int> _param = new List<int>();
            private readonly List<int> _inStart = new List<int>();
            private readonly List<int> _inNets = new List<int>();
            private readonly List<int> _out0 = new List<int>();
            private readonly List<int> _out1 = new List<int>();
            private readonly List<int> _clockGates = new List<int>();
            private readonly List<int> _slotGates = new List<int>();
            private readonly List<byte> _initialInputs = new List<byte>();
            private readonly List<float> _weights = new List<float>();
            private readonly List<float> _neuronParams = new List<float>();
            private readonly List<ulong> _lut = new List<ulong>();
            private readonly Dictionary<NeuralNetwork, int> _lutOffsets = new Dictionary<NeuralNetwork, int>();
            private int _neuronCount;

            public Builder(Circuit root)
            {
                _root = root;
                _resolver = new NetResolver(_result, compiling: true);
            }

            public CompiledCircuit Build()
            {
                var rootScope = CreateScope(_root, null, 0, null);
                BuildChildren(rootScope, new List<string>());

                foreach (var scope in _scopes)
                    AllocatePrimitiveNets(scope);

                foreach (var scope in _scopes)
                    EmitGates(scope);

                MapRoot(rootScope);
                Finish();

                // Nets inside ICs are resolved again on demand (probing); drop the compile-time caches.
                foreach (var scope in _scopes)
                    scope.Memo = null;
                rootScope.PrimitiveOutputs = null;
                _result.RootScope = rootScope;
                return _result;
            }

            private CompiledScope CreateScope(Circuit circuit, CompiledScope parent, int instanceId, SubcircuitDefinition definition)
            {
                var scope = new CompiledScope(_scopes.Count, circuit, parent, instanceId, definition);
                _scopes.Add(scope);
                return scope;
            }

            private void BuildChildren(CompiledScope scope, List<string> ancestry)
            {
                foreach (var component in scope.Circuit.Components)
                {
                    if (component.Kind != ComponentKind.Subcircuit)
                        continue;

                    if (!_root.Library.TryGet(component.SubcircuitId, out var definition))
                    {
                        _resolver.Report(DiagnosticSeverity.Error, $"Subcircuit '{component.SubcircuitId}' is not defined", scope, component);
                        continue;
                    }
                    if (ancestry.Contains(definition.Id))
                    {
                        _resolver.Report(DiagnosticSeverity.Error, $"Subcircuit '{definition.Id}' contains itself", scope, component);
                        continue;
                    }
                    if (ancestry.Count >= MaxNestingDepth)
                    {
                        _resolver.Report(DiagnosticSeverity.Error, $"Subcircuits are nested deeper than {MaxNestingDepth} levels", scope, component);
                        continue;
                    }

                    var child = CreateScope(definition.Circuit, scope, component.Id, definition);
                    scope.Children.Add(component.Id, child);
                    ancestry.Add(definition.Id);
                    BuildChildren(child, ancestry);
                    ancestry.RemoveAt(ancestry.Count - 1);
                }
            }

            private static bool IsPrimitive(CompiledScope scope, CircuitComponent component)
            {
                if (GetOpCode(component.Kind).HasValue || component.Kind == ComponentKind.NeuralBlock)
                    return true;
                // Interface inputs are real inputs only at the top level; inside a subcircuit they alias the parent net.
                return scope.IsRoot && component.Spec.IsInterfaceInput;
            }

            private void AllocatePrimitiveNets(CompiledScope scope)
            {
                foreach (var component in scope.Circuit.Components)
                {
                    if (!IsPrimitive(scope, component))
                        continue;
                    int outputs = scope.Circuit.GetOutputCount(component);
                    var nets = new int[outputs];
                    for (int i = 0; i < outputs; i++)
                        nets[i] = _nextNet++;
                    scope.PrimitiveOutputs.Add(component.Id, nets);
                }
            }

            private void EmitGates(CompiledScope scope)
            {
                var circuit = scope.Circuit;
                foreach (var component in circuit.Components)
                {
                    if (!scope.PrimitiveOutputs.TryGetValue(component.Id, out var outNets))
                        continue;
                    if (component.Kind == ComponentKind.NeuralBlock)
                    {
                        EmitNeuralBlock(scope, component, outNets);
                        continue;
                    }

                    int gate = _op.Count;
                    OpCode op;
                    int param = 0;
                    var opCode = GetOpCode(component.Kind);
                    if (opCode.HasValue)
                    {
                        op = opCode.Value;
                        if (op == OpCode.Clock)
                        {
                            param = Math.Max(1, component.ClockHalfPeriod);
                            _clockGates.Add(gate);
                        }
                        else if (op == OpCode.Constant)
                        {
                            param = component.Value ? 1 : 0;
                        }
                        else if (op == OpCode.Perceptron || op == OpCode.Spiking)
                        {
                            param = AddNeuron(component);
                            scope.Neurons[component.Id] = param;
                        }
                    }
                    else
                    {
                        op = OpCode.Input;
                        param = _slotGates.Count;
                        _slotGates.Add(gate);
                        _initialInputs.Add((byte)(component.Value ? 1 : 0));
                        _result.InputSlots[component.Id] = param;
                    }

                    _op.Add((byte)op);
                    _param.Add(param);
                    _inStart.Add(_inNets.Count);
                    int inputs = circuit.GetInputCount(component);
                    if (op == OpCode.Perceptron || op == OpCode.Spiking)
                        PadWeights();
                    for (int pin = 0; pin < inputs; pin++)
                    {
                        _inNets.Add(_resolver.ResolveInput(scope, component.Id, pin));
                        if (op == OpCode.Perceptron || op == OpCode.Spiking)
                            _weights.Add(pin < component.Neuron.Weights.Count ? component.Neuron.Weights[pin] : 0f);
                    }
                    _out0.Add(outNets.Length > 0 ? outNets[0] : -1);
                    _out1.Add(outNets.Length > 1 ? outNets[1] : -1);
                }
            }

            private int AddNeuron(CircuitComponent component)
            {
                var neuron = component.Neuron ?? NeuronParameters.Default(component.Kind, component.InputCount);
                _neuronParams.Add(neuron.Threshold);
                _neuronParams.Add(neuron.Leak);
                _neuronParams.Add(neuron.Reset);
                _neuronParams.Add(neuron.Refractory);
                return _neuronCount++;
            }

            /// <summary>Weights are stored parallel to the input nets once the first neuron appears.</summary>
            private void PadWeights()
            {
                while (_weights.Count < _inNets.Count)
                    _weights.Add(0f);
            }

            /// <summary>A neural block becomes one lookup gate per output; equal networks share their tables.</summary>
            private void EmitNeuralBlock(CompiledScope scope, CircuitComponent component, int[] outNets)
            {
                var network = component.Network;
                if (!_lutOffsets.TryGetValue(network, out int offset))
                {
                    offset = _lut.Count;
                    _lut.AddRange(network.GetLut());
                    _lutOffsets.Add(network, offset);
                }
                int inputs = scope.Circuit.GetInputCount(component);
                var inputNets = new int[inputs];
                for (int pin = 0; pin < inputs; pin++)
                    inputNets[pin] = _resolver.ResolveInput(scope, component.Id, pin);
                int words = network.LutWordsPerOutput;
                for (int m = 0; m < outNets.Length; m++)
                {
                    _op.Add((byte)OpCode.Lut);
                    _param.Add(offset + m * words);
                    _inStart.Add(_inNets.Count);
                    _inNets.AddRange(inputNets);
                    _out0.Add(outNets[m]);
                    _out1.Add(-1);
                }
            }

            private void MapRoot(CompiledScope root)
            {
                foreach (var component in root.Circuit.Components)
                {
                    int outputs = root.Circuit.GetOutputCount(component);
                    if (outputs > 0)
                    {
                        var nets = new int[outputs];
                        for (int pin = 0; pin < outputs; pin++)
                            nets[pin] = _resolver.ResolveOutput(root, component.Id, pin);
                        _result.OutputNets[component.Id] = nets;
                    }
                    int inputs = root.Circuit.GetInputCount(component);
                    if (inputs > 0)
                    {
                        var nets = new int[inputs];
                        for (int pin = 0; pin < inputs; pin++)
                            nets[pin] = _resolver.ResolveInput(root, component.Id, pin);
                        _result.InputNets[component.Id] = nets;
                    }
                }
            }

            private void Finish()
            {
                var r = _result;
                r.GateCount = _op.Count;
                r.NetCount = _nextNet;
                r.Op = _op.ToArray();
                r.Param = _param.ToArray();
                _inStart.Add(_inNets.Count);
                r.InStart = _inStart.ToArray();
                r.InNets = _inNets.ToArray();
                r.Out0 = _out0.ToArray();
                r.Out1 = _out1.ToArray();
                r.ClockGates = _clockGates.ToArray();
                r.InputSlotCount = _slotGates.Count;
                r.InputSlotGate = _slotGates.ToArray();
                r.InitialInputs = _initialInputs.ToArray();
                if (_neuronCount > 0)
                {
                    PadWeights();
                    r.Weights = _weights.ToArray();
                    r.NeuronParams = _neuronParams.ToArray();
                }
                else
                {
                    r.Weights = new float[1];
                    r.NeuronParams = new float[4];
                }
                r.NeuronCount = _neuronCount;
                r.Lut = _lut.Count > 0 ? _lut.ToArray() : new ulong[1];
                r.SourceVersion = _root.StructureVersion;
                r.LibraryVersion = _root.Library.Version;
                BuildFanout(r);
            }

            private static void BuildFanout(CompiledCircuit r)
            {
                var counts = new int[r.NetCount + 1];
                var lastGate = new int[r.NetCount];
                for (int i = 0; i < lastGate.Length; i++)
                    lastGate[i] = -1;

                for (int g = 0; g < r.GateCount; g++)
                {
                    for (int i = r.InStart[g]; i < r.InStart[g + 1]; i++)
                    {
                        int net = r.InNets[i];
                        if (net == CompiledCircuit.LowNet || lastGate[net] == g)
                            continue;
                        lastGate[net] = g;
                        counts[net + 1]++;
                    }
                }

                for (int n = 0; n < r.NetCount; n++)
                    counts[n + 1] += counts[n];

                var fanout = new int[counts[r.NetCount]];
                var cursor = new int[r.NetCount];
                Array.Copy(counts, cursor, r.NetCount);
                for (int i = 0; i < lastGate.Length; i++)
                    lastGate[i] = -1;

                for (int g = 0; g < r.GateCount; g++)
                {
                    for (int i = r.InStart[g]; i < r.InStart[g + 1]; i++)
                    {
                        int net = r.InNets[i];
                        if (net == CompiledCircuit.LowNet || lastGate[net] == g)
                            continue;
                        lastGate[net] = g;
                        fanout[cursor[net]++] = g;
                    }
                }

                r.FanoutStart = counts;
                r.FanoutGates = fanout;
            }
        }
    }

    /// <summary>
    /// Follows wires through subcircuit boundaries to the gate output that drives a pin. Used while compiling and,
    /// afterwards, to find the nets inside a particular IC instance (probing).
    /// </summary>
    internal sealed class NetResolver
    {
        private readonly CompiledCircuit _result;
        private readonly bool _compiling;
        private readonly HashSet<(CompiledScope, int, int, bool)> _resolving = new HashSet<(CompiledScope, int, int, bool)>();

        public NetResolver(CompiledCircuit result, bool compiling)
        {
            _result = result;
            _compiling = compiling;
        }

        public int ResolveInput(CompiledScope scope, int componentId, int pin)
        {
            // After compilation the document level is answered from the maps built for it.
            if (!_compiling && scope.IsRoot)
                return _result.GetInputNet(componentId, pin);
            var memo = scope.Memo ?? (scope.Memo = new Dictionary<(int, int, bool), int>());
            var key = (componentId, pin, true);
            if (memo.TryGetValue(key, out int cached))
                return cached;
            var wire = scope.Circuit.GetWireInto(componentId, pin);
            int net = wire == null ? CompiledCircuit.LowNet : ResolveOutput(scope, wire.From.ComponentId, wire.From.Pin);
            memo[key] = net;
            return net;
        }

        public int ResolveOutput(CompiledScope scope, int componentId, int pin)
        {
            if (!_compiling && scope.IsRoot)
                return _result.GetOutputNet(componentId, pin);
            var memo = scope.Memo ?? (scope.Memo = new Dictionary<(int, int, bool), int>());
            var key = (componentId, pin, false);
            if (memo.TryGetValue(key, out int cached))
                return cached;
            if (!scope.Circuit.TryGetComponent(componentId, out var component))
                return CompiledCircuit.LowNet;
            var loopKey = (scope, componentId, pin, false);
            if (!_resolving.Add(loopKey))
            {
                Report(DiagnosticSeverity.Warning, "Pins are connected in a loop without any gate; treated as 0", scope, component);
                return CompiledCircuit.LowNet;
            }

            int net = CompiledCircuit.LowNet;
            if (scope.PrimitiveOutputs != null && scope.PrimitiveOutputs.TryGetValue(componentId, out var nets))
            {
                if (pin >= 0 && pin < nets.Length)
                    net = nets[pin];
            }
            else if (component.Spec.IsInterfaceInput && !scope.IsRoot)
            {
                int index = scope.Definition.IndexOfInputComponent(componentId);
                if (index >= 0)
                    net = ResolveInput(scope.Parent, scope.InstanceId, index);
            }
            else if (component.Kind == ComponentKind.Subcircuit && scope.Children.TryGetValue(componentId, out var child))
            {
                var outputs = child.Definition.OutputComponentIds;
                if (pin >= 0 && pin < outputs.Count)
                    net = ResolveInput(child, outputs[pin], 0);
            }

            _resolving.Remove(loopKey);
            memo[key] = net;
            return net;
        }

        public void Report(DiagnosticSeverity severity, string message, CompiledScope scope, CircuitComponent component)
        {
            if (!_compiling)
                return;
            _result.Diagnostics.Add(new CompileDiagnostic(severity, message, scope.IsRoot ? component.Id : RootComponentOf(scope)));
        }

        private static int RootComponentOf(CompiledScope scope)
        {
            while (scope.Parent != null && !scope.Parent.IsRoot)
                scope = scope.Parent;
            return scope.InstanceId;
        }
    }
}
