using Core.Command;
using UnityEngine;

namespace Commands
{
    public class DeleteCommand : ICommand
    {
        private GameObject _object;
        public DeleteCommand(GameObject @object)
        {
            _object = @object;
        }

        public void Execute() => _object.SetActive(false);
        public void Undo() => _object.SetActive(true);
        public void Dispose()
        {
            if (!_object.activeSelf)
            {
                GameObject.Destroy(_object);
            }
        }
    }
}