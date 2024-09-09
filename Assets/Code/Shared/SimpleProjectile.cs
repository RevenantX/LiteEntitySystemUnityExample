using Code.Client;
using LiteEntitySystem;
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
    
    [SetEntityFlags(EntityFlags.UpdateOnClient)]
    public class SimpleProjectile : EntityLogic
    {
        [SyncVarFlags(SyncFlags.Interpolated)]
        public SyncVar<Vector2> Position;
        public SyncVar<Vector2> ShooterPos;
        public SyncVar<Vector2> Speed;
        public SyncVar<byte> ShooterPlayerId;

        public Vector2 VisualPostion;
        
        private Rigidbody2D _rigidbody;
        private BoxCollider2D _collider;
        private UnityPhysicsManager _unityPhys;
        public GameObject UnityObject;

        private float _spawnLerp = 0f;
        private Vector3 _prevPos;
        private float _lifeTime = 2f;

        protected override void OnConstructed()
        {
            _unityPhys = EntityManager.GetSingleton<UnityPhysicsManager>();

            if (EntityManager.IsClient && ShooterPlayerId != EntityManager.PlayerId)
            {
                _spawnLerp = 1f;
                VisualPostion = ShooterPos;
            }
            else
            {
                VisualPostion = Position;
            }
            _prevPos = VisualPostion;
            
            var prefab = Resources.Load<GameObject>(EntityManager.IsClient ? "ProjectileClient" : "ProjectileServer");
            UnityObject = Object.Instantiate(prefab, VisualPostion, Quaternion.identity, _unityPhys.Root);
            UnityObject.name = $"Projectile_{Id}";
            UnityObject.GetComponent<SimpleProjectileView>().Attached = this;
        }

        protected override void OnDestroy()
        {
            if (!IsLocal && EntityManager.IsClient)
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
            _prevPos = Position.Value;
            if (IsLocal || EntityManager.IsServer)
            {
                Position.Value += Speed.Value * EntityManager.DeltaTimeF;
                _lifeTime -= EntityManager.DeltaTimeF;
                if (EntityManager.IsServer && _lifeTime <= 0f)
                {
                    Destroy();
                }
            }
            //_rigidbody.position = Position;
        }

        protected override void VisualUpdate()
        {
            var visualPos = Vector2.Lerp(_prevPos, Position, EntityManager.LerpFactor);
            if (_spawnLerp > 0f)
            {
                _spawnLerp -= (float)EntityManager.VisualDeltaTime;
                VisualPostion = Vector2.Lerp(ShooterPos, visualPos, 1f - _spawnLerp);
            }
            else
            {
                VisualPostion = visualPos;
            }
        }
    }
}