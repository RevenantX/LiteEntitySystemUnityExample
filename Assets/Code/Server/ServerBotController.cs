using Code.Shared;
using LiteEntitySystem;
using LiteEntitySystem.Extensions;
using UnityEngine;

namespace Code.Server
{
    public class ServerBotController : AiControllerLogic<BasePlayer>
    {
        private float _rotation;
        private readonly SyncTimer _rotationChangeTimer = new SyncTimer(0.5f);

        public ServerBotController(EntityParams entityParams) : base(entityParams)
        {
            _rotation = Random.Range(0, 360);
        }

        public override void BeforeControlledUpdate()
        {
            if (_rotationChangeTimer.UpdateAndReset(EntityManager.DeltaTimeF))
            {
                _rotation += Random.Range(-30f, 30f);
                _rotationChangeTimer.Reset(Random.Range(0.5f, 3f));
            }
            bool normalFire = Random.Range(0, 50) == 0;
            ControlledEntity.SetInput(
                normalFire,
                false,
                _rotation,
                new Vector2(Mathf.Cos(_rotation * Mathf.Deg2Rad),Mathf.Sin(_rotation * Mathf.Deg2Rad)*0.1f));
        }
    }
}