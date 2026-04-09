using UnityEngine;

public class DrawingSelectionManager : MonoBehaviour
{
    public static DrawingSelectionManager Instance { get; private set; }   

    [SerializeField] private SpriteRenderer _selectionSpriteRenderer; 
    private Transform _selectionTransform;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        else
        {
            Instance = this;
        }

        _selectionSpriteRenderer.enabled = false;
        _selectionTransform = _selectionSpriteRenderer.transform;
    }

    public void DrawSelection(Vector3 startPosition, Vector3 currentPosition)
    {
        _selectionTransform.position = (currentPosition + startPosition) / 2;
        Vector2 size = startPosition - currentPosition;
        size.x = Mathf.Abs(size.x);
        size.y = Mathf.Abs(size.y);
        _selectionSpriteRenderer.size = size;
    }

    public void EnableSelectionDrawer(bool enabled)
    {
        _selectionSpriteRenderer.enabled = enabled;
    }
}