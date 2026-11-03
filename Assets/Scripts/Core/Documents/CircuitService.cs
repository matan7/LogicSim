using System;
using System.IO;
using Core.Command;
using LogicSim.Mcp;
using LogicSim.Simulation;
using UnityEngine;

namespace Core.Documents
{
    /// <summary>Owns the open circuit document: model, undoable editing, file name and saving.</summary>
    public class CircuitService
    {
        public const string DefaultName = "Untitled";

        public Circuit Circuit { get; }
        public ICircuitEditor Editor { get; }
        public CircuitDocumentStore Documents { get; }
        public string DocumentName { get; private set; } = DefaultName;
        public bool IsDirty { get; private set; }

        /// <summary>Name or dirty flag changed.</summary>
        public event Action DocumentChanged;

        /// <summary>A whole document was opened (views may want to frame it).</summary>
        public event Action DocumentOpened;

        public CircuitService(CommandService commandService)
        {
            Circuit = new Circuit();
            Editor = new CommandCircuitEditor(Circuit, commandService);
            Documents = new CircuitDocumentStore(Path.Combine(Application.persistentDataPath, "Circuits"));

            Circuit.StructureChanged += MarkDirty;
            Circuit.ComponentMoved += _ => MarkDirty();
            Circuit.ComponentPropertiesChanged += _ => MarkDirty();
        }

        public bool HasName => DocumentName != DefaultName;

        public void SetDocumentName(string name)
        {
            DocumentName = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
            DocumentChanged?.Invoke();
        }

        public void New()
        {
            Editor.Apply(new ReplaceCircuitEdit(Circuit, CircuitSnapshot.Empty(), "New circuit"));
            SetDocumentName(DefaultName);
            IsDirty = false;
            DocumentChanged?.Invoke();
            DocumentOpened?.Invoke();
        }

        public string Save(string name = null)
        {
            if (!string.IsNullOrWhiteSpace(name))
                DocumentName = name.Trim();
            var path = Documents.Save(Circuit, DocumentName);
            IsDirty = false;
            DocumentChanged?.Invoke();
            return path;
        }

        public void Open(string name) => OpenJson(Documents.Read(name), name);

        /// <summary>Replaces the circuit with a document (undoable).</summary>
        public CircuitDocument OpenJson(string json, string fallbackName)
        {
            var document = CircuitSerializer.Deserialize(json, Circuit.Library);
            Editor.Apply(new ReplaceCircuitEdit(Circuit, document.Snapshot, "Open circuit"));
            foreach (var warning in document.Warnings)
                Debug.LogWarning("[LogicSim] " + warning);
            DocumentName = string.IsNullOrWhiteSpace(document.Name) ? fallbackName : document.Name;
            IsDirty = false;
            DocumentChanged?.Invoke();
            DocumentOpened?.Invoke();
            return document;
        }

        private void MarkDirty()
        {
            if (IsDirty)
                return;
            IsDirty = true;
            DocumentChanged?.Invoke();
        }
    }

    /// <summary>Routes model edits through the app's undo/redo stack.</summary>
    public sealed class CommandCircuitEditor : ICircuitEditor
    {
        private readonly CommandService _commandService;

        public Circuit Circuit { get; }

        public CommandCircuitEditor(Circuit circuit, CommandService commandService)
        {
            Circuit = circuit;
            _commandService = commandService;
        }

        public void Apply(ICircuitEdit edit) => _commandService.ExecuteCommand(new CircuitEditCommand(edit));
    }

    public sealed class CircuitEditCommand : ICommand
    {
        public ICircuitEdit Edit { get; }

        public CircuitEditCommand(ICircuitEdit edit)
        {
            Edit = edit ?? throw new ArgumentNullException(nameof(edit));
        }

        public void Execute() => Edit.Apply();
        public void Undo() => Edit.Revert();

        public void Dispose()
        {
        }
    }
}
