using LiteEntitySystem;
using UnityEngine;

namespace Code.Shared
{
    [EntityFlags(EntityFlags.UpdateOnClient)]
    public class UnityPhysicsManager : SingletonEntityLogic
    {
        public PhysicsScene2D PhysicsScene { get; private set; }
        public Transform Root { get; private set; }

        public UnityPhysicsManager Init(Transform root)
        {
            Root = root;
            PhysicsScene = root.gameObject.scene.GetPhysicsScene2D();
            return this;
        }
        
        public UnityPhysicsManager(EntityParams entityParams) : base(entityParams)
        {
            Physics.simulationMode = SimulationMode.Script;
        }

        protected override void Update()
        {
            PhysicsScene.Simulate(EntityManager.DeltaTimeF);
        }
    }
}