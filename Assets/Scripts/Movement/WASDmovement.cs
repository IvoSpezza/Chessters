using UnityEngine;
using UnityEngine.InputSystem;

public class WASDmovement : MonoBehaviour
{
    [SerializeField] private float _walkSpeed = 5;
    [SerializeField] private float _sprintMultiplier = 1.2f;
    private Vector3 _velocity;
    private bool _running;

    void Update()
    {
        transform.position += _velocity * Time.deltaTime;
    }

    public void OnMove(InputValue value)
    {
        Vector2 movement = value.Get<Vector2>();

        if (_running)
        {
            _velocity = new Vector3(movement.x, 0, movement.y) * _walkSpeed * _sprintMultiplier;
        }
        else
        {
            _velocity = new Vector3(movement.x, 0, movement.y) * _walkSpeed;
        }
    }

    public void OnSprint(InputValue value)
    {
        if (value.isPressed)
        {
            _running = true;
            _velocity *= _sprintMultiplier;
        }
        else
        {
            _running = false;
            _velocity /= _sprintMultiplier;
        }
    }

   
}
