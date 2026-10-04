using UnityEngine;
using Core.Command;
using Behaviours;

namespace Commands
{
    public class DragCommand : ICommand
    {
        private Draggable _draggable;
        private Vector3 _startPosition;
        private Vector3 _endPosition;

        public DragCommand(Draggable draggable, Vector3 startPosition, Vector3 endPosition)
        {
            _draggable = draggable;
            _startPosition = startPosition;
            _endPosition = endPosition;
        }

        public void Dispose()
        {
        }

        public void Execute()
        {
            _draggable.transform.position = _endPosition;
        }

        public void Undo()
        {
            _draggable.transform.position = _startPosition;
        }
    }
}

