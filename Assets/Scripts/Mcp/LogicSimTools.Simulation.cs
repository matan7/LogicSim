using System;
using System.Linq;
using LogicSim.Simulation;
using Newtonsoft.Json.Linq;

namespace LogicSim.Mcp
{
    // Simulation tools: inputs, run, read signals, status, truth table.
    public sealed partial class LogicSimTools
    {
        private McpToolResult SetInputs(JObject args)
        {
            if (!(args["values"] is JObject values) || !values.HasValues)
                throw new McpToolException("'values' must map inputs to 0/1");
            var applied = new JObject();
            foreach (var property in values.Properties())
            {
                // Inputs belong to the document, also while an IC is open.
                var component = ResolveComponent(Root, property.Name);
                if (!component.Spec.IsInterfaceInput)
                    throw new McpToolException($"{component} is not a Switch, Button or PinIn");
                bool value = ParseBool(property.Value);
                Root.SetInputValue(component.Id, value);
                applied[NameOf(component)] = value ? 1 : 0;
            }
            return McpToolResult.Ok(new JObject { ["inputs"] = applied, ["hint"] = "Call run_simulation to propagate the change." });
        }

        private McpToolResult RunSimulation(JObject args)
        {
            var session = _workspace.Session;
            // The simulation runs the document; pending IC edits are written back first.
            int applied = Hierarchy.ApplyAll();
            session.EnsureCompiled();
            long startTick = session.Tick;
            bool stable;
            if (args["ticks"] != null && !Bool(args, "settle", false))
            {
                long ticks = Math.Max(0, (long)args["ticks"]);
                session.Run(ticks);
                stable = session.Engine.IsStable;
            }
            else
            {
                long maxTicks = args["max_ticks"] != null ? Math.Max(1, (long)args["max_ticks"]) : 100_000;
                stable = session.Settle(maxTicks);
            }

            var result = SignalSummary(Root, session);
            if (applied > 0)
                result["applied_ic_edits"] = applied;
            result["ticks_elapsed"] = session.Tick - startTick;
            result["tick"] = session.Tick;
            result["stable"] = stable;
            if (!stable)
                result["note"] = "Signals still change (oscillation, running clock or very deep logic).";
            AddDiagnostics(result);
            return McpToolResult.Ok(result);
        }

        private McpToolResult ReadSignals(JObject args)
        {
            var signals = CurrentSignals(out var reason);
            if (signals == null)
                throw new McpToolException(reason);
            if (args["pins"] is JArray pins && pins.Count > 0)
            {
                var values = new JObject();
                foreach (var pinToken in pins)
                {
                    var text = (string)pinToken;
                    var (component, pin, output) = ParseAnyPin(text);
                    bool value = output ? signals.GetOutputValue(component.Id, pin) : signals.GetInputPinValue(component.Id, pin);
                    values[text] = value ? 1 : 0;
                }
                return McpToolResult.Ok(new JObject { ["pins"] = values, ["tick"] = _workspace.Session.Tick });
            }
            var summary = SignalSummary(Circuit, signals);
            summary["tick"] = _workspace.Session.Tick;
            if (!Hierarchy.IsAtRoot)
                summary["level"] = DescribeLevel();
            return McpToolResult.Ok(summary);
        }

        private McpToolResult ResetSimulation(JObject args)
        {
            _workspace.Session.Reset();
            return McpToolResult.Ok(new { tick = _workspace.Session.Tick });
        }

        private McpToolResult GetStatus(JObject args)
        {
            var session = _workspace.Session;
            session.EnsureCompiled();
            var compiled = session.Compiled;
            var result = new JObject
            {
                ["host"] = _workspace.HostName,
                ["backend"] = session.Engine.Name,
                ["mode"] = session.Engine.ActiveMode.ToString(),
                ["running"] = _workspace.IsRunning,
                ["ticks_per_second"] = _workspace.TicksPerSecond,
                ["tick"] = session.Tick,
                ["stable"] = session.Engine.IsStable,
                ["gates"] = compiled.GateCount,
                ["nets"] = compiled.NetCount,
                ["gate_evaluations"] = session.Engine.TotalEvaluations
            };
            AddDiagnostics(result);
            return McpToolResult.Ok(result);
        }

        private McpToolResult SetRunning(JObject args)
        {
            if (args["ticks_per_second"] != null)
                _workspace.TicksPerSecond = Math.Max(1, (double)args["ticks_per_second"]);
            if (args["running"] != null)
                _workspace.IsRunning = ParseBool(args["running"]);
            return McpToolResult.Ok(new { running = _workspace.IsRunning, ticks_per_second = _workspace.TicksPerSecond });
        }

        private McpToolResult TruthTableTool(JObject args)
        {
            var inputs = (args["inputs"] as JArray)?.Select(ResolveComponent).ToList();
            var outputs = (args["outputs"] as JArray)?.Select(ResolveComponent).ToList();
            long maxTicks = args["max_ticks_per_row"] != null ? (long)args["max_ticks_per_row"] : TruthTableGenerator.DefaultMaxTicksPerRow;
            var table = TruthTableGenerator.Generate(Circuit, inputs, outputs, maxTicks);
            var rows = new JArray();
            foreach (var row in table.Rows)
            {
                var entry = new JObject();
                for (int i = 0; i < row.Inputs.Length; i++)
                    entry[table.InputNames[i]] = row.Inputs[i] ? 1 : 0;
                for (int i = 0; i < row.Outputs.Length; i++)
                    entry[table.OutputNames[i]] = row.Outputs[i] ? 1 : 0;
                if (!row.Stable)
                    entry["stable"] = false;
                rows.Add(entry);
            }
            return McpToolResult.Ok(new JObject
            {
                ["inputs"] = new JArray(table.InputNames),
                ["outputs"] = new JArray(table.OutputNames),
                ["rows"] = rows,
                ["text"] = table.ToText()
            });
        }
    }
}
