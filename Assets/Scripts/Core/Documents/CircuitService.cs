using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.AppState;
using Core.Command;
using LogicSim.Mcp;
using LogicSim.Simulation;
using UnityEngine;

namespace Core.Documents
{
    /// <summary>
    /// Owns the open circuit document: model, undoable editing, the ICs opened for in-place editing,
    /// file name and saving.
    /// </summary>
    public class CircuitService
    {
        public const string DefaultName = "Untitled";

        private readonly CommandService _commandService;
        private ISignalReader _levelReader;
        private object _levelReaderKey;

        /// <summary>The document (top level).</summary>
        public Circuit Root { get; }

        public CircuitHierarchy Hierarchy { get; }

        /// <summary>The circuit being edited: the document or the working copy of the open IC.</summary>
        public Circuit Circuit => Hierarchy.Current.Circuit;

        /// <summary>Applies edits to <see cref="Circuit"/> through the undo history of the current level.</summary>
        public ICircuitEditor Editor => Hierarchy.Editor;

        public CircuitDocumentStore Documents { get; }
        public string DocumentName { get; private set; } = DefaultName;
        public bool IsDirty { get; private set; }

        /// <summary>Name or dirty flag changed.</summary>
        public event Action DocumentChanged;

        /// <summary>A whole document was opened (views may want to frame it).</summary>
        public event Action DocumentOpened;

        /// <summary>An IC was opened or closed: <see cref="Circuit"/> is a different object now.</summary>
        public event Action LevelChanged;

        public CircuitService(CommandService commandService, AppStateService appState)
        {
            _commandService = commandService;
            Root = new Circuit();
            Hierarchy = new CircuitHierarchy(Root, new CommandEditHistory(commandService));
            Documents = new CircuitDocumentStore(Path.Combine(Application.persistentDataPath, "Circuits"));

            Root.StructureChanged += MarkDirty;
            Root.ComponentMoved += _ => MarkDirty();
            Root.ComponentPropertiesChanged += _ => MarkDirty();
            Root.Library.Changed += MarkDirty;
            Hierarchy.Changed += () => LevelChanged?.Invoke();

            // The simulation runs the document as stored in the library: write back open ICs first.
            appState.StateChanging += state =>
            {
                if (state == ApplicationState.Simulator)
                    ApplyOpenLevels();
            };
        }

        public bool HasName => DocumentName != DefaultName;

        public void SetDocumentName(string name)
        {
            DocumentName = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
            DocumentChanged?.Invoke();
        }

        /// <summary>Writes the working copies of all open ICs back to the library.</summary>
        public int ApplyOpenLevels()
        {
            try
            {
                return Hierarchy.ApplyAll();
            }
            catch (CircuitException exception)
            {
                Debug.LogWarning("[LogicSim] " + exception.Message);
                return 0;
            }
        }

        public void OpenInstance(int instanceId) => Hierarchy.OpenInstance(instanceId);

        public void OpenDefinition(string definitionId) => Hierarchy.OpenDefinition(definitionId);

        public void CloseLevel(bool apply) => Hierarchy.Close(apply);

        public void CloseTo(int depth, bool apply) => Hierarchy.CloseTo(depth, apply);

        /// <summary>
        /// Simulation values for the current level: the document, or the instance the IC was opened through.
        /// Null when the level has unapplied changes or was opened from the library.
        /// </summary>
        public ISignalReader GetLevelSignals(SimulationSession session)
        {
            if (Hierarchy.IsAtRoot)
                return session;
            var level = Hierarchy.Current;
            if (Hierarchy.IsModified(level))
                return null;
            if (!ReferenceEquals(_levelReaderKey, level))
            {
                _levelReaderKey = level;
                var path = Hierarchy.GetInstancePath(level);
                _levelReader = path == null ? null : session.CreateReader(path);
            }
            if (_levelReader is InstanceSignalReader instance && !instance.IsValid)
                return null;
            return _levelReader;
        }

        public void New()
        {
            Hierarchy.CloseTo(0, apply: true);
            Editor.Apply(new ReplaceCircuitEdit(Root, CircuitSnapshot.Empty(), "New circuit"));
            SetDocumentName(DefaultName);
            IsDirty = false;
            DocumentChanged?.Invoke();
            DocumentOpened?.Invoke();
        }

        public string Save(string name = null)
        {
            if (!string.IsNullOrWhiteSpace(name))
                DocumentName = name.Trim();
            ApplyOpenLevels();
            var path = Documents.Save(Root, DocumentName);
            IsDirty = false;
            DocumentChanged?.Invoke();
            return path;
        }

        public void Open(string name) => OpenJson(Documents.Read(name), name);

        /// <summary>Replaces the circuit with a document (undoable).</summary>
        public CircuitDocument OpenJson(string json, string fallbackName)
        {
            var document = CircuitSerializer.Deserialize(json, Root.Library);
            Hierarchy.CloseTo(0, apply: true);
            Editor.Apply(new ReplaceCircuitEdit(Root, document.Snapshot, "Open circuit"));
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

    /// <summary>The model's undo history interface on top of the app's command stack.</summary>
    public sealed class CommandEditHistory : IEditHistory
    {
        private readonly CommandService _commands;

        public CommandEditHistory(CommandService commands)
        {
            _commands = commands;
        }

        public int UndoCount => _commands.UndoCount;
        public int RedoCount => _commands.RedoCount;
        public int ScopeDepth => _commands.ScopeDepth;

        public void Execute(ICircuitEdit edit) => _commands.ExecuteCommand(new CircuitEditCommand(edit));
        public void Record(ICircuitEdit appliedEdit) => _commands.PushExecuted(new CircuitEditCommand(appliedEdit));

        public bool Undo()
        {
            int before = _commands.UndoCount;
            _commands.UndoCommand();
            return _commands.UndoCount < before;
        }

        public bool Redo()
        {
            int before = _commands.RedoCount;
            _commands.RedoCommand();
            return _commands.RedoCount < before;
        }

        public void PushScope() => _commands.PushScope();

        public IReadOnlyList<ICircuitEdit> PopScope()
        {
            return _commands.PopScope().OfType<CircuitEditCommand>().Select(c => c.Edit).ToList();
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
