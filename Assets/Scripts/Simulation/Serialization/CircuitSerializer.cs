using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LogicSim.Simulation
{
    /// <summary>Parsed circuit file.</summary>
    public sealed class CircuitDocument
    {
        public string Name { get; }
        public CircuitSnapshot Snapshot { get; }
        public IReadOnlyList<string> Warnings { get; }

        public CircuitDocument(string name, CircuitSnapshot snapshot, IReadOnlyList<string> warnings)
        {
            Name = name;
            Snapshot = snapshot;
            Warnings = warnings;
        }
    }

    /// <summary>JSON format for circuits (*.lsim.json) and clipboard fragments.</summary>
    public static class CircuitSerializer
    {
        public const string DocumentFormat = "logicsim";
        public const string FragmentFormat = "logicsim-fragment";
        public const int FormatVersion = 1;
        public const string FileExtension = ".lsim.json";

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        #region DTOs

        private sealed class DocumentDto
        {
            public string Format;
            public int Version;
            public string Name;
            public CircuitDto Circuit;
            public List<SubcircuitDto> Subcircuits;
        }

        private sealed class CircuitDto
        {
            public List<ComponentDto> Components = new List<ComponentDto>();
            public List<WireDto> Wires = new List<WireDto>();
        }

        private sealed class ComponentDto
        {
            public int Id;
            public string Type;
            public float X;
            public float Y;
            public int? Rotation;
            public string Label;
            public int? Inputs;
            public int? HalfPeriod;
            public bool? Value;
            public string Subcircuit;
            public NeuronDto Neuron;
            public NetworkDto Network;
        }

        private sealed class NeuronDto
        {
            public List<float> Weights;
            public float Threshold;
            public float? Leak;
            public float? Reset;
            public int? Refractory;
        }

        private sealed class NetworkDto
        {
            public List<int> Layers;
            public string Activation;
            public List<List<float>> Weights;
            public List<List<float>> Biases;
        }

        private sealed class WireDto
        {
            public int Id;
            public PinDto From;
            public PinDto To;
        }

        private sealed class PinDto
        {
            public int Component;
            public int Pin;
        }

        private sealed class SubcircuitDto
        {
            public string Id;
            public string Name;
            public CircuitDto Circuit;
        }

        #endregion

        #region Documents

        public static string Serialize(Circuit circuit, string name, bool indented = true)
        {
            var dto = new DocumentDto
            {
                Format = DocumentFormat,
                Version = FormatVersion,
                Name = name,
                Circuit = ToDto(circuit.Components, circuit.Wires),
                Subcircuits = circuit.Library.Definitions.Select(ToDto).ToList()
            };
            return JsonConvert.SerializeObject(dto, indented ? Formatting.Indented : Formatting.None, Settings);
        }

        /// <summary>Parses a document. Subcircuit definitions are created for <paramref name="targetLibrary"/>.</summary>
        public static CircuitDocument Deserialize(string json, SubcircuitLibrary targetLibrary)
        {
            var dto = Parse(json, DocumentFormat);
            var warnings = new List<string>();
            var definitions = (dto.Subcircuits ?? new List<SubcircuitDto>())
                .Select(s => FromDto(s, targetLibrary, warnings)).Where(d => d != null).ToList();
            var circuitDto = dto.Circuit ?? new CircuitDto();
            var components = FromDto(circuitDto.Components, warnings);
            var wires = FromDto(circuitDto.Wires);
            return new CircuitDocument(dto.Name ?? string.Empty, new CircuitSnapshot(components, wires, definitions), warnings);
        }

        #endregion

        #region Fragments

        public static string SerializeFragment(CircuitFragment fragment, SubcircuitLibrary library)
        {
            var used = new List<SubcircuitDefinition>();
            CollectDefinitions(fragment.Components, library, used);
            var dto = new DocumentDto
            {
                Format = FragmentFormat,
                Version = FormatVersion,
                Circuit = ToDto(fragment.Components, fragment.Wires),
                Subcircuits = used.Select(ToDto).ToList()
            };
            return JsonConvert.SerializeObject(dto, Formatting.None, Settings);
        }

        /// <summary>Parses a fragment and adds subcircuit definitions missing from <paramref name="targetLibrary"/>.</summary>
        public static CircuitFragment DeserializeFragment(string json, SubcircuitLibrary targetLibrary)
        {
            var dto = Parse(json, FragmentFormat);
            var warnings = new List<string>();
            foreach (var sub in dto.Subcircuits ?? new List<SubcircuitDto>())
            {
                if (targetLibrary.TryGet(sub.Id, out _))
                    continue;
                var definition = FromDto(sub, targetLibrary, warnings);
                if (definition != null)
                    targetLibrary.Add(definition);
            }
            var circuitDto = dto.Circuit ?? new CircuitDto();
            return CircuitFragment.FromElements(FromDto(circuitDto.Components, warnings), FromDto(circuitDto.Wires));
        }

        public static bool LooksLikeFragment(string text)
        {
            return !string.IsNullOrEmpty(text) && text.Contains("\"" + FragmentFormat + "\"");
        }

        #endregion

        #region Conversion

        private static DocumentDto Parse(string json, string expectedFormat)
        {
            DocumentDto dto;
            try
            {
                dto = JsonConvert.DeserializeObject<DocumentDto>(json, Settings);
            }
            catch (JsonException exception)
            {
                throw new CircuitException("Invalid circuit file: " + exception.Message);
            }
            if (dto == null)
                throw new CircuitException("Empty circuit file");
            if (!string.IsNullOrEmpty(dto.Format) && dto.Format != expectedFormat && dto.Format != DocumentFormat)
                throw new CircuitException($"Unsupported format '{dto.Format}'");
            if (dto.Version > FormatVersion)
                throw new CircuitException($"File version {dto.Version} is newer than supported version {FormatVersion}");
            return dto;
        }

        private static void CollectDefinitions(IEnumerable<CircuitComponent> components, SubcircuitLibrary library, List<SubcircuitDefinition> result)
        {
            foreach (var component in components)
            {
                if (component.Kind != ComponentKind.Subcircuit || !library.TryGet(component.SubcircuitId, out var definition))
                    continue;
                if (result.Any(d => d.Id == definition.Id))
                    continue;
                CollectDefinitions(definition.Circuit.Components, library, result);
                result.Add(definition);
            }
        }

        private static SubcircuitDto ToDto(SubcircuitDefinition definition)
        {
            return new SubcircuitDto
            {
                Id = definition.Id,
                Name = definition.Name,
                Circuit = ToDto(definition.Circuit.Components, definition.Circuit.Wires)
            };
        }

        private static SubcircuitDefinition FromDto(SubcircuitDto dto, SubcircuitLibrary library, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(dto.Id))
            {
                warnings.Add("Subcircuit without id skipped");
                return null;
            }
            var circuit = new Circuit(library);
            var circuitDto = dto.Circuit ?? new CircuitDto();
            foreach (var problem in circuit.ReplaceContents(FromDto(circuitDto.Components, warnings), FromDto(circuitDto.Wires)))
                warnings.Add($"Subcircuit '{dto.Id}': {problem}");
            return new SubcircuitDefinition(dto.Id, dto.Name, circuit);
        }

        private static CircuitDto ToDto(IEnumerable<CircuitComponent> components, IEnumerable<Wire> wires)
        {
            return new CircuitDto
            {
                Components = components.Select(ToDto).ToList(),
                Wires = wires.Select(w => new WireDto
                {
                    Id = w.Id,
                    From = new PinDto { Component = w.From.ComponentId, Pin = w.From.Pin },
                    To = new PinDto { Component = w.To.ComponentId, Pin = w.To.Pin }
                }).ToList()
            };
        }

        private static ComponentDto ToDto(CircuitComponent c)
        {
            var spec = c.Spec;
            return new ComponentDto
            {
                Id = c.Id,
                Type = c.Kind.ToString(),
                X = c.X,
                Y = c.Y,
                Rotation = c.Rotation != 0 ? c.Rotation : (int?)null,
                Label = string.IsNullOrEmpty(c.Label) ? null : c.Label,
                Inputs = spec.HasVariableInputs ? c.InputCount : (int?)null,
                HalfPeriod = c.Kind == ComponentKind.Clock ? c.ClockHalfPeriod : (int?)null,
                Value = c.Kind == ComponentKind.Switch || c.Kind == ComponentKind.Constant || c.Kind == ComponentKind.PinIn ? c.Value : (bool?)null,
                Subcircuit = c.Kind == ComponentKind.Subcircuit ? c.SubcircuitId : null,
                Neuron = c.IsNeuron && c.Neuron != null ? ToDto(c.Neuron, c.Kind == ComponentKind.SpikingNeuron) : null,
                Network = c.Kind == ComponentKind.NeuralBlock && c.Network != null ? ToDto(c.Network) : null
            };
        }

        private static NeuronDto ToDto(NeuronParameters neuron, bool spiking)
        {
            return new NeuronDto
            {
                Weights = neuron.Weights.ToList(),
                Threshold = neuron.Threshold,
                Leak = spiking ? neuron.Leak : (float?)null,
                Reset = spiking ? neuron.Reset : (float?)null,
                Refractory = spiking ? neuron.Refractory : (int?)null
            };
        }

        private static NetworkDto ToDto(NeuralNetwork network)
        {
            return new NetworkDto
            {
                Layers = network.LayerSizes.ToList(),
                Activation = network.Activation.ToString(),
                Weights = Enumerable.Range(0, network.ConnectionCount).Select(l => network.GetWeights(l).ToList()).ToList(),
                Biases = Enumerable.Range(0, network.ConnectionCount).Select(l => network.GetBiases(l).ToList()).ToList()
            };
        }

        private static NeuronParameters FromDto(NeuronDto dto, ComponentKind kind)
        {
            var defaults = NeuronParameters.Default(kind, Math.Max(1, dto.Weights?.Count ?? 1));
            return new NeuronParameters(dto.Weights != null && dto.Weights.Count > 0 ? dto.Weights : defaults.Weights,
                dto.Threshold, dto.Leak ?? defaults.Leak, dto.Reset ?? defaults.Reset, dto.Refractory ?? defaults.Refractory);
        }

        private static NeuralNetwork FromDto(NetworkDto dto)
        {
            if (!Enum.TryParse(dto.Activation ?? "Sigmoid", true, out NeuralActivation activation))
                activation = NeuralActivation.Sigmoid;
            return new NeuralNetwork(dto.Layers ?? new List<int>(), activation,
                (dto.Weights ?? new List<List<float>>()).Select(l => (IEnumerable<float>)l),
                (dto.Biases ?? new List<List<float>>()).Select(l => (IEnumerable<float>)l));
        }

        private static List<CircuitComponent> FromDto(List<ComponentDto> dtos, List<string> warnings)
        {
            var result = new List<CircuitComponent>();
            foreach (var dto in dtos ?? new List<ComponentDto>())
            {
                if (!TryParseKind(dto.Type, out var kind))
                {
                    warnings.Add($"Unknown component type '{dto.Type}' skipped");
                    continue;
                }
                var component = new CircuitComponent(kind, dto.X, dto.Y)
                {
                    Id = dto.Id,
                    Rotation = (((dto.Rotation ?? 0) % 4) + 4) % 4,
                    Label = dto.Label ?? string.Empty,
                    ClockHalfPeriod = Math.Max(1, dto.HalfPeriod ?? CircuitComponent.DefaultClockHalfPeriod),
                    Value = dto.Value ?? false,
                    SubcircuitId = dto.Subcircuit ?? string.Empty
                };
                if (dto.Inputs.HasValue)
                    component.InputCount = dto.Inputs.Value;
                try
                {
                    if (dto.Neuron != null && component.IsNeuron)
                        ComponentFactory.SetNeuron(component, FromDto(dto.Neuron, kind));
                    if (dto.Network != null && kind == ComponentKind.NeuralBlock)
                        ComponentFactory.SetNetwork(component, FromDto(dto.Network));
                }
                catch (CircuitException exception)
                {
                    warnings.Add($"Component {dto.Id}: {exception.Message}; default settings used");
                }
                component.NormalizeNeuralState();
                if (kind == ComponentKind.Subcircuit && string.IsNullOrEmpty(component.SubcircuitId))
                {
                    warnings.Add($"Subcircuit component {dto.Id} without definition skipped");
                    continue;
                }
                result.Add(component);
            }
            return result;
        }

        private static List<Wire> FromDto(List<WireDto> dtos)
        {
            return (dtos ?? new List<WireDto>())
                .Where(w => w.From != null && w.To != null)
                .Select(w => new Wire(w.Id, new PinRef(w.From.Component, w.From.Pin), new PinRef(w.To.Component, w.To.Pin)))
                .ToList();
        }

        public static bool TryParseKind(string text, out ComponentKind kind)
        {
            kind = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var normalized = text.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
            foreach (ComponentKind value in Enum.GetValues(typeof(ComponentKind)))
            {
                if (string.Equals(value.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    kind = value;
                    return true;
                }
            }
            var alias = normalized.ToLowerInvariant();
            foreach (var spec in ComponentSpec.All)
            {
                foreach (var name in spec.Aliases)
                {
                    if (name == alias)
                    {
                        kind = spec.Kind;
                        return true;
                    }
                }
            }
            return false;
        }

        #endregion
    }
}
