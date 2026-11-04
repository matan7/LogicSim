using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LogicSim.Views
{
    /// <summary>View for the hand-drawn gate prefabs (fixed two inputs, or one for NOT/Buffer).</summary>
    public abstract class GateView : ComponentView
    {
        [SerializeField, FormerlySerializedAs("Input1")] private GameObject _input1;
        [SerializeField, FormerlySerializedAs("Input2")] private GameObject _input2;
        [SerializeField, FormerlySerializedAs("Output")] private GameObject _output;

        protected override void CollectPins(List<PinView> inputs, List<PinView> outputs)
        {
            AddPin<InputPin>(_input1, inputs);
            AddPin<InputPin>(_input2, inputs);
            AddPin<OutputPin>(_output, outputs);
        }

        internal static void AddPin<T>(GameObject go, List<PinView> list) where T : PinView
        {
            if (go == null)
                return;
            var pin = go.GetComponent<PinView>();
            if (pin == null)
                pin = go.AddComponent<T>();
            list.Add(pin);
        }
    }
}
