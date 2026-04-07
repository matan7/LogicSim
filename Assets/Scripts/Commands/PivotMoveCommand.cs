using UnityEngine;
using Core.Command;

namespace Commands
{
    public class PivotMoveCommand : ICommand
    {
        private Transform _pivot;
        private Vector3 _startPosition;
        private Vector3 _endPosition;

        public PivotMoveCommand(Transform pivot, Vector3 startPosition, Vector3 endPosition)
        {
            _pivot = pivot;
            _startPosition = startPosition;
            _endPosition = endPosition;
        }

        public void Dispose()
        {
        }

        public void Execute()
        {
            _pivot.position = _endPosition;
        }

        public void Undo()
        {
            _pivot.position = _startPosition;
        }
    }
}

