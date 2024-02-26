using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class BasicPlayerController : NetworkBehaviour
{
    public float moveSpeed = 10.0f;
    public float turnSpeed = 60.0f;
    public float rotationSpeed = 1000f;

    private Rigidbody rb;

    public override void OnNetworkSpawn()
    {
        Debug.Log("inside basic player controller - on network spawn");

        if (IsServer)
        {
            Debug.Log("server has id: " + (int)OwnerClientId);
        }
        if (IsOwner)
        {
            Debug.Log("Owner has id: " + (int)OwnerClientId);
        }
        if (IsClient)
        {
            Debug.Log("Client has id: " + (int)OwnerClientId);
        }
    }

    void Start()
    {
        rb = gameObject.GetComponent<Rigidbody>();

        // Lock and hide cursor only for the local player
        if (IsLocalPlayer)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void FixedUpdate()
    {
        float moveHorizontal = Input.GetAxis("Horizontal");
        float moveVertical = Input.GetAxis("Vertical");
        float rotationInput = Input.GetAxis("Mouse X");

        if (IsOwner)
        {
            PlayerMovement(moveHorizontal, moveVertical, rotationInput);

        }
    }

    void OnCollisionStay(Collision collision)
    {
        // Check if the player is colliding with the terrain
        if (collision.gameObject.CompareTag("Terrain"))
        {
            // Stop the player's movement
            rb.velocity = Vector3.zero;
        }
    }

    [ServerRpc]
    private void playerMovementServerRPC(float mov_x, float mov_y, float rot_y)
    {
        PlayerMovement(mov_x, mov_y, rot_y);
    }


    private void PlayerMovement(float moveHorizontal, float moveVertical, float rotationInput)
    {
        // Control movement and rotation for the local player only


        Vector3 movement = new Vector3(moveHorizontal, 0.0f, moveVertical);
        movement = movement.normalized * moveSpeed * Time.deltaTime;

        rb.MovePosition(transform.position + transform.TransformDirection(movement));

        // Debug.Log("movment: " +  movement);

        
        Debug.Log("Mouse: " + rotationInput);
        float rotationAmount = rotationInput * rotationSpeed * Time.deltaTime;
        Quaternion deltaRotation = Quaternion.Euler(0f, rotationAmount, 0f);
        rb.MoveRotation(rb.rotation * deltaRotation);
    }
}

