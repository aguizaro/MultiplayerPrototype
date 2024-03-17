using UnityEngine;
using Unity.Netcode;

public class HandleGameStates
{
    public class InputState
    {
        public int tick;
        public Vector2 moveInput;
        public Vector2 lookAround; //currently only need y rotation (float) - (x: mouseX delta, y: mouseY delta)
                                  // but I suspect we may want x rotation
    }

    public class TransformStateRW: INetworkSerializable
    {
        public int tick;
        public Vector3 finalPosition;
        public Quaternion finalRotation;
        public bool isMoving;

        private int calculateDataSize()
        {
            int dataSize = FastBufferWriter.GetWriteSize(tick) +
               FastBufferWriter.GetWriteSize(finalPosition) +
               FastBufferWriter.GetWriteSize(finalRotation) +
               FastBufferWriter.GetWriteSize(isMoving);
            return dataSize;
            
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T: IReaderWriter
        {
            if (serializer.IsReader)
            {
                var reader = serializer.GetFastBufferReader();
                reader.TryBeginRead(calculateDataSize());
                reader.ReadValue(out tick);
                reader.ReadValue(out finalPosition);
                reader.ReadValue(out finalRotation);
                reader.ReadValue(out isMoving);
            }
            else
            {
                var writer = serializer.GetFastBufferWriter();
                writer.TryBeginWrite(calculateDataSize());
                writer.WriteValue(tick);
                writer.WriteValue(finalPosition);
                writer.WriteValue(finalRotation);
                writer.WriteValue(isMoving);
                


            }
        }
    }
    
}