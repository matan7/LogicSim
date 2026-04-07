using UnityEngine;
using Core.Command;
namespace Commands
{
    public class InstantiateCommand : ICommand
    {
        private GameObject _object;

        public InstantiateCommand(GameObject @object)
        {
            _object = @object;
        }

        public void Execute() => _object.SetActive(true);
        public void Undo() => _object.SetActive(false);
        public void Dispose()
        {
            if (!_object.activeSelf)
            {
                GameObject.Destroy(_object);
            }
        }
    }
}
