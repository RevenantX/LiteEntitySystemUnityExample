using Code.Client;
using LiteEntitySystem;
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
    
    [EntityFlags(EntityFlags.UpdateOnClient)]
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
            if (!IsLocal && IsClient)
            {
                //Debug.Log($"Cli_Constructed At TICK: {ClientManager.ServerTick} {this}");
            }
            else if (IsServer)
            {
                //Debug.Log($"Srv_Constructed At TICK: {EntityManager.Tick} {this}");
            }
            
            _unityPhys = EntityManager.GetSingleton<UnityPhysicsManager>();
            if (IsClient)
            {
                var prefab = Resources.Load<GameObject>("ProjectileClient");
                UnityObject = Object.Instantiate(prefab, Position.Value, Quaternion.identity, _unityPhys.Root);
                UnityObject.name = $"Projectile_{Id}";
                //UnityObject.GetComponent<SpriteRenderer>().color = IsLocal ? Color.green : Color.red;
            }
        }

        protected override void OnDestroy()
        {
            if (IsClient && !IsLocal && !HitSomething)
                ClientLogic.Instance.SpawnHit(Position);
            if(UnityObject != null)
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
            //Debug.Log($"Shoot: {EntityManager.Mode}, Tick: {EntityManager.Tick}");
        }

        protected override void Update()
        {
            //skip IsRemoteControlled because EntityFlags.UpdateOnClient
            //but EntityFlags.UpdateOnClient needed for VisualUpdate
            if (HitSomething || (IsClient && IsRemoteControlled))
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
                if (hit.transform.TryGetComponent<PlayerProxy>(out var playerProxy) && playerProxy.AttachedPlayer.SharedReference != ShooterPlayer )
                {
                    playerProxy.AttachedPlayer.Damage(25);
                    if (EntityManager.IsClient && EntityManager.InNormalState)
                        ClientLogic.Instance.SpawnHit(Position);
                    UnityObject?.SetActive(false);
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
            UnityObject.transform.position = Position.InterpolatedValue;
        }
    }
}