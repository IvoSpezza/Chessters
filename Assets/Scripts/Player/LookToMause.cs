using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
public class LookToMause : NetworkBehaviour
{
    private Camera m_camera; 

    public override void OnNetworkSpawn()
    {
        if (!IsOwner) return;
        m_camera = Camera.main;
     
    }  

    public Vector3 GetOrientation()
    {        
        if (m_camera == null || Mouse.current == null) return transform.forward;        

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray castToGround =  m_camera.ScreenPointToRay(mousePos);
        
        Plane plane = new Plane(Vector3.up, transform.position);

        if (plane.Raycast(castToGround,out float distance))
        {
            Vector3 direction = castToGround.GetPoint(distance) - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) return direction.normalized;
        }        
        return transform.forward;
    }  


}
