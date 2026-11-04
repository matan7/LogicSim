using System;
using System.Linq;
using LogicSim.Simulation;
using Newtonsoft.Json.Linq;

namespace LogicSim.Mcp
{
    // Subcircuit tools: create, inspect, collapse, explode, open and close ICs.
    public sealed partial class LogicSimTools
    {
        private McpToolResult CreateSubcircuit(JObject args)
        {
            var name = (string)Required(args, "name");
            var ids = (args["components"] as JArray)?.Select(t => ResolveComponent(t).Id).ToList()
                ?? Circuit.Components.Select(c => c.Id).ToList();
            if (ids.Count == 0)
                throw new McpToolException("No components to turn into a subcircuit");
            var fragment = CircuitFragment.Capture(Circuit, ids);
            var definition = new SubcircuitDefinition(Circuit.Library.CreateUniqueId(name), name, fragment.ToCircuit(Circuit.Library));
            if (definition.InputComponentIds.Count == 0 && definition.OutputComponentIds.Count == 0)
                throw new McpToolException("A subcircuit needs at least one Switch/Button/PinIn or Bulb/PinOut to act as a pin");
            _workspace.Editor.Apply(new AddSubcircuitDefinitionEdit(Circuit.Library, definition));
            return McpToolResult.Ok(DescribeDefinition(definition));
        }

        private McpToolResult ListSubcircuits(JObject args)
        {
            return McpToolResult.Ok(new JObject { ["subcircuits"] = new JArray(Circuit.Library.Definitions.Select(DescribeDefinition)) });
        }

        private SubcircuitDefinition ResolveDefinition(JToken token)
        {
            var text = ((string)token)?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new McpToolException("Missing subcircuit id");
            var library = Circuit.Library;
            if (library.TryGet(text, out var byId))
                return byId;
            var byName = library.Definitions.FirstOrDefault(d => string.Equals(d.Name, text, StringComparison.OrdinalIgnoreCase));
            return byName ?? throw new McpToolException($"Unknown subcircuit '{text}'. Use list_subcircuits.");
        }

        private CircuitComponent ResolveInstance(JToken token)
        {
            var component = ResolveComponent(token);
            if (component.Kind != ComponentKind.Subcircuit)
                throw new McpToolException($"{component} is not a subcircuit instance");
            return component;
        }

        private McpToolResult GetSubcircuit(JObject args)
        {
            var definition = ResolveDefinition(Required(args, "subcircuit"));
            var inner = definition.Circuit;
            var result = DescribeDefinition(definition);
            result["circuit"] = new JObject
            {
                ["components"] = new JArray(inner.Components.Select(DescribeIn)),
                ["wires"] = new JArray(inner.Wires.Select(w => new JObject
                {
                    ["id"] = w.Id,
                    ["from"] = $"{w.From.ComponentId}.{inner.GetOutputName(inner.GetComponent(w.From.ComponentId), w.From.Pin)}",
                    ["to"] = $"{w.To.ComponentId}.{inner.GetInputName(inner.GetComponent(w.To.ComponentId), w.To.Pin)}"
                }))
            };
            result["used_by"] = new JArray(Circuit.Library.DirectUsersOf(definition.Id).Select(d => d.Id));
            result["instances_here"] = Circuit.InstancesOf(definition.Id).Count();
            return McpToolResult.Ok(result);
        }

        private static JObject DescribeIn(CircuitComponent component)
        {
            var entry = new JObject { ["id"] = component.Id, ["type"] = component.Kind.ToString(), ["x"] = component.X, ["y"] = component.Y };
            if (!string.IsNullOrEmpty(component.Label))
                entry["label"] = component.Label;
            if (component.Kind == ComponentKind.Subcircuit)
                entry["subcircuit"] = component.SubcircuitId;
            return entry;
        }

        private McpToolResult CollapseToSubcircuit(JObject args)
        {
            var name = (string)Required(args, "name");
            var ids = (Required(args, "components") as JArray ?? throw new McpToolException("'components' must be an array"))
                .Select(t => ResolveComponent(t).Id).ToList();
            SubcircuitRefactoring.CollapseResult result;
            try
            {
                result = SubcircuitRefactoring.Collapse(Circuit, ids, name);
            }
            catch (CircuitException exception)
            {
                throw new McpToolException(exception.Message);
            }
            _workspace.Editor.Apply(result.Edit);
            _workspace.Focus(new[] { result.Instance.Id });
            var output = DescribeDefinition(result.Definition);
            output["instance"] = Describe(Circuit.GetComponent(result.Instance.Id), null);
            if (result.KeptOutside.Count > 0)
                output["kept_outside"] = new JArray(result.KeptOutside);
            return McpToolResult.Ok(output);
        }

        private McpToolResult ExplodeSubcircuit(JObject args)
        {
            var instance = ResolveInstance(Required(args, "component"));
            var result = SubcircuitRefactoring.Explode(Circuit, instance.Id);
            _workspace.Editor.Apply(result.Edit);
            _workspace.Focus(result.Components.Select(c => c.Id));
            return McpToolResult.Ok(new JObject
            {
                ["removed_instance"] = instance.Id,
                ["components"] = new JArray(result.Components.Select(c => Describe(Circuit.GetComponent(c.Id), null)))
            });
        }

        private McpToolResult MakeSubcircuitUnique(JObject args)
        {
            var instance = ResolveInstance(Required(args, "component"));
            var edit = SubcircuitRefactoring.MakeUnique(Circuit, instance.Id, (string)args["name"], out var copy);
            _workspace.Editor.Apply(edit);
            var output = DescribeDefinition(copy);
            output["instance"] = instance.Id;
            return McpToolResult.Ok(output);
        }

        private McpToolResult RenameSubcircuit(JObject args)
        {
            var definition = ResolveDefinition(Required(args, "subcircuit"));
            _workspace.Editor.Apply(new RenameSubcircuitEdit(Circuit.Library, definition.Id, (string)Required(args, "name")));
            return McpToolResult.Ok(DescribeDefinition(Circuit.Library.Get(definition.Id)));
        }

        private McpToolResult DeleteSubcircuit(JObject args)
        {
            var definition = ResolveDefinition(Required(args, "subcircuit"));
            var library = Circuit.Library;
            var users = library.DirectUsersOf(definition.Id).Select(d => d.Name).ToList();
            bool usedInOpenLevels = Hierarchy.Levels.Any(l => l.Circuit.InstancesOf(definition.Id).Any() || l.DefinitionId == definition.Id);
            if (users.Count > 0 || usedInOpenLevels)
                throw new McpToolException($"'{definition.Name}' is still used" + (users.Count > 0 ? " by " + string.Join(", ", users) : " in the open circuit"));
            _workspace.Editor.Apply(new RemoveSubcircuitDefinitionEdit(library, definition.Id));
            return McpToolResult.Ok(new { deleted = definition.Id });
        }

        private McpToolResult OpenSubcircuit(JObject args)
        {
            try
            {
                if (args["component"] != null)
                    Hierarchy.OpenInstance(ResolveInstance(args["component"]).Id);
                else if (args["subcircuit"] != null)
                    Hierarchy.OpenDefinition(ResolveDefinition(args["subcircuit"]).Id);
                else
                    throw new McpToolException("Pass 'component' (an instance) or 'subcircuit' (a definition id)");
            }
            catch (CircuitException exception)
            {
                throw new McpToolException(exception.Message);
            }
            var definition = Circuit.Library.Get(Hierarchy.Current.DefinitionId);
            var result = DescribeDefinition(definition);
            result["level"] = DescribeLevel();
            result["hint"] = "Edit with the usual tools, then close_subcircuit to update every instance.";
            return McpToolResult.Ok(result);
        }

        private McpToolResult CloseSubcircuit(JObject args)
        {
            if (Hierarchy.IsAtRoot)
                throw new McpToolException("No subcircuit is open");
            bool apply = Bool(args, "apply", true);
            bool all = Bool(args, "all", false);
            var closed = new JArray();
            try
            {
                do
                {
                    var level = Hierarchy.Current;
                    closed.Add(new JObject
                    {
                        ["subcircuit"] = level.DefinitionId,
                        ["modified"] = Hierarchy.IsModified(level)
                    });
                    Hierarchy.Close(apply);
                }
                while (all && !Hierarchy.IsAtRoot);
            }
            catch (CircuitException exception)
            {
                throw new McpToolException(exception.Message);
            }
            return McpToolResult.Ok(new JObject { ["closed"] = closed, ["applied"] = apply, ["level"] = DescribeLevel() });
        }
    }
}
