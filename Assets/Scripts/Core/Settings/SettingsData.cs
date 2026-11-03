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

        // Wires and signals
        public Color WireColor = new Color(0.85f, 0.88f, 0.92f, 1f);
        public Color SignalHighColor = new Color(0.35f, 1f, 0.45f, 1f);
        public Color SignalLowColor = new Color(0.18f, 0.32f, 0.22f, 1f);

        // Simulation
        public double TicksPerSecond = 600;

        // MCP server (Model Context Protocol) for AI agents
        public bool McpEnabled = true;
        public int McpPort = 8765;

        // System
        public int UndoHistoryMoves = 100;

    }
}