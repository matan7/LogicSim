using System;
using UnityEngine;

namespace StateMachine
{
    public class StateMachine : MonoBehaviour
    {
        public static StateMachine Instance { get; private set; }
        public AppliacationState AppliacationState { get; private set; }

        public event Action<AppliacationState> AppliacationStateChanged;

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

            AppliacationState = AppliacationState.Editor;
        }

        public void SetApplicationState(AppliacationState state)
        {
            AppliacationState = state;
            AppliacationStateChanged?.Invoke(AppliacationState);
        }

    }
}

