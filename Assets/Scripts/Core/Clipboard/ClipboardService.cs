using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Clipboard
{
    public class ClipboardService
    {
        private Dictionary<Type, object> _clipboards = new Dictionary<Type, object>();

        public void SetClipboard<T>(List<T> items)
        {
            _clipboards[typeof(T)] = items;
        }

        public void SetStringClipboad(string data)
        {
            GUIUtility.systemCopyBuffer = data;
        }

        public List<T> GetClipboard<T>()
        {
            if (_clipboards.TryGetValue(typeof(T), out var list))
                return (List<T>)list;
            return null;
        }
    }
}
