using System;
using LiteEntitySystem.Collections;
using LiteEntitySystem.Internal;

namespace LiteEntitySystem
{
    public ref struct CustomSyncHandler
    {
        private readonly Span<byte> _resultData;
        private readonly BitSpan _includedFields;
        private StateSerializer _stateSerializer;
        internal bool Skip;
        
        internal CustomSyncHandler(StateSerializer stateSerializer, BitSpan includedFields, Span<byte> resultData)
        {
            _resultData = resultData;
            _includedFields = includedFields;
            _stateSerializer = stateSerializer;
            Skip = false;
        }

        public bool IsFieldIncluded<T>(SyncVar<T> sv) where T : unmanaged => _includedFields[sv.FieldId];

        public void ExcludeField<T>(SyncVar<T> sv) where T : unmanaged => _includedFields[sv.FieldId] = false;

        public void SkipSync() => Skip = true;

        public void ModifyValueOnce<T>(SyncVar<T> sv, T modifiedValue) where T : unmanaged
        {
            
        }
    }
}