using LiteEntitySystem;
using UnityEngine;

namespace Code.Shared
{
    public class WeaponItem : EntityLogic
    {
        public SyncVar<Vector2> Position;

        public byte WeaponType;
        
        public WeaponItem(EntityParams entityParams) : base(entityParams)
        {
            
        }
    }
}