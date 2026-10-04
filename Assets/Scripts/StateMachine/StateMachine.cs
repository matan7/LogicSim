using System;
using UnityEngine;

namespace StateMachine
{
    public class StateMachine : MonoBehaviour
    {
        public static StateMachine Instance { get; private set; }
        public ApplicationState ApplicationState { get; private set; }

        public event Action<ApplicationState> ApplicationStateChanged;

        void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;
            }

            ApplicationState = ApplicationState.Editor;
        }

        public void SetApplicationState(ApplicationState state)
        {
            ApplicationState = state;
            ApplicationStateChanged?.Invoke(ApplicationState);
        }

    }
}

