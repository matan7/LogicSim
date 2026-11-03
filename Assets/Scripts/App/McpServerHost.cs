using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Core;
using Core.Settings;
using LogicSim.Mcp;
using UnityEngine;

namespace LogicSim.App
{
    /// <summary>
    /// Runs the MCP server inside the app. HTTP requests arrive on thread-pool threads and are executed on the
    /// main thread, between two simulation frames, so tools see and change the live circuit.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class McpServerHost : MonoBehaviour
    {
        private const int MaxMessagesPerFrame = 16;

        private readonly ConcurrentQueue<(string body, TaskCompletionSource<string> result)> _queue =
            new ConcurrentQueue<(string, TaskCompletionSource<string>)>();

        private McpServer _server;
        private StreamableHttpTransport _transport;

        public static McpServerHost Instance { get; private set; }

        public bool IsListening => _transport != null && _transport.IsListening;
        public string Endpoint => _transport?.Endpoint;
        public string LastError { get; private set; }
        public string LastTool { get; private set; }
        public int CallCount { get; private set; }

        public event Action StatusChanged;

        private void Awake()
        {
            Instance = this;
            _server = LogicSimTools.CreateServer(new UnityWorkspace());
            _server.ToolCalled += (name, ok) =>
            {
                LastTool = ok ? name : name + " (error)";
                CallCount++;
                StatusChanged?.Invoke();
            };
        }

        private void Start()
        {
            var settings = SettingsService.Settings;
            if (settings != null && settings.McpEnabled)
                StartServer(settings.McpPort);
        }

        public void StartServer(int port)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            LastError = "MCP is not available in WebGL builds";
            StatusChanged?.Invoke();
#else
            StopServer();
            try
            {
                _transport = new StreamableHttpTransport(port, Enqueue);
                _transport.Log += message => Debug.Log("[LogicSim MCP] " + message);
                _transport.Start();
                LastError = null;
                // Agents talk to the app while another window has focus.
                Application.runInBackground = true;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                _transport = null;
                Debug.LogWarning($"[LogicSim MCP] Could not listen on port {port}: {exception.Message}");
            }
            StatusChanged?.Invoke();
#endif
        }

        public void StopServer()
        {
            _transport?.Dispose();
            _transport = null;
            StatusChanged?.Invoke();
        }

        private Task<string> Enqueue(string body)
        {
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue((body, result));
            return result.Task;
        }

        private void Update()
        {
            bool processed = false;
            for (int i = 0; i < MaxMessagesPerFrame && _queue.TryDequeue(out var item); i++)
            {
                processed = true;
                try
                {
                    item.result.SetResult(_server.HandleMessage(item.body));
                }
                catch (Exception exception)
                {
                    item.result.SetException(exception);
                }
            }
            // Tools may have run the simulation; repaint the values.
            if (processed)
                SystemCore.SimulationService.NotifyValuesChanged();
        }

        private void OnDestroy()
        {
            StopServer();
            while (_queue.TryDequeue(out var item))
                item.result.TrySetCanceled();
            if (Instance == this)
                Instance = null;
        }
    }
}
