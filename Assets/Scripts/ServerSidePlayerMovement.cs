using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class ServerSidePlayerMovement : NetworkBehaviour
{
    public float moveSpeed = 20.0f;
    public float turnSpeed = 60.0f;
    public float rotationSpeed = 500f;

    //private LobbyManager _lobbyManager;
    private Rigidbody rb;


    private int tick = 0;
    private float tickRate = 1f / 60f;
    private float tickDeltaTime = 0f;
    private const int buffer = 1024;

    private HandleGameStates.InputState[] _inputStates = new HandleGameStates.InputState[buffer];
    private HandleGameStates.TransformStateRW[] _transformStates = new HandleGameStates.TransformStateRW[buffer];


    public NetworkVariable<HandleGameStates.TransformStateRW> currentServerTransformState = new();
    public HandleGameStates.TransformStateRW previousTransformState;


    private void Awake()
    {
        rb = gameObject.GetComponent<Rigidbody>();

        //find next spawn point (to avoid player spawning on top of one another) --- --- 


        // Lock and hide cursor only for the local player
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

    }


    private void OnServerStateChanged(HandleGameStates.TransformStateRW prevVal, HandleGameStates.TransformStateRW newVal)
    {
        previousTransformState = prevVal;
    }

    private void OnEnable()
    {
        currentServerTransformState.OnValueChanged += OnServerStateChanged;
    }

    private void OnDisable()
    {
        currentServerTransformState.OnValueChanged -= OnServerStateChanged;
    }


    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

    }


    // processes player movement for server and clients
    public void ProcessLocalPlayerMovement(Vector2 _moveInput, Vector2 _lookAround)
    {
        tickDeltaTime += Time.deltaTime;

        if (tickDeltaTime > tickRate)
        {   //record an action
            int bufferIndex = tick % buffer;

            if (IsHost) Debug.Log("HOST process input\ntick: " + tick + "\nMove input: " + _moveInput + " MouseX: " + _lookAround.x);
            else Debug.Log("CLIENT process input\ntick: " + tick + "\nMove input: " + _moveInput + " MouseX: " + _lookAround.x);

            // move player on server
            MovePlayerWithServerTickServerRpc(tick, _moveInput, _lookAround);
            //move player localy at the same time
            PlayerMovement(_moveInput, _lookAround.x); // if host becomes much faster, its because playerMov is caled inside the RPC and then again here

            HandleGameStates.InputState inputState = new()
            {
                tick = tick,
                moveInput = _moveInput,
                lookAround = _lookAround

            };

            HandleGameStates.TransformStateRW transformState = new()
            {
                tick = tick,
                finalPosition = transform.position,
                finalRotation = transform.rotation,
                isMoving = !(_moveInput.magnitude == 0 && _lookAround.x == 0)
            };

            if (IsHost) Debug.Log("HOST moved to \ntick: " + tick + "\npos out: " + transformState.finalPosition + " rotation out: " + transformState.finalRotation.eulerAngles);
            else Debug.Log("CLIENT moved to\ntick: " + tick + "\npos out: " + transformState.finalPosition + " rotation out: " + transformState.finalRotation.eulerAngles);


            //store buffer history
            _inputStates[bufferIndex] = inputState;
            _transformStates[bufferIndex] = transformState;

            //check ticks
            tickDeltaTime -= tickRate;
            if (tickRate == buffer)
            {
                tick = 0;
            }
            else
            {
                tick++;
            }
        }

    }

    // replaces the need for NetworkTransform componnent
    public void SimulateOtherPlayers()
    {   // limit other players to the tick rate

        tickDeltaTime += Time.deltaTime;

        if (tickDeltaTime > tickRate)
        {
            if (currentServerTransformState.Value == null) return; //avoid reading variable before data is synced

            if (currentServerTransformState.Value.isMoving)
            {
                transform.position = currentServerTransformState.Value.finalPosition;
                transform.rotation = currentServerTransformState.Value.finalRotation;
            }

            tickDeltaTime -= tickRate;

            if (tick == buffer)
            {
                tick = 0;
            }
            else
            {
                tick++;
            }
        }
    }


    // all player movement must conform to server set tick rate, stores player move state in network variable & updates previous state
    // this server side code moves the current player and stores the data in the buffer
    [ServerRpc]
    private void MovePlayerWithServerTickServerRpc(int tick, Vector2 moveInput, Vector2 lookAround)
    {
        PlayerMovement(moveInput, lookAround.x);

        HandleGameStates.TransformStateRW transformState = new()
        {
            tick = tick,
            finalPosition = transform.position,
            finalRotation = transform.rotation,
            isMoving = !(moveInput.magnitude == 0 && lookAround.x == 0)
        };

        previousTransformState = currentServerTransformState.Value; //this might be redundant bc of the onValChanged event will do this
        currentServerTransformState.Value = transformState;

        Debug.Log("On Server RPC: currentState:\ntick: " + tick + "\npos out: " + transformState.finalPosition + " rotation out: " + transformState.finalRotation.eulerAngles);



    }

    private void Update()
    {
        Vector2 moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        float mouseDelta = Input.GetAxis("Mouse X");

        if (IsClient && IsLocalPlayer)
        {
            ProcessLocalPlayerMovement(moveInput, new Vector2(mouseDelta, 0)); //y val is not being used yet

        }
        else
        {
            SimulateOtherPlayers();
        }

        Debug.Log((IsLocalPlayer ? "Local player" : "non-local player") + " pos: " + transform.position + "\nand rot: " + transform.rotation.eulerAngles);

        //if(currentServerTransformState.Value != null) Debug.Log("CURRENT:\ntick: " + currentServerTransformState.Value.tick + " isMov: " + currentServerTransformState.Value.isMoving + "\npos: " + currentServerTransformState.Value.finalPosition + " rot: " + currentServerTransformState.Value.finalRotation.eulerAngles);
        //if (previousTransformState != null) Debug.Log("PREV:\ntick: " + previousTransformState.tick + " isMov: " + previousTransformState.isMoving + "\npos: " + previousTransformState.finalPosition + " rot: " + previousTransformState.finalRotation.eulerAngles);

    }

    // moves the player using WASD for position and mouseX delta for y rotation
    private void PlayerMovement(Vector2 movementInput, float rotationInput)
    {

        Vector3 movement = new Vector3(movementInput.x, 0.0f, movementInput.y);
        movement = movement.normalized * moveSpeed * tickRate;

        rb.MovePosition(transform.position + transform.TransformDirection(movement));

        float rotationAmount = rotationInput * rotationSpeed * tickRate; //if this gives issues try removing tickRate from calc
        Quaternion deltaRotation = Quaternion.Euler(0f, rotationAmount, 0f);
        rb.MoveRotation(rb.rotation * deltaRotation);
    }
}
