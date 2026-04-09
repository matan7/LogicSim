using Core.Selecting;
using Core.Settings;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class SelectableSprite : MonoBehaviour, ISelectable
{
    private SpriteRenderer _spriteRenderer;
    private Color _startColor;

    public Transform Transform => transform;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _startColor = _spriteRenderer.color;
    }

    public void Select()
    {
        _spriteRenderer.color = SettingsService.Settings.SelectionColor;
    }

    public void Deselect()
    {
        _spriteRenderer.color = _startColor;
    }
}
