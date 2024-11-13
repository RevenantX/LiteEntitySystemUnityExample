using System;
using Code.Shared;
using UnityEngine;

namespace Code.Client
{
    public class HitEffect : MonoBehaviour
    {
        [SerializeField] private AudioSource _source;
        [SerializeField] private AudioClip[] _hitClips;
        
        private Action<HitEffect> _onDeathCallback;

        public void Init(Action<HitEffect> onDeathCallback)
        {
            _onDeathCallback = onDeathCallback;
            gameObject.SetActive(false);
        }
        
        public void Spawn(Vector2 from)
        {
            transform.position = from;
            gameObject.SetActive(true);
            _source.PlayOneShot(_hitClips.GetRandomElement());
        }

        private void Update()
        {
            if (!_source.isPlaying)
            {
                gameObject.SetActive(false);
                _onDeathCallback?.Invoke(this);
            }
        }
    }
}