using System;
using System.Collections.Generic;

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
        public NetPlayer OnlyForPlayer;
        public ExecuteFlags ExecuteFlags;

        public int TotalSize => RpcDeltaCompressor.MaxDeltaSize + Header.ByteCount;
        
        public const int ReserverdRPCsCount = 4;
        
        public const ushort NewRPCId = 0;
        public const ushort ConstructRPCId = 1;
        public const ushort DestroyRPCId = 2;
        public const ushort LateConstructedRPCId = 3;

        //can be static because doesn't use any buffers
        //used for header delta compression
        private static DeltaCompressor RpcDeltaCompressor = new(Utils.SizeOfStruct<RPCHeader>());
        
        public static void InitReservedRPCs(List<RpcFieldInfo> rpcCache)
        {
            for(int i = 0; i < ReserverdRPCsCount; i++)
                rpcCache.Add(new RpcFieldInfo(-1, null));
        }

        public bool AllowToSendForPlayer(byte forPlayerId, byte entityOwnerId)
        {
            if (ExecuteFlags.HasFlagFast(ExecuteFlags.SendToAll))
                return true;
            if (ExecuteFlags.HasFlagFast(ExecuteFlags.SendToOwner) && entityOwnerId == forPlayerId)
                return true;
            if (ExecuteFlags.HasFlagFast(ExecuteFlags.SendToOther) && entityOwnerId != forPlayerId)
                return true;
            
            return false;
        }
        
        public unsafe int WriteTo(byte* resultData, ref int position, ref RPCHeader prevHeader)
        {
            int headerEncodedSize = RpcDeltaCompressor.Encode(ref prevHeader, ref Header, new Span<byte>(resultData + position, RpcDeltaCompressor.MaxDeltaSize));
            if (Header.ByteCount > 0)
            {
                fixed (byte* rpcData = Data)
                    RefMagic.CopyBlock(resultData + headerEncodedSize + position, rpcData, Header.ByteCount);
            }
            position += headerEncodedSize + Header.ByteCount;
            prevHeader = Header;
            //Logger.Log($"Sending: EID: {Header.EntityId}, Id: {Header.Id}, BC: {Header.ByteCount}");
            return headerEncodedSize + Header.ByteCount;
        }
        
        public void Init(NetPlayer targetPlayer, InternalEntity entity, ushort tick, ushort byteCount, ushort rpcId, ExecuteFlags executeFlags)
        {
            OnlyForPlayer = targetPlayer;
            ExecuteFlags = executeFlags;
            Header.EntityId = entity.Id;
            Header.Tick = tick;
            Header.Id = rpcId;
            Header.ByteCount = byteCount;
            if(byteCount > 0)
                Utils.ResizeOrCreate(ref Data, byteCount);
        }
    }
}