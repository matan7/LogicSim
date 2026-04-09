using Core.Command;
using System.Collections.Generic;
using UnityEngine;

namespace Commands
{
    public class DeleteCommand : ICommand
    {
        private List<GameObject> _objects;

        public DeleteCommand(List<GameObject> objects)
        {
            _objects = objects;
        }

        public void Execute()
        {
            for (int i = 0; i < _objects.Count; i++)
            {
                _objects[i].SetActive(false);
            }
        }

        public void Undo()
        {
            for (int i = 0; i < _objects.Count; i++)
            {
                _objects[i].SetActive(true);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _objects.Count; i++)
            {
                if (!_objects[i].activeSelf)
                {
                    GameObject.Destroy(_objects[i]);
                }
            }
            
        }
    }
}