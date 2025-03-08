namespace LiteEntitySystem.Internal
{
    internal static class ReservedRPCs
    {
        public const int ReserverdRPCsCount = 3;

        public const ushort NewRPCId = 0;
        public const ushort ConstructRPCId = 1;
        public const ushort DeleteRPCId = 2;

        public static void WriteNewRPC(ushort tick, RemoteCallPacket packet)
        {
            packet.Header = new RPCHeader
            {
                ByteCount = 0,
                Id = NewRPCId,
                Tick = tick
            };
            packet.Data = null;
        }
        
        public static void WriteConstructRPC(ushort tick, RemoteCallPacket packet)
        {
            packet.Header = new RPCHeader
            {
                ByteCount = 0,
                Id = ConstructRPCId,
                Tick = tick
            };
            packet.Data = null;
        }
        
        public static readonly MethodCallDelegate NewRPC = (ptr, buffer) =>
        {
            var entityManager = (ClientEntityManager)ptr;
            
        };
        
        public static readonly MethodCallDelegate ConstructRPC = (ptr, _) =>
        {
            var entity = (InternalEntity)ptr;
            entity.OnConstructed();
        };

        public static readonly MethodCallDelegate DeleteRPC = (ptr, _) =>
        {
            var entity = (InternalEntity)ptr;
            //destroy
        };
    }
}