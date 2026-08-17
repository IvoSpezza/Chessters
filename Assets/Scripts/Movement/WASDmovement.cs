using UnityEngine;
using UnityEngine.InputSystem;

public class WASDmovement : MonoBehaviour
{
    [SerializeField] private float _maxSpeed = 5;
    private Vector3 _velocity;



    void Update()
    {
        transform.position += _velocity * Time.deltaTime;
    }

    public void OnMove(InputValue value)
    {
        Vector2 movement = value.Get<Vector2>();
        _velocity = new Vector3(movement.x, 0, movement.y) * _maxSpeed;
    }
}
