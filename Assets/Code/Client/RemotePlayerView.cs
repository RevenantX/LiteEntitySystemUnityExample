using Code.Shared;
using UnityEngine;

namespace Code.Client
{
    public class RemotePlayerView : MonoBehaviour
    {
        private TextMesh _health;
        private BasePlayer _player;

        public static RemotePlayerView Create(RemotePlayerView prefab, BasePlayer player)
        {
            var obj = Instantiate(prefab, player.UnityObject.transform);
            obj._player = player;
            var textObj = new GameObject($"text_{player.Name}_{player.Id}");
            obj._health = textObj.AddComponent<TextMesh>();
            obj._health.characterSize = 0.3f;
            obj._health.anchor = TextAnchor.MiddleCenter;
            return obj;
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                Destroy(_health.gameObject);
                _health = null;
            }
        }

        private void Update()
        {
            _health.transform.position = _player.Position;
            _health.text = _player.Health.ToString();
        }
    }
}