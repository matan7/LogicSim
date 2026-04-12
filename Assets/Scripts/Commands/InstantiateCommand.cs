using UnityEngine;
using Core.Command;
using System.Collections.Generic;
namespace Commands
{
    public class InstantiateCommand : ICommand
    {
        private GameObject _object;
        private List<GameObject> _objects;
        public InstantiateCommand(GameObject @object)
        {
            _object = @object;
        }
        public InstantiateCommand(List<GameObject> objects)
        {
            _objects = objects;
        }

        public void Execute()
        {
            if (_object != null) 
                _object.SetActive(true);
            else
            {
                for (int i = 0; i <  _objects.Count; i++) 
                    _objects[i].SetActive(true);
            }
        }
        public void Undo()
        {
            if (_object != null)
                _object.SetActive(false);
            else
            {
                for (int i = 0; i < _objects.Count; i++)
                    _objects[i].SetActive(false);
            }
        }

        public void Dispose()
        {
            if (_object != null)
            {
                if (!_object.activeSelf)
                    GameObject.Destroy(_object);
            }
            else
            {
                for (int i = 0; i < _objects.Count; i++)
                    if (!_objects[i].activeSelf)
                    {
                        GameObject.Destroy(_objects[i]);
                    }
            }
        }
    }
}
