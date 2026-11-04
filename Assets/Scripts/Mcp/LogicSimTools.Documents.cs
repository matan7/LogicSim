using LogicSim.Simulation;
using Newtonsoft.Json.Linq;

namespace LogicSim.Mcp
{
    // Saving, loading and importing documents.
    public sealed partial class LogicSimTools
    {
        private McpToolResult SaveCircuit(JObject args)
        {
            var name = (string)args["name"];
            if (string.IsNullOrWhiteSpace(name))
                name = _workspace.DocumentName;
            Hierarchy.ApplyAll();
            var path = _workspace.Documents.Save(Root, name);
            _workspace.DocumentName = name;
            return McpToolResult.Ok(new { name, path });
        }

        private McpToolResult LoadCircuit(JObject args)
        {
            var name = (string)Required(args, "name");
            var json = _workspace.Documents.Read(name);
            Hierarchy.CloseTo(0, apply: true);
            var document = CircuitSerializer.Deserialize(json, Circuit.Library);
            _workspace.Editor.Apply(new ReplaceCircuitEdit(Circuit, document.Snapshot, "Load circuit"));
            _workspace.DocumentName = string.IsNullOrEmpty(document.Name) ? name : document.Name;
            return McpToolResult.Ok(new { name = _workspace.DocumentName, components = Circuit.ComponentCount, wires = Circuit.WireCount, warnings = document.Warnings });
        }

        private McpToolResult ListSaved(JObject args)
        {
            return McpToolResult.Ok(new { directory = _workspace.Documents.Directory, circuits = _workspace.Documents.List() });
        }

        private McpToolResult ImportJson(JObject args)
        {
            var json = (string)Required(args, "json");
            Hierarchy.CloseTo(0, apply: true);
            var document = CircuitSerializer.Deserialize(json, Circuit.Library);
            _workspace.Editor.Apply(new ReplaceCircuitEdit(Circuit, document.Snapshot, "Import circuit"));
            if (!string.IsNullOrEmpty(document.Name))
                _workspace.DocumentName = document.Name;
            return McpToolResult.Ok(new { name = _workspace.DocumentName, components = Circuit.ComponentCount, wires = Circuit.WireCount, warnings = document.Warnings });
        }
    }
}
