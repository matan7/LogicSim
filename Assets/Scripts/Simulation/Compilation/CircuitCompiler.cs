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

        public static OpCode? GetOpCode(ComponentKind kind)
        {
            switch (kind)
            {
                case ComponentKind.Buffer: return OpCode.Buffer;
                case ComponentKind.Not: return OpCode.Not;
                case ComponentKind.And: return OpCode.And;
                case ComponentKind.Or: return OpCode.Or;
                case ComponentKind.Nand: return OpCode.Nand;
                case ComponentKind.Nor: return OpCode.Nor;
                case ComponentKind.Xor: return OpCode.Xor;
                case ComponentKind.Xnor: return OpCode.Xnor;
                case ComponentKind.Clock: return OpCode.Clock;
                case ComponentKind.Constant: return OpCode.Constant;
                case ComponentKind.DFlipFlop: return OpCode.DFlipFlop;
                case ComponentKind.TFlipFlop: return OpCode.TFlipFlop;
                case ComponentKind.JKFlipFlop: return OpCode.JKFlipFlop;
                default: return null;
            }
        }

        private sealed class Scope
        {
            public int Index;
            public Circuit Circuit;
            public Scope Parent;
            public int InstanceId;
            public SubcircuitDefinition Definition;
            public readonly Dictionary<int, Scope> Children = new Dictionary<int, Scope>();
            public readonly Dictionary<int, int[]> PrimitiveOutputs = new Dictionary<int, int[]>();
            public bool IsRoot => Parent == null;
        }

        private sealed class Builder
        {
            private readonly Circuit _root;
            private readonly CompiledCircuit _result = new CompiledCircuit();
            private readonly List<Scope> _scopes = new List<Scope>();
            private readonly Dictionary<(int scope, int component, int pin, bool input), int> _memo = new Dictionary<(int, int, int, bool), int>();
            private readonly HashSet<(int scope, int component, int pin, bool input)> _resolving = new HashSet<(int, int, int, bool)>();
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

            public Builder(Circuit root)
            {
                _root = root;
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
                return _result;
            }

            private Scope CreateScope(Circuit circuit, Scope parent, int instanceId, SubcircuitDefinition definition)
            {
                var scope = new Scope
                {
                    Index = _scopes.Count,
                    Circuit = circuit,
                    Parent = parent,
                    InstanceId = instanceId,
                    Definition = definition
                };
                _scopes.Add(scope);
                return scope;
            }

            private void BuildChildren(Scope scope, List<string> ancestry)
            {
                foreach (var component in scope.Circuit.Components)
                {
                    if (component.Kind != ComponentKind.Subcircuit)
                        continue;

                    if (!_root.Library.TryGet(component.SubcircuitId, out var definition))
                    {
                        Report(DiagnosticSeverity.Error, $"Subcircuit '{component.SubcircuitId}' is not defined", scope, component);
                        continue;
                    }
                    if (ancestry.Contains(definition.Id))
                    {
                        Report(DiagnosticSeverity.Error, $"Subcircuit '{definition.Id}' contains itself", scope, component);
                        continue;
                    }
                    if (ancestry.Count >= MaxNestingDepth)
                    {
                        Report(DiagnosticSeverity.Error, $"Subcircuits are nested deeper than {MaxNestingDepth} levels", scope, component);
                        continue;
                    }

                    var child = CreateScope(definition.Circuit, scope, component.Id, definition);
                    scope.Children.Add(component.Id, child);
                    ancestry.Add(definition.Id);
                    BuildChildren(child, ancestry);
                    ancestry.RemoveAt(ancestry.Count - 1);
                }
            }

            private static bool IsPrimitive(Scope scope, CircuitComponent component)
            {
                if (GetOpCode(component.Kind).HasValue)
                    return true;
                // Interface inputs are real inputs only at the top level; inside a subcircuit they alias the parent net.
                return scope.IsRoot && component.Spec.IsInterfaceInput;
            }

            private void AllocatePrimitiveNets(Scope scope)
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

            private void EmitGates(Scope scope)
            {
                var circuit = scope.Circuit;
                foreach (var component in circuit.Components)
                {
                    if (!scope.PrimitiveOutputs.TryGetValue(component.Id, out var outNets))
                        continue;

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
                    }
                    else
                    {
                        op = OpCode.Input;
                        param = _slotGates.Count;
                        _slotGates.Add(gate);
                        _initialInputs.Add(component.Kind == ComponentKind.Button ? (byte)0 : (byte)(component.Value ? 1 : 0));
                        _result.InputSlots[component.Id] = param;
                    }

                    _op.Add((byte)op);
                    _param.Add(param);
                    _inStart.Add(_inNets.Count);
                    int inputs = circuit.GetInputCount(component);
                    for (int pin = 0; pin < inputs; pin++)
                        _inNets.Add(ResolveInput(scope, component.Id, pin));
                    _out0.Add(outNets.Length > 0 ? outNets[0] : -1);
                    _out1.Add(outNets.Length > 1 ? outNets[1] : -1);
                }
            }

            private void MapRoot(Scope root)
            {
                foreach (var component in root.Circuit.Components)
                {
                    int outputs = root.Circuit.GetOutputCount(component);
                    if (outputs > 0)
                    {
                        var nets = new int[outputs];
                        for (int pin = 0; pin < outputs; pin++)
                            nets[pin] = ResolveOutput(root, component.Id, pin);
                        _result.OutputNets[component.Id] = nets;
                    }
                    int inputs = root.Circuit.GetInputCount(component);
                    if (inputs > 0)
                    {
                        var nets = new int[inputs];
                        for (int pin = 0; pin < inputs; pin++)
                            nets[pin] = ResolveInput(root, component.Id, pin);
                        _result.InputNets[component.Id] = nets;
                    }
                }
            }

            private int ResolveInput(Scope scope, int componentId, int pin)
            {
                var key = (scope.Index, componentId, pin, true);
                if (_memo.TryGetValue(key, out int cached))
                    return cached;
                var wire = scope.Circuit.GetWireInto(componentId, pin);
                int net = wire == null ? CompiledCircuit.LowNet : ResolveOutput(scope, wire.From.ComponentId, wire.From.Pin);
                _memo[key] = net;
                return net;
            }

            private int ResolveOutput(Scope scope, int componentId, int pin)
            {
                var key = (scope.Index, componentId, pin, false);
                if (_memo.TryGetValue(key, out int cached))
                    return cached;
                if (!scope.Circuit.TryGetComponent(componentId, out var component))
                    return CompiledCircuit.LowNet;
                if (!_resolving.Add(key))
                {
                    Report(DiagnosticSeverity.Warning, "Pins are connected in a loop without any gate; treated as 0", scope, component);
                    return CompiledCircuit.LowNet;
                }

                int net = CompiledCircuit.LowNet;
                if (scope.PrimitiveOutputs.TryGetValue(componentId, out var nets))
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

                _resolving.Remove(key);
                _memo[key] = net;
                return net;
            }

            private void Report(DiagnosticSeverity severity, string message, Scope scope, CircuitComponent component)
            {
                _result.Diagnostics.Add(new CompileDiagnostic(severity, message, scope.IsRoot ? component.Id : RootComponentOf(scope)));
            }

            private static int RootComponentOf(Scope scope)
            {
                while (scope.Parent != null && !scope.Parent.IsRoot)
                    scope = scope.Parent;
                return scope.InstanceId;
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
                r.SourceVersion = _root.StructureVersion;
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
}
