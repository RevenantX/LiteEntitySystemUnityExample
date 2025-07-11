using System;

namespace LiteEntitySystem.Internal
{
    internal enum DiffType
    {
        Normal,
        Constructed,
        LateConstruct
    }
    
    internal struct StateSerializer
    {
        public static readonly int HeaderSize = Utils.SizeOfStruct<EntityDataHeader>();
        public const int DiffHeaderSize = 4;
        public const int MaxStateSize = ushort.MaxValue;
        
        private const int TickBetweenFullRefresh = ushort.MaxValue/5;
        
        private EntityFieldInfo[] _fields;
        private int _fieldsCount;
        private int _fieldsFlagsSize;
        private EntityFlags _flags;
        private InternalEntity _entity;
        private byte[] _latestEntityData;
        private ushort[] _fieldChangeTicks;
        private ushort _versionChangedTick;
        private uint _fullDataSize;
        
        public byte NextVersion;
        public ushort LastChangedTick;
        
        private DateTime _lastRefreshedTime;
        private int _secondsBetweenRefresh;
        
        public void AllocateMemory(ref EntityClassData classData, byte[] ioBuffer)
        {
            if (_entity != null)
            {
                Logger.LogError($"State serializer isn't freed: {_entity}");
                return;
            }
            
            if (GetMaximumDiffSize() > ushort.MaxValue)
                throw new Exception($"Entity classId: {classData.ClassId - 1} is too big: {GetMaximumDiffSize()} > {ushort.MaxValue}");
            
            _fields = classData.Fields;
            _fieldsCount = classData.FieldsCount;
            _fieldsFlagsSize = classData.FieldsFlagsSize;
            _fullDataSize = (uint)(HeaderSize + classData.FixedFieldsSize);
            _flags = classData.Flags;
            _latestEntityData = ioBuffer;
            
            if (_fieldChangeTicks == null || _fieldChangeTicks.Length < _fieldsCount)
                _fieldChangeTicks = new ushort[_fieldsCount];
        }

        public unsafe void Init(InternalEntity e, ushort tick)
        {
            _entity = e;
            NextVersion = (byte)(_entity.Version + 1);
            _versionChangedTick = tick;
            LastChangedTick = tick;
            
            fixed (byte* data = _latestEntityData)
                *(EntityDataHeader*)data = _entity.DataHeader;

            _lastRefreshedTime = DateTime.UtcNow;
            _secondsBetweenRefresh = TickBetweenFullRefresh / e.ServerManager.Tickrate;
        }
        
        public unsafe void UpdateFieldValue<T>(ushort fieldId, ushort minimalTick, ushort tick, ref T newValue) where T : unmanaged
        {
            _fieldChangeTicks[fieldId] = tick;
            MarkChanged(minimalTick, tick);
            fixed (byte* data = &_latestEntityData[HeaderSize + _fields[fieldId].FixedOffset])
                *(T*)data = newValue;
        }
        
        public void MarkFieldsChanged(ushort minimalTick, ushort tick, SyncFlags onlyWithFlags)
        {
            for (int i = 0; i < _fieldsCount; i++)
                if ((_fields[i].Flags & onlyWithFlags) == onlyWithFlags)
                    _fieldChangeTicks[i] = tick;
            MarkChanged(minimalTick, tick);
        }

        public void MarkChanged(ushort minimalTick, ushort tick)
        {
            LastChangedTick = tick;
            //refresh every X seconds to prevent wrap-arounded bugs
            DateTime currentTime = DateTime.UtcNow;
            if ((currentTime - _lastRefreshedTime).TotalSeconds > _secondsBetweenRefresh)
            {
                _versionChangedTick = minimalTick;
                for (int i = 0; i < _fieldsCount; i++)
                    if(_fieldChangeTicks[i] != tick) //change only not refreshed at current tick
                        _fieldChangeTicks[i] = minimalTick;
                _lastRefreshedTime = currentTime;
            }
        }

        //size of all values (in worst case), DiffHeader, and size of bitfield with information about fields exist or not 
        public int GetMaximumDiffSize() =>
            _entity == null ? 0 : (int)(_fullDataSize - HeaderSize) + DiffHeaderSize + _fieldsFlagsSize;

        public void MakeNewRPC()
        {
            _entity.ServerManager.AddRemoteCall(
                _entity,
                new ReadOnlySpan<byte>(_latestEntityData, 0, HeaderSize),
                RemoteCallPacket.NewRPCId,
                ExecuteFlags.SendToAll);
        }

        public unsafe void MakeConstructedRPC(NetPlayer player)
        {
            //make on sync
            try
            {
                var syncableFields = _entity.ClassData.SyncableFields;
                for (int i = 0; i < syncableFields.Length; i++)
                    RefMagic.GetFieldValue<SyncableField>(_entity, syncableFields[i].Offset).OnSyncRequested();
                _entity.OnSyncRequested();
            }
            catch (Exception e)
            {
                Logger.LogError($"Exception in OnSyncRequested: {e}");
            }

            var constructedRpc =
                _entity.ServerManager.GetRPCFromPool(
                    _entity,
                    RemoteCallPacket.ConstructRPCId,
                    (ushort)GetMaximumDiffSize());
            
            fixed (byte* rawData = constructedRpc.Data)
            {
                int resultPosition = 0;
                MakeDiff(player, rawData, ref resultPosition, DiffType.Constructed);
                constructedRpc.Header.ByteCount = (ushort)resultPosition;
                //Logger.Log($"Server send CRPCData {_entity.Id}, sz {resultPosition}: {Utils.BytesToHexString(new ReadOnlySpan<byte>(rawData, resultPosition))}");
            }
            _entity.ServerManager.EnqueuePendingRPC(constructedRpc);
        }

        public void MakeDestroyedRPC(ushort tick)
        {
            //Logger.Log($"DestroyEntity: {_entity.Id} {_entity.Version}, ClassId: {_entity.ClassId}");
            LastChangedTick = tick;
            _entity.ServerManager.AddRemoteCall(
                _entity,
                RemoteCallPacket.DestroyRPCId,
                ExecuteFlags.SendToAll);
        }

        public bool ShouldSync(byte playerId, bool includeDestroyed)
        {
            if (_entity == null || (!includeDestroyed && _entity.IsDestroyed))
                return false;
            if (_flags.HasFlagFast(EntityFlags.OnlyForOwner) && _entity.InternalOwnerId.Value != playerId)
                return false;
            return true;
        }

        public void Free()
        {
            _entity = null;
            _latestEntityData = null;
        }

        public unsafe bool MakeDiff(NetPlayer player, byte* resultData, ref int position, DiffType diffType)
        {
            if (_entity == null)
            {
                Logger.LogWarning("MakeDiff on freed?");
                return false;
            }
            
            //skip known
            if (diffType == DiffType.Normal && Utils.SequenceDiff(LastChangedTick, player.CurrentServerTick) <= 0)
                return false;
            
            //skip sync for non owners
            bool isOwned = _entity.InternalOwnerId.Value == player.Id;
            if (_flags.HasFlagFast(EntityFlags.OnlyForOwner) && !isOwned)
                return false;
            
            //make diff
            int startPos = position;
            //at 0 ushort
            ushort* totalSize = (ushort*)(resultData + startPos);
            *totalSize = 0;
            
            position += sizeof(ushort);

            //if constructed not received send difference from constructed. Else from last state
            ushort compareToTick = Utils.SequenceDiff(_versionChangedTick, player.CurrentServerTick) > 0
                ? _versionChangedTick
                : player.CurrentServerTick;
            
            //overwrite IsSyncEnabled for each player
            SyncGroup enabledSyncGroups = SyncGroup.All;
            if (_entity is EntityLogic el)
            {
                if (player.EntitySyncInfo.TryGetValue(el, out var syncGroupData))
                {
                    enabledSyncGroups = syncGroupData.EnabledGroups;
                    _fieldChangeTicks[el.IsSyncEnabledFieldId] = syncGroupData.LastChangedTick;
                }
                else
                {
                    //if no data it "never" changed
                    _fieldChangeTicks[el.IsSyncEnabledFieldId] = _versionChangedTick;
                }
                _latestEntityData[HeaderSize + _fields[el.IsSyncEnabledFieldId].FixedOffset] = (byte)enabledSyncGroups;
            }

            fixed (byte* lastEntityData = _latestEntityData) //make diff
            {
                byte* entityDataAfterHeader = lastEntityData + HeaderSize;
                
                // -1 for cycle
                byte* fields = resultData + startPos + DiffHeaderSize - 1;
                //put entity id at 2
                *(ushort*)(resultData + position) = _entity.Id;
                position += sizeof(ushort) + _fieldsFlagsSize;
                int positionBeforeDeltaCompression = position;

                //write fields
                for (int i = 0; i < _fieldsCount; i++)
                {
                    int currentBit = i % 8;
                    if (currentBit == 0)
                    {
                        fields++;
                        *fields = 0;
                    }
                    
                    ref var field = ref _fields[i];
                    
                    switch (diffType)
                    {
                        case DiffType.Normal:
                            //not actual
                            if (Utils.SequenceDiff(_fieldChangeTicks[i], compareToTick) <= 0)
                            {
                                //Logger.Log($"SkipOld: {field.Name}");
                                //old data
                                continue;
                            }
                            break;
                        
                        case DiffType.Constructed:
                            //skip default
                            if (field.TypeProcessor.IsDefault(_entity, field.Offset))
                            {
                                continue;
                            }
                            break;
                    }
                    
                    if (((field.Flags & SyncFlags.OnlyForOwner) != 0 && !isOwned) || 
                        ((field.Flags & SyncFlags.OnlyForOtherPlayers) != 0 && isOwned))
                    {
                        //Logger.Log($"SkipSync: {field.Name}, isOwned: {isOwned}");
                        continue;
                    }
                    
                    if(!isOwned && SyncGroupUtils.IsSyncVarDisabled(enabledSyncGroups, field.Flags))
                    {
                        //IgnoreDiffSyncSettings
                        continue;
                    }
                    
                    *fields |= (byte)(1 << currentBit);
                    RefMagic.CopyBlock(resultData + position, entityDataAfterHeader + field.FixedOffset, field.Size);
                    position += field.IntSize;
                    //Logger.Log($"WF {_entity.GetType()} f: {_classData.Fields[i].Name}");
                }

                if (position == positionBeforeDeltaCompression)
                {
                    position = startPos;
                    return false;
                }
            }

            *totalSize = (ushort)(position - startPos);
            return true;
        }
    }
}