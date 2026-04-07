using Core;
using UnityEngine;
using UnityEngine.UI;

public class RedoButton : MonoBehaviour
{
    private Image _iconImage;
    private Button _button;

    void Start()
    {
        _button = GetComponent<Button>();
        _iconImage = GetComponentInChildren<Image>();

        _button.onClick.AddListener(OnClick);
        SystemCore.CommandService.HistoryChanged += OnHistoryChanged;

        _button.interactable = false;
    }

    private void OnHistoryChanged(int undoCount, int redoCount)
    {
        _button.interactable = redoCount > 0;
    }

    private void OnClick()
    {
        SystemCore.CommandService.RedoCommand();
    }
}
