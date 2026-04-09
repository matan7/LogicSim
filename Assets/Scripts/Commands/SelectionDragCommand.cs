using UnityEngine;
using Core.Command;
using System.Collections.Generic;
using Behaviours;

namespace Commands
{
    public class SelectionDragCommand : ICommand
    {
        private List<Dragable> _selectedDragables;
        private List<Vector3> _startPositions;
        private List<Vector3> _endPositions;

        public SelectionDragCommand(List<Dragable> selectedDragables, List<Vector3> startPositions, List<Vector3> endPositions)
        {
            _selectedDragables = selectedDragables;
            _startPositions = startPositions;
            _endPositions = endPositions;
        }

        public void Dispose()
        {
        }

        public void Execute()
        {
            for (int i = 0; i < _selectedDragables.Count; i++)
            {
                _selectedDragables[i].transform.position = _endPositions[i];
            }
        }

        public void Undo()
        {
            for (int i = 0; i < _selectedDragables.Count; i++)
            {
                _selectedDragables[i].transform.position = _startPositions[i];
            }
        }
    }
}