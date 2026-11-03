using System.Collections.Generic;
using LogicSim.Simulation;
using UnityEngine;
using UnityEngine.Serialization;

namespace LogicSim.Views
{
    /// <summary>Toggle switch prefab. Shows the switch state from the model in both modes.</summary>
    public class SwitchView : ComponentView
    {
        [SerializeField, FormerlySerializedAs("Output")] private GameObject _output;
        [SerializeField, FormerlySerializedAs("isOn")] private Sprite _onSprite;
        [SerializeField, FormerlySerializedAs("isOff")] private Sprite _offSprite;

        private SpriteRenderer _renderer;

        public override ComponentKind Kind => ComponentKind.Switch;

        protected override void CollectPins(List<PinView> inputs, List<PinView> outputs)
        {
            _renderer = GetComponent<SpriteRenderer>();
            GateView.AddPin<OutputPin>(_output, outputs);
        }

        public override void OnPropertiesChanged()
        {
            if (_renderer != null)
                _renderer.sprite = Component.Value ? _onSprite : _offSprite;
        }
    }
}
