using Behaviours;
using Commands;
using Core.Clipboard;
using Core.Command;
using Core.Input;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Assets.Scripts.Actions
{
    internal class PasteAction
    {
        private CommandService _commandService;
        private ClipboardService _clipboardService;
        private InputService _inputService;
        private PivotPoint _pivotPoint;
        public PasteAction(CommandService commandService, ClipboardService clipboardService, InputService inputService, PivotPoint pivotPoint)
        {
            _commandService = commandService;
            _clipboardService = clipboardService;
            _inputService = inputService;
            _pivotPoint = pivotPoint;

            _inputService.CurrentInputActions.FindAction("Commands/PasteCommand").canceled += Paste;
        }

        private void Paste(InputAction.CallbackContext context)
        {
            if (EventSystem.current.currentSelectedGameObject != null)
                return;

            var clipboardContent = _clipboardService.GetClipboard<GameObject>();
            if (clipboardContent == null || clipboardContent.Count == 0) 
                return;

            var averagePosition = Vector3.zero;

            for (int i = 0; i < clipboardContent.Count; i++) 
            {
                averagePosition += clipboardContent[i].transform.position;
            }

            averagePosition /= clipboardContent.Count;

            Paste(clipboardContent, _pivotPoint.transform.position - averagePosition);
        }

        private void Paste(List<GameObject> clipboard, Vector3 offsetPosition = new Vector3())
        {
            List<GameObject> list = new List<GameObject>();
            for (int i = 0; i < clipboard.Count; i++)
            {
                if (clipboard[i] != null && clipboard[i].activeInHierarchy)
                {
                    var newObject = GameObject.Instantiate(clipboard[i], clipboard[i].transform.position + offsetPosition, clipboard[i].transform.rotation, clipboard[i].transform.parent);
                    var selectable = clipboard[i].GetComponent<SelectableSprite>();
                    newObject.GetComponent<SelectableSprite>().SetDefaultColor(selectable.GetDefaultColor);
                    newObject.name = clipboard[i].name;
                    list.Add(newObject);
                }
            }

            InstantiateCommand command = new InstantiateCommand(list);
            _commandService.ExecuteCommand(command);
        }
    }
}
