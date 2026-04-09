using System;
using UnityEngine;

namespace Core.Settings
{
    [Serializable]
    public class SettingsData
    {

        // Input data
        public float ClickGestureTimeMax = 0.3f;
        public float DragThreshold = 0.1f;

        // UI
        public bool SnapToGrid = true;
        public Vector2 DropPositionOffset = new Vector2(2.5f, 2);
        public Vector2 OrthographicSizeFactor = new Vector2(2900, 2170);
        public Color DragColor = new Color(0, 1, 0.14f, 0.75f);

        public Color SelectionColor = new Color(0.35f, 0.65f, 1f, 1f);

        // Camera
        public Vector2 CameraMinMaxZoom = new Vector2(10, 75);
        public float CameraOrthographicSize = 25;

        // System
        public int UndoHistoryMoves = 100;

    }
}