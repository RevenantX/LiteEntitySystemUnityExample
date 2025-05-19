using LiteEntitySystem;
using UnityEngine;

namespace Code.Shared
{
    public class BasePlayerController : HumanControllerLogic<PlayerInputPacket, BasePlayer>
    {
        private readonly Camera _mainCamera;
        
        public BasePlayerController(EntityParams entityParams) : base(entityParams)
        {
            _mainCamera = Camera.main;
        }

        protected override void Update()
        {
            base.Update();
            if (ControlledEntity == null)
                return;
            const float maxPlayerDistance = 35f;
            if (EntityManager.IsServer)
            {
                foreach (var otherPlayer in EntityManager.GetEntities<BasePlayer>())
                    ServerManager.ToggleSyncGroup(
                        OwnerId, 
                        otherPlayer, 
                        SyncGroup.SyncGroup1,
                        (otherPlayer.Position - ControlledEntity.Position).sqrMagnitude < maxPlayerDistance * maxPlayerDistance);
            }
        }

        protected override void VisualUpdate()
        {
            if (ControlledEntity == null)
                return;
            
            //input
            Vector2 velocity = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            Vector2 mousePos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
            Vector2 dir = mousePos - ControlledEntity.Position;
            float rotation = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            
            ref var nextCommand = ref ModifyPendingInput();
            
            if(Input.GetAxis("Fire1") > 0f)
                nextCommand.Keys |= MovementKeys.Fire;
            if (Input.GetMouseButton(1))
                nextCommand.Keys |= MovementKeys.Projectile;
            
            if (velocity.x < -0.5f)
                nextCommand.Keys |= MovementKeys.Left;
            if (velocity.x > 0.5f)
                nextCommand.Keys |= MovementKeys.Right;
            if (velocity.y < -0.5f)
                nextCommand.Keys |= MovementKeys.Up;
            if (velocity.y > 0.5f)
                nextCommand.Keys |= MovementKeys.Down;

            nextCommand.Rotation = rotation;
        }

        protected override void BeforeControlledUpdate()
        {
            base.BeforeControlledUpdate();
            var velocity = Vector2.zero;
            var input = CurrentInput;
            if (input.Keys.HasFlagFast(MovementKeys.Up))
                velocity.y = -1f;
            if (input.Keys.HasFlagFast(MovementKeys.Down))
                velocity.y = 1f;
            
            if (input.Keys.HasFlagFast(MovementKeys.Left))
                velocity.x = -1f;
            if (input.Keys.HasFlagFast(MovementKeys.Right))
                velocity.x = 1f;
            
            ControlledEntity?.SetInput(
                input.Keys.HasFlagFast(MovementKeys.Fire),
                input.Keys.HasFlagFast(MovementKeys.Projectile),
                input.Rotation,
                velocity);
        }
    }
}