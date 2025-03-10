using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using K4os.Compression.LZ4;

namespace LiteEntitySystem.Internal
{
    internal readonly struct InterpolatedCache
    {
        public readonly InternalEntity Entity;
        public readonly int FieldOffset;
        public readonly int FieldFixedOffset;
        public readonly ValueTypeProcessor TypeProcessor;
        public readonly int StateReaderOffset;

        public InterpolatedCache(InternalEntity entity, ref EntityFieldInfo field, int offset)
        {
            Entity = entity;
            FieldOffset = field.Offset;
            FieldFixedOffset = field.FixedOffset;
            TypeProcessor = field.TypeProcessor;
            StateReaderOffset = offset;
        }
    }

    internal class ServerStateData
    {
        public byte[] Data = new byte[1500];
        public int Size;
        public ushort Tick;
        public ushort ProcessedTick;
        public ushort LastReceivedTick;
        public byte BufferedInputsCount;
        public int InterpolatedCachesCount;
        public InterpolatedCache[] InterpolatedCaches = new InterpolatedCache[32];
        
        private int _totalPartsCount;
        private int _receivedPartsCount;
        private byte _maxReceivedPart;
        private ushort _partMtu;
        private readonly BitArray _receivedParts = new (EntityManager.MaxParts);
        
        private int _dataOffset;
        private int _dataSize;
        private int _rpcReadPos;
        private int _rpcEndPos;
        
        public int DataOffset => _dataOffset;
        public int DataSize => _dataSize;
        
        private static readonly ThreadLocal<HashSet<SyncableField>> SyncablesSet = new(()=>new HashSet<SyncableField>());
        
        public void Preload(InternalEntity[] entityDict)
        {
            for (int bytesRead = _dataOffset; bytesRead < _dataOffset + _dataSize;)
            {
                int initialReaderPosition = bytesRead;
                ushort fullSyncAndTotalSize = BitConverter.ToUInt16(Data, initialReaderPosition);
                bool fullSync = (fullSyncAndTotalSize & 1) == 1;
                int totalSize = fullSyncAndTotalSize >> 1;
                bytesRead += totalSize;
                ushort entityId = BitConverter.ToUInt16(Data, initialReaderPosition + sizeof(ushort));
                if (entityId == EntityManager.InvalidEntityId || entityId >= EntityManager.MaxSyncedEntityCount)
                {
                    //Should remove at all
                    Logger.LogError($"[CEM] Invalid entity id: {entityId}");
                    return;
                }
      
                //it should be here at preload
                var entity = entityDict[entityId];
                if (entity == null)
                {
                    //Removed entity
                    //Logger.LogError($"Preload entity: {preloadData.EntityId} == null");
                    continue;
                }

                ref var classData = ref entity.ClassData;
                int entityFieldsOffset = initialReaderPosition + StateSerializer.DiffHeaderSize;
                int stateReaderOffset = fullSync 
                    ? initialReaderPosition + StateSerializer.HeaderSize + sizeof(ushort) 
                    : entityFieldsOffset + classData.FieldsFlagsSize;

                //preload interpolation info
                if (entity.IsRemoteControlled && classData.InterpolatedCount > 0)
                    Utils.ResizeIfFull(ref InterpolatedCaches, InterpolatedCachesCount + classData.InterpolatedCount);
                for (int i = 0; i < classData.FieldsCount; i++)
                {
                    if (!fullSync && !Utils.IsBitSet(Data, entityFieldsOffset, i))
                        continue;
                    ref var field = ref classData.Fields[i];
                    if (entity.IsRemoteControlled && field.Flags.HasFlagFast(SyncFlags.Interpolated))
                        InterpolatedCaches[InterpolatedCachesCount++] = new InterpolatedCache(entity, ref field, stateReaderOffset);
                    stateReaderOffset += field.IntSize;
                }

                if (stateReaderOffset != initialReaderPosition + totalSize)
                {
                    Logger.LogError($"Missread! {stateReaderOffset} > {initialReaderPosition + totalSize}");
                    return;
                }
            }
        }
        
        public unsafe void ExecuteRpcs(ClientEntityManager entityManager, ushort minimalTick, bool firstSync)
        {
            var syncSet = SyncablesSet.Value;
            syncSet.Clear();
            //if(_remoteCallsCount > 0)
            //    Logger.Log($"Executing rpcs (ST: {Tick}) for tick: {entityManager.ServerTick}, Min: {minimalTick}, Count: {_remoteCallsCount}");
            fixed (byte* rawData = Data)
            {
                while (_rpcReadPos < _rpcEndPos)
                {
                    if (_rpcEndPos - _rpcReadPos < sizeof(RPCHeader))
                    {
                        Logger.LogError("Broken rpcs sizes?");
                        return;
                    }
                    
                    var header = *(RPCHeader*)(rawData + _rpcReadPos);
                    int rpcDataStart = _rpcReadPos + sizeof(RPCHeader);
                    
                    if (!firstSync)
                    {
                        if (Utils.SequenceDiff(header.Tick, entityManager.ServerTick) > 0)
                        {
                            //Logger.Log($"Skip rpc. Entity: {rpc.EntityId}. Tick {rpc.Header.Tick} > ServerTick: {entityManager.ServerTick}. Id: {rpc.Header.Id}.");
                            return;
                        }

                        if (Utils.SequenceDiff(header.Tick, minimalTick) <= 0)
                        {
                            _rpcReadPos += header.ByteCount + sizeof(RPCHeader);
                            //Logger.Log($"Skip rpc. Entity: {rpc.EntityId}. Tick {rpc.Header.Tick} <= MinimalTick: {minimalTick}. Id: {rpc.Header.Id}.");
                            continue;
                        }
                    }
                    
                    _rpcReadPos += header.ByteCount + sizeof(RPCHeader);

                    //Logger.Log($"Executing rpc. Entity: {rpc.EntityId}. Tick {rpc.Header.Tick}. Id: {rpc.Header.Id}. Type: {rpcType}");
                    var entity = entityManager.EntitiesDict[header.EntityId];
                    if (entity == null)
                    {
                        if (header.Id == RemoteCallPacket.NewRPCId)
                        {
                            Logger.Log("NewRPC");
                            continue;
                        }
                        else
                        {
                            Logger.LogError($"Entity is null: {header.EntityId}");
                            continue;
                        }
                    }
                    
                    entityManager.CurrentRPCTick = header.Tick;
                    
                    var rpcFieldInfo = entityManager.ClassDataDict[entity.ClassId].RemoteCallsClient[header.Id];
                    if (rpcFieldInfo.SyncableOffset == -1)
                    {
                        try
                        {
                            if (header.Id == RemoteCallPacket.NewRPCId)
                            {
                                Logger.Log("NewRPC when entity created???");
                            }
                            else if (header.Id == RemoteCallPacket.ConstructRPCId)
                            {
                                Logger.Log("ConstructRPC");
                                //entityManager.ConstructEntity(entity);
                            }
                            else if (header.Id == RemoteCallPacket.DeleteRPCId)
                            {
                                Logger.Log("DeleteRPC");
                            }
                            else
                            {
                                rpcFieldInfo.Method(entity, new ReadOnlySpan<byte>(rawData + rpcDataStart, header.ByteCount));
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.LogError($"Error when executing RPC: {entity}. RPCID: {header.Id}. {e}");
                        }
                    }
                    else
                    {
                        var syncableField = RefMagic.RefFieldValue<SyncableField>(entity, rpcFieldInfo.SyncableOffset);
                        if (syncSet.Add(syncableField))
                            syncableField.BeforeReadRPC();
                        try
                        {
                            rpcFieldInfo.Method(syncableField, new ReadOnlySpan<byte>(rawData + rpcDataStart, header.ByteCount));
                        }
                        catch (Exception e)
                        {
                            Logger.LogError($"Error when executing syncableRPC: {entity}. RPCID: {header.Id}. {e}");
                        }
                    }
                }
            }
            foreach (var syncableField in syncSet)
                syncableField.AfterReadRPC();
        }

        public void Reset(ushort tick)
        {
            Tick = tick;
            _receivedParts.SetAll(false);
            InterpolatedCachesCount = 0;
            _maxReceivedPart = 0;
            _receivedPartsCount = 0;
            _totalPartsCount = 0;
            Size = 0;
            _partMtu = 0;
        }

        public unsafe bool ReadBaseline(BaselineDataHeader header, byte* rawData, int fullSize)
        {
            Reset(header.Tick);
            Size = header.OriginalLength;
            Data = new byte[header.OriginalLength];
            _dataOffset = 0;
            _dataSize = header.EventsOffset;
            _rpcReadPos = header.EventsOffset;
            _rpcEndPos = Size;
            fixed (byte* stateData = Data)
            {
                int decodedBytes = LZ4Codec.Decode(
                    rawData + sizeof(BaselineDataHeader),
                    fullSize - sizeof(BaselineDataHeader),
                    stateData,
                    Size);
                if (decodedBytes != header.OriginalLength)
                {
                    Logger.LogError("Error on decompress");
                    return false;
                }
            }
            return true;
        }

        public unsafe bool ReadPart(DiffPartHeader partHeader, byte* rawData, int partSize)
        {
            if (_receivedParts[partHeader.Part])
            {
                //duplicate ?
                return false;
            }
            if (partHeader.PacketType == InternalPackets.DiffSyncLast)
            {
                partSize -= sizeof(LastPartData);
                var lastPartData = *(LastPartData*)(rawData + partSize);
                _totalPartsCount = partHeader.Part + 1;
                _partMtu = (ushort)(lastPartData.Mtu - sizeof(DiffPartHeader));
                LastReceivedTick = lastPartData.LastReceivedTick;
                ProcessedTick = lastPartData.LastProcessedTick;
                BufferedInputsCount = lastPartData.BufferedInputsCount;
                _dataOffset = lastPartData.EventsSize;
                _rpcReadPos = 0;
                _rpcEndPos = lastPartData.EventsSize;
                //Logger.Log($"TPC: {partHeader.Part} {_partMtu}, LastReceivedTick: {LastReceivedTick}, LastProcessedTick: {ProcessedTick}");
            }
            partSize -= sizeof(DiffPartHeader);
            if(_partMtu == 0)
                _partMtu = (ushort)partSize;
            Utils.ResizeIfFull(ref Data, _totalPartsCount > 1 
                ? _partMtu * _totalPartsCount 
                : _partMtu * partHeader.Part + partSize);
            fixed(byte* stateData = Data)
                RefMagic.CopyBlock(stateData + _partMtu * partHeader.Part, rawData + sizeof(DiffPartHeader), (uint)partSize);
            _receivedParts[partHeader.Part] = true;
            Size += partSize;
            _receivedPartsCount++;
            _maxReceivedPart = partHeader.Part > _maxReceivedPart ? partHeader.Part : _maxReceivedPart;

            if (_receivedPartsCount == _totalPartsCount)
            {
                _dataSize = Size - _dataOffset;
                return true;
            }
            return false;
        }
    }
}