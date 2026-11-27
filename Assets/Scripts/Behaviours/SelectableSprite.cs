using Core.Selecting;
using Core.Settings;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class SelectableSprite : MonoBehaviour, ISelectionView
{
    private SpriteRenderer _spriteRenderer;
    private Color _startColor;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _startColor = _spriteRenderer.color;
    }

    public void SetSelected(bool selected)
    {
        _spriteRenderer.color = selected ? SettingsService.Settings.SelectionColor : _startColor;
    }
}
