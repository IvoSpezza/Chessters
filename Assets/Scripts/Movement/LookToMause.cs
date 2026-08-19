using UnityEngine;
using UnityEngine.InputSystem;
public class LookToMause : MonoBehaviour
{
    [SerializeField] private Camera m_camera;

    private Plane ground;

    private void Start()
    {
        ground = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
    }
    void Update()
    {
        transform.forward = GetOrientation();
    }

    public Vector3 GetOrientation()
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray castToGround =  m_camera.ScreenPointToRay(mousePos);

        if (ground.Raycast(castToGround,out float distance))
        {
            Vector3 worldPoint = castToGround.GetPoint(distance);
            Vector3 direction = worldPoint-transform.position;
            direction.y = 0f;
            return direction.normalized;
        }

        return transform.forward;
    }
}
