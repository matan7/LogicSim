using UnityEngine;

namespace LogicSim.UI
{
    /// <summary>Icons for the generated toolbar buttons (Resources/UiIcons).</summary>
    [CreateAssetMenu(menuName = "LogicSim/UI Icon Set", fileName = "UiIcons")]
    public class UiIconSet : ScriptableObject
    {
        public Sprite New;
        public Sprite Open;
        public Sprite Save;
        public Sprite Cut;
        public Sprite Copy;
        public Sprite Paste;
        public Sprite Delete;
        public Sprite Subcircuit;

        private static UiIconSet _instance;

        public static UiIconSet Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<UiIconSet>("UiIcons");
                return _instance;
            }
        }
    }
}
