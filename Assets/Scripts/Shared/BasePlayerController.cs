using LiteEntitySystem;
using UnityEngine;

namespace Code.Shared
{
    public class BasePlayerController : HumanControllerLogic<PlayerInputPacket, BasePlayer>
    {
        private readonly Camera _mainCamera;
        private PlayerInputPacket _nextCommand;
        
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
            foreach (var otherPlayer in EntityManager.GetEntities<BasePlayer>())
            {
                ChangeEntityDiffSync(otherPlayer, (otherPlayer.Position - ControlledEntity.Position).sqrMagnitude < maxPlayerDistance * maxPlayerDistance);
            }
        }

        protected override void OnEntityDiffSyncChanged(EntityLogic entity, bool enabled)
        {
            if (entity is BasePlayer bp)
                bp.SetActive(enabled);
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
            
            if(Input.GetAxis("Fire1") > 0f)
                _nextCommand.Keys |= MovementKeys.Fire;
            if (Input.GetMouseButton(1))
                _nextCommand.Keys |= MovementKeys.Projectile;
            
            if (velocity.x < -0.5f)
                _nextCommand.Keys |= MovementKeys.Left;
            if (velocity.x > 0.5f)
                _nextCommand.Keys |= MovementKeys.Right;
            if (velocity.y < -0.5f)
                _nextCommand.Keys |= MovementKeys.Up;
            if (velocity.y > 0.5f)
                _nextCommand.Keys |= MovementKeys.Down;

            _nextCommand.Rotation = rotation;
        }

        protected override void ReadInput(in PlayerInputPacket input)
        {
            var velocity = Vector2.zero;

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

        protected override void GenerateInput(out PlayerInputPacket input)
        {
            input = _nextCommand;
            _nextCommand.Keys = 0;
        }
    }
}