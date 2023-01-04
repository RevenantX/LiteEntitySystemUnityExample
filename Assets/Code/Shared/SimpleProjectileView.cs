using UnityEngine;

namespace Code.Shared
{
    public class SimpleProjectileView : MonoBehaviour
    {
        public SimpleProjectile Attached;

        private void Update()
        {
            transform.position = Attached.VisualPostion;
        }
    }
}