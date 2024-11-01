using Code.Client;
using LiteEntitySystem;
using LiteEntitySystem.Internal;
using UnityEngine;

namespace Code.Shared
{
    public struct ProjectileInitParams
    {
        public byte OwnerId;
        public Vector2 Position;
        public Vector2 Speed;
        public void Init(SimpleProjectile e) => e.Init(OwnerId, Position, Speed);
    }
    
    [EntityFlags(EntityFlags.Updateable)]
    public class SimpleProjectile : EntityLogic
    {
        [SyncVarFlags(SyncFlags.Interpolated)]
        public SyncVar<Vector2> Position;
        public SyncVar<Vector2> ShooterPos;
        public SyncVar<Vector2> Speed;
        public SyncVar<byte> ShooterPlayerId;
        
        private Rigidbody2D _rigidbody;
        private BoxCollider2D _collider;
        private UnityPhysicsManager _unityPhys;
        public GameObject UnityObject;
        
        private float _lifeTime = 2f;

        protected override void OnConstructed()
        {
            _unityPhys = EntityManager.GetSingleton<UnityPhysicsManager>();
            var prefab = Resources.Load<GameObject>(EntityManager.IsClient ? "ProjectileClient" : "ProjectileServer");
            UnityObject = Object.Instantiate(prefab, Position.Value, Quaternion.identity, _unityPhys.Root);
            UnityObject.name = $"Projectile_{Id}";
        }

        protected override void OnDestroy()
        {
            if (!IsLocal)
                ClientLogic.Instance.SpawnHit(Position);
            Object.Destroy(UnityObject);
            _rigidbody = null;
            _collider = null;
        }

        public SimpleProjectile(EntityParams entityParams) : base(entityParams)
        {
        }

        public void Init(byte playerId, Vector2 position, Vector2 speed)
        {
            Position.Value = position;
            Speed.Value = speed;
            ShooterPos.Value = position;
            ShooterPlayerId.Value = playerId;
        }

        protected override void Update()
        {
            Position.Value += Speed.Value * EntityManager.DeltaTimeF;
            _lifeTime -= EntityManager.DeltaTimeF;
            if (EntityManager.IsServer && _lifeTime <= 0f)
            {
                Destroy();
            }
            //_rigidbody.position = Position;
        }

        protected override void VisualUpdate()
        {
            UnityObject.transform.position = Position.Value;
        }
    }
}