using System.Collections.Generic;
using LogicSim.Simulation;
using UnityEngine;
using UnityEngine.Serialization;

namespace LogicSim.Views
{
    /// <summary>Bulb prefab: lit while its input is 1 during the simulation.</summary>
    public class BulbView : ComponentView
    {
        [SerializeField, FormerlySerializedAs("Input")] private GameObject _input;
        [SerializeField, FormerlySerializedAs("isOn")] private Sprite _onSprite;
        [SerializeField, FormerlySerializedAs("isOff")] private Sprite _offSprite;

        private SpriteRenderer _renderer;

        public override ComponentKind Kind => ComponentKind.Bulb;

        protected override void CollectPins(List<PinView> inputs, List<PinView> outputs)
        {
            _renderer = GetComponent<SpriteRenderer>();
            GateView.AddPin<InputPin>(_input, inputs);
        }

        public override void ShowValues(ISignalReader session)
        {
            base.ShowValues(session);
            if (_renderer != null)
                _renderer.sprite = session != null && session.GetInputPinValue(ComponentId, 0) ? _onSprite : _offSprite;
        }
    }
}
