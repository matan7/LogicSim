using UnityEngine;
using Core.Command;
using System.Collections.Generic;
using Behaviours;

namespace Commands
{
    public class SelectionDragCommand : ICommand
    {
        private List<Draggable> _selectedDraggables;
        private List<Vector3> _startPositions;
        private List<Vector3> _endPositions;

        public SelectionDragCommand(List<Draggable> selectedDraggables, List<Vector3> startPositions, List<Vector3> endPositions)
        {
            _selectedDraggables = selectedDraggables;
            _startPositions = startPositions;
            _endPositions = endPositions;
        }

        public void Dispose()
        {
        }

        public void Execute()
        {
            for (int i = 0; i < _selectedDraggables.Count; i++)
            {
                _selectedDraggables[i].transform.position = _endPositions[i];
            }
        }

        public void Undo()
        {
            for (int i = 0; i < _selectedDraggables.Count; i++)
            {
                _selectedDraggables[i].transform.position = _startPositions[i];
            }
        }
    }
}