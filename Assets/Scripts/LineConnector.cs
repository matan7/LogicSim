using UnityEngine;

public class LineConnector : MonoBehaviour
{
    [SerializeField] private Transform _startPoint;
    [SerializeField] private Transform _endPoint;
    [SerializeField] private LineRenderer _lineRenderer;

    public void Setup(Transform start, Transform end)
    {
        _startPoint = start;
        _endPoint = end;
    }     

    public void DrawLine()
    {
        if (_startPoint.position == _endPoint.position)
        {
            _lineRenderer.positionCount = 0;
            _lineRenderer.enabled = false;
            return;
        }
        Vector3 startPosition = _startPoint.position;  
        Vector3 endPosition = _endPoint.position;

        // They have same vertical or same horizontal position
        if (startPosition.x == endPosition.x || startPosition.y == endPosition.y)
        {
            _lineRenderer.positionCount = 2;
            _lineRenderer.SetPositions(new Vector3[]{ startPosition, endPosition });
            _lineRenderer.enabled = true;
        }
        else
        {
            _lineRenderer.positionCount = 4;
            Vector3[] positions = new Vector3[4];
            positions[0] = startPosition;
            positions[1] = new Vector3((endPosition.x - startPosition.x) / 2 + startPosition.x, startPosition.y);
            positions[2] = new Vector3((endPosition.x - startPosition.x) / 2 + startPosition.x, endPosition.y);
            positions[3] = endPosition;
            _lineRenderer.SetPositions(positions);
            _lineRenderer.enabled = true;
        }
    }
}
