using Core.Clipboard;
using Core.Input;
using Core.Selecting;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Assets.Scripts.Actions
{
    internal class CopyAction
    {
        private SelectingService _selectingService;
        private ClipboardService _clipboardService;
        private InputService _inputService;
        public CopyAction(SelectingService selectingService, ClipboardService clipboardService, InputService inputService)
        {
            _selectingService = selectingService;
            _clipboardService = clipboardService;
            _inputService = inputService;

            _inputService.CurrentInputActions.FindAction("Commands/CopyCommand").canceled += Copy;
        }

        private void Copy(InputAction.CallbackContext context)
        {
            if (EventSystem.current.currentSelectedGameObject != null)
            {
                if (EventSystem.current.currentSelectedGameObject.TryGetComponent(out TMP_InputField input))
                {
                    int start = input.selectionStringAnchorPosition;
                    int end = input.selectionStringFocusPosition;

                    if (start > end) (start, end) = (end, start);
                    string selected = input.text.Substring(start, end - start);
                    _clipboardService.SetStringClipboad(selected);
                }
            }
            else if (_selectingService.SelectionList.Count > 0)
            {
                var selectionList = _selectingService.SelectionList;
                var clipboard = new List<GameObject>();
                for (int i = 0; i < selectionList.Count; i++)
                {
                    if (selectionList[i].Transform.gameObject.activeInHierarchy)
                    {
                        clipboard.Add(selectionList[i].Transform.gameObject);
                    }
                }

                _clipboardService.SetClipboard<GameObject>(clipboard);
            }
            else
            {
                _clipboardService.SetClipboard<GameObject>(null);
            }
        }
    }
}
