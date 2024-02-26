using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class PlayerNetwork : NetworkBehaviour
{
    private readonly NetworkVariable<PlayerNetworkData> _netState = new NetworkVariable<PlayerNetworkData>(writePerm: NetworkVariableWritePermission.Owner);
    [SerializeField] private float _cheapInterpolationTime = 0.1f;


    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            Debug.Log("server id: " + (int)OwnerClientId);
        }
        if (IsOwner)
        {
            Debug.Log("Owner id: " + (int)OwnerClientId);
        }
        if (IsClient)
        {
            Debug.Log("Client id: " + (int)OwnerClientId);
        }
        
    }

    struct PlayerNetworkData: INetworkSerializable
    {
        private float _xPos, _yPos, _zPos;
        private short _yRot;

        internal Vector3 Position
        {
            readonly get => new Vector3(_xPos, _yPos, _zPos);
            set
            {
                _xPos = value.x;
                _yPos = value.y;
                _zPos = value.z;
            }
        }

        internal Vector3 Rotation
        {
            readonly get => new Vector3(0, _yRot, 0);
            set => _yRot = (short)value.y;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref _xPos);
            serializer.SerializeValue(ref _yPos);
            serializer.SerializeValue(ref _zPos);
            serializer.SerializeValue(ref _yRot);
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (IsOwner)
        {
            _netState.Value = new PlayerNetworkData()
            {
                Position = transform.position,
                Rotation = transform.rotation.eulerAngles

            };

            Move(_netState.Value.Position, _netState.Value.Rotation);

            Debug.Log("Owner set net var - pos: " + transform.position + "rot: " + transform.rotation.eulerAngles );
        }
        else
        {
            //request move from server rpc

            Debug.Log("Client reading net var - pos: " + transform.position + "rot: " + transform.rotation.eulerAngles);
        }
        
    }

    private void Move(Vector3 pos, Vector3 rot)
    {
        transform.position = Vector3.Lerp(transform.position, _netState.Value.Position, _cheapInterpolationTime);

        Quaternion targetRotation = Quaternion.Euler(0, _netState.Value.Rotation.y, 0);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, _cheapInterpolationTime);
    }

    [ServerRpc]
    private void RequestMoveServerRpc()
    {

    }
}