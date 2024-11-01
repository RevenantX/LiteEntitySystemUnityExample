using Code.Client;
using LiteEntitySystem;
using LiteEntitySystem.Internal;
using UnityEngine;

namespace Code.Shared
{
    public struct ProjectileInitParams
    {
        public EntitySharedReference Player;
        public Vector2 Position;
        public Vector2 Speed;
        public void Init(SimpleProjectile e) => e.Init(Player, Position, Speed);
    }
    
    [EntityFlags(EntityFlags.Updateable)]
    public class SimpleProjectile : EntityLogic
    {
        private static readonly RaycastHit2D[] RaycastHits = new RaycastHit2D[10];
        
        [SyncVarFlags(SyncFlags.Interpolated)]
        public SyncVar<Vector2> Position;
        public SyncVar<Vector2> Speed;
        public SyncVar<EntitySharedReference> ShooterPlayer;
        public SyncVar<bool> HitSomething;
        
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
            if (!IsLocal && !HitSomething)
                ClientLogic.Instance.SpawnHit(Position);
            Object.Destroy(UnityObject);
        }

        public SimpleProjectile(EntityParams entityParams) : base(entityParams)
        {
        }

        public void Init(EntitySharedReference player, Vector2 position, Vector2 speed)
        {
            Position.Value = position;
            Speed.Value = speed;
            ShooterPlayer.Value = player;
        }

        protected override void Update()
        {
            if (HitSomething)
                return;
            
            EnableLagCompensationForOwner();
            int hitsCount = _unityPhys.PhysicsScene.Raycast(
                Position, 
                Speed.Value.normalized,
                Speed.Value.magnitude * EntityManager.DeltaTimeF,
                RaycastHits);
            DisableLagCompensationForOwner();
            
            for (int i = 0; i < hitsCount; i++)
            {
                ref var hit = ref RaycastHits[i];
                if (hit.transform.TryGetComponent<BasePlayerView>(out var playerProxy) && playerProxy.AttachedPlayer.SharedReference != ShooterPlayer )
                {
                    playerProxy.AttachedPlayer.Damage(25);
                    if (EntityManager.IsClient && EntityManager.InNormalState)
                        ClientLogic.Instance.SpawnHit(Position);
                    UnityObject.SetActive(false);
                    HitSomething.Value = true;
                    break;
                }
            }
            
            Position.Value += Speed.Value * EntityManager.DeltaTimeF;
            _lifeTime -= EntityManager.DeltaTimeF;
            
            if (HitSomething || (EntityManager.IsServer && _lifeTime <= 0f))
                Destroy();
        }

        protected override void VisualUpdate()
        {
            UnityObject.transform.position = Position.Value;
        }
    }
}