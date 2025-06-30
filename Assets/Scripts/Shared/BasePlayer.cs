﻿using Code.Client;
using LiteEntitySystem;
using LiteEntitySystem.Extensions;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Code.Shared
{
    public class BasePlayer : PawnLogic
    {
        private static readonly RaycastHit2D[] RaycastHits = new RaycastHit2D[100];
        
        [SyncVarFlags(SyncFlags.Interpolated | SyncFlags.LagCompensated | SyncFlags.SyncGroup1)] 
        private SyncVar<Vector2> _position;
        
        [SyncVarFlags(SyncFlags.Interpolated | SyncFlags.LagCompensated | SyncFlags.SyncGroup1)] 
        private SyncVar<FloatAngle> _rotation;
        
        [SyncVarFlags(SyncFlags.AlwaysRollback | SyncFlags.SyncGroup1)] 
        private SyncVar<byte> _health;
        
        [SyncVarFlags(SyncFlags.SyncGroup1)]
        private SyncVar<float> _speed;
        public readonly SyncString Name = new ();
        private readonly SyncTimer _shootTimer = new (0.5f);
        private static RemoteCall<ShootPacket> _shootRemoteCall;
        private static RemoteCall<HitPacket> _hitRemoteCall;
        
        private Vector2 _velocity;
        private Rigidbody2D _rigidbody;
        private BoxCollider2D _collider;
        private UnityPhysicsManager _unityPhys;
        private float _targetRotation;
        private bool _isFiring;
        private bool _projectileFire;
        
        public byte Health => _health;
        public Vector2 Position => _position.InterpolatedValue;
        public float Rotation => _rotation.InterpolatedValue;
        
        public GameObject UnityObject;
        public GameObject View;
        
        public BasePlayer(EntityParams entityParams) : base(entityParams)
        {
            _speed.Value = 3f;
        }

        protected override void RegisterRPC(ref RPCRegistrator r)
        {
            base.RegisterRPC(ref r);
            r.CreateRPCAction(this, OnShoot, ref _shootRemoteCall, ExecuteFlags.ExecuteOnPrediction | ExecuteFlags.SendToOther | ExecuteFlags.SyncGroup1);
            r.CreateRPCAction(this, OnHit, ref _hitRemoteCall, ExecuteFlags.ExecuteOnPrediction | ExecuteFlags.SendToOther | ExecuteFlags.SyncGroup1);
        }

        protected override void OnConstructed()
        {
            _unityPhys = EntityManager.GetSingleton<UnityPhysicsManager>();
            UnityObject = new GameObject($"Player_{Name}_{Id}");
            UnityObject.transform.SetParent(_unityPhys.Root);
            _collider = UnityObject.AddComponent<BoxCollider2D>();
            _rigidbody = UnityObject.AddComponent<Rigidbody2D>();
            _rigidbody.isKinematic = true;
            _collider.isTrigger = true;
            _collider.size = Vector2.one * 0.66f;
            UnityObject.AddComponent<PlayerProxy>().AttachedPlayer = this;
            Debug.Log($"Player joined: {Name.Value}");
        }

        protected override void OnLateConstructed()
        {
            SetActive(IsSyncGroupEnabled(SyncGroup.SyncGroup1));
        }

        protected override void OnSyncGroupsChanged(SyncGroup enabledGroups)
        {
            //Debug.Log($"SyncChanged: {this} - {enabledGroups}");
            base.OnSyncGroupsChanged(enabledGroups);

            bool enabled = enabledGroups.HasFlagFast(SyncGroup.SyncGroup1);
            SetActive(enabled);
        }

        private void SetActive(bool active)
        {
            if (UnityObject == null)
                return;
            if (active)
                UnityObject.transform.position = _position.Value;
            UnityObject.SetActive(active);
            View?.SetActive(active);
        }

        protected override void OnLagCompensationStart()
        {
            if (_rigidbody != null)
                _rigidbody.position = _position;
        }
        
        protected override void OnLagCompensationEnd()
        {
            if(_rigidbody != null)
                _rigidbody.position = _position;
        }

        protected override void OnDestroy()
        {
            if (EntityManager.IsClient)
                ClientLogic.Instance.SpawnHit(_position);
            Object.Destroy(UnityObject);
            _rigidbody = null;
            _collider = null;
        }

        public void Spawn(Vector2 position)
        {
            _position.Value = position;
            _rotation.Value = Random.Range(0f, 180f);
            _targetRotation = _rotation.Value;
            _health.Value = 100;
        }

        public struct HitPacket
        {
            public Vector2 Position;
        }

        public void Damage(byte damage)
        {
            if (damage > _health)
                _health.Value = 0;
            else
                _health.Value -= damage;
            
            if (EntityManager.IsServer && _health == 0)
                Destroy();
        }

        private void OnHit(HitPacket p)
        {
            ClientLogic.Instance.SpawnHit(p.Position);
        }
        
        private void OnShoot(ShootPacket p)
        {
            ClientLogic.Instance.SpawnShoot(p.Origin, p.Hit);
        }

        public void SetInput(bool isFiring, bool isProjectileFiring, float rotation, Vector2 velocity)
        {
            _targetRotation = rotation;
            _velocity = velocity.normalized * _speed;
            _isFiring = isFiring;
            _projectileFire = isProjectileFiring;
        }

        protected override void Update()
        {
            base.Update();
            _rotation.Value = _targetRotation;
            _position.Value += _velocity * EntityManager.DeltaTimeF;
            _shootTimer.Update(EntityManager.DeltaTimeF);
            if (_shootTimer.IsTimeElapsed && (_isFiring || _projectileFire))
            {
                _shootTimer.Reset();
                if(_isFiring)
                {
                    //shoot raycast
                    const float MaxLength = 20f;
            
                    Vector2 dir = new Vector2(Mathf.Cos(_rotation.Value * Mathf.Deg2Rad), Mathf.Sin(_rotation.Value * Mathf.Deg2Rad));
            
                    EnableLagCompensationForOwner();
                    int hitsCount = _unityPhys.PhysicsScene.Raycast(_position, dir, MaxLength, RaycastHits);
                    DisableLagCompensationForOwner();
            
                    for (int i = 0; i < hitsCount; i++)
                    {
                        ref var hit = ref RaycastHits[i];
                        if (hit.transform.TryGetComponent<PlayerProxy>(out var playerProxy) && playerProxy.AttachedPlayer != this )
                        {
                            if (EntityManager.InNormalState)
                                ExecuteRPC(_hitRemoteCall, new HitPacket { Position = hit.point });
                            playerProxy.AttachedPlayer.Damage(25);
                        }
                    }
            
                    if(EntityManager.InNormalState)
                        ExecuteRPC(_shootRemoteCall, new ShootPacket
                        {
                            Origin = _position,
                            Hit = _position + dir * MaxLength
                        });
                }
                else if(EntityManager.InNormalState)
                {
                    //shoot projectile
                    var initParams = new ProjectileInitParams
                    {
                        Player = this,
                        Position = _position,
                        Speed = new Vector2(Mathf.Cos(_rotation.Value * Mathf.Deg2Rad), Mathf.Sin(_rotation.Value * Mathf.Deg2Rad)) * 10f
                    };
                    AddPredictedEntity<SimpleProjectile>(initParams.Init);
                }
            }
        }
    }
}

