namespace LiteEntitySystem.Internal
{
    internal struct RPCHeader
    {
        public ushort EntityId;
        public ushort Id;
        public ushort Tick;
        public ushort ByteCount;
    }
    
    internal sealed class RemoteCallPacket
    {
        public RPCHeader Header;
        public byte[] Data;
        public RemoteCallPacket Next;
        public unsafe int TotalSize => sizeof(RPCHeader) + Header.ByteCount;
        
        //positive only for player
        //negative except that player
        public int ForPlayer;

        public bool ShouldSend(byte playerId) => ForPlayer == 0 || (ForPlayer < 0 
            ? playerId != -ForPlayer  //except
            : playerId == ForPlayer); //only for

        public unsafe void WriteTo(byte* resultData, ref int position)
        {
            *(RPCHeader*)(resultData + position) = Header;
            fixed (byte* rpcData = Data)
                RefMagic.CopyBlock(resultData + sizeof(RPCHeader) + position, rpcData, Header.ByteCount);
            position += sizeof(RPCHeader) + Header.ByteCount;
        }
        
        public void Init(ushort entityId, ushort tick, ushort byteCount, ushort rpcId, int forPlayer)
        {
            Header.EntityId = entityId;
            Header.Tick = tick;
            Header.Id = rpcId;
            ForPlayer = forPlayer;
            Header.ByteCount = byteCount;
            Utils.ResizeOrCreate(ref Data, byteCount);
        }
    }
}