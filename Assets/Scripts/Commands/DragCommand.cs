using UnityEngine;
using Core.Command;
using Behaviours;

namespace Commands
{
    public class DragCommand : ICommand
    {
        private Dragable _dragable;
        private Vector3 _startPosition;
        private Vector3 _endPosition;

        public DragCommand(Dragable dragable, Vector3 startPosition, Vector3 endPosition)
        {
            _dragable = dragable;
            _startPosition = startPosition;
            _endPosition = endPosition;
        }

        public void Dispose()
        {
        }

        public void Execute()
        {
            _dragable.transform.position = _endPosition;
        }

        public void Undo()
        {
            _dragable.transform.position = _startPosition;
        }
    }
}

