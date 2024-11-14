using Code.Shared;
using UnityEngine;

namespace Code.Client
{
    public class ClientPlayerView : MonoBehaviour
    {
        [SerializeField] private TextMesh _name;
        private BasePlayerController _playerController;
        private Transform _mainCamera;

        private Vector3 _currentDampVelocity;

        private void Awake()
        {
            _mainCamera = Camera.main!.transform;
        }
        
        public static ClientPlayerView Create(ClientPlayerView prefab, BasePlayerController playerController)
        {
            var player = playerController.ControlledEntity;
            var obj = Instantiate(prefab, player.UnityObject.transform);
            obj._playerController = playerController;
            obj._name.text = player.Name;
            return obj;
        }

        private void LateUpdate()
        {
            if (_playerController.ControlledEntity != null)
            {
                Vector2 pos = _playerController.ControlledEntity.Position;
                _mainCamera.position = Vector3.SmoothDamp(_mainCamera.position, new Vector3(pos.x, pos.y, _mainCamera.position.z), ref _currentDampVelocity, 0.3f);
            }
        }
    }
}