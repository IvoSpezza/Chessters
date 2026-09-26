using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bossaball.Multiplayer
{
    public class PlayerMovement : NetworkBehaviour
    {
        
        private const float STOPVALUE = 0.01f;

        [Header("Movement")]
        [SerializeField, Min(0)] private float _acceleration = 3f;
        [SerializeField, Min(0)] private float _deceleration = 5f;
        [SerializeField, Min(0)] private float _maxSpeed = 5f;

        [SerializeField, Min(0)] private float _jumpForce = 10f;
        [SerializeField, Min(0)] private float _gravityForce = 20f;

        [Header("Jump")]
        [SerializeField] private LayerMask _groundLayer;
        [SerializeField] private float _groundCheckDistance = 1.1f;
                    
        private PlayerInput _playerInput;
        
        private Vector2 _serverMoveInput = Vector2.zero;

        private Vector3 _actualMovement;
        private float _actualSpeed;

        private float _verticalVelocity;
        private bool _jumpPressed;
        private bool _isGrounded;

        
        //Al logguear, setea los imputs y si esta instancia es la suya, toma el foco de la camara
        public override void OnNetworkSpawn()
        {
            _playerInput = GetComponent<PlayerInput>();
            _playerInput.enabled = IsOwner;
       
            if (!IsOwner) return;                            
        }  
           

        private void FixedUpdate()
        {
            if (!IsServer)
                return;

            CheckGround();

            bool isMoving = _serverMoveInput.sqrMagnitude > STOPVALUE;
            
            SetActualSpeed(isMoving);

            if (_jumpPressed)
            {
                Jump();
                _jumpPressed = false;
            }

            ApplyGravity();
            ApplyMovement();
        }


        //Confirma si el jugador esta en el suelo
        private void CheckGround()
        {
            _isGrounded = Physics.Raycast(transform.position, Vector3.down, _groundCheckDistance, _groundLayer);
        }

      

        private void ApplyGravity()
        {
            if (_isGrounded && _verticalVelocity <= 0f)
            {
                _verticalVelocity = 0f;
                return;
            }

            _verticalVelocity -= _gravityForce * Time.fixedDeltaTime;
        }

        //Dado un bool que representa si se esta moviendo o no, setea la velocidad actual y la direccion
        private void SetActualSpeed(bool moveState)
        {
            float changeFactor = moveState ? _acceleration : _deceleration;
            float objetive = moveState ? _maxSpeed : 0f;
            _actualSpeed = Mathf.MoveTowards(_actualSpeed, objetive, changeFactor * Time.fixedDeltaTime);

            if (moveState)
            {
                _actualMovement = new Vector3(_serverMoveInput.x, 0, _serverMoveInput.y).normalized;
            }
        }

        //Aplica la velocidad actual al personaje
        private void ApplyMovement()
        {
            Vector3 velocity = _actualMovement*_actualSpeed;
            velocity.y = _verticalVelocity;

            transform.position += velocity * Time.fixedDeltaTime;
        }

        //-------------------------------------------------------------------------------------------//
        //Lee la accion de move, W A S D o joystick si estubiera conectado y se lo manda al server
        public void OnMove(InputValue action)
        {
            Vector2 moveInput = action.Get<Vector2>();
            SendMoveInputRpc(moveInput);            
        }

        //Recibe un vector 2 que representa el imput de movimiento y lo valida
        [Rpc(SendTo.Server)]
        private void SendMoveInputRpc(Vector2 input)
        {
            _serverMoveInput = Vector2.ClampMagnitude(input, 1f);
        }
        //-------------------------------------------------------------------------------------------//

        //Lee la accion de saltar, tecla Space o x en joystick. Si el player esta en el suelo, pide un salto
        public void OnJump()
        {
            if (!_isGrounded) return;
            SendJumpInputRpc();
        }

        //Le avisa al server que el player pidio saltar
        [Rpc(SendTo.Server)]
        private void SendJumpInputRpc()
        {               
            _jumpPressed = true;
        }

        //Se ejecuta en el FixedUpade si el server valido el salto
        private void Jump()
        {                     
            _verticalVelocity = _jumpForce;
        }
    }
}
