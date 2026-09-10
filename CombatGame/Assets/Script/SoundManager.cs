using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Footsies
{

    public class SoundManager : Singleton<SoundManager>
    {

        public GameObject seSourceObject1;
        public GameObject seSourceObject2;
        public GameObject bgmSourceObject;

        [Range(0.0f, 1.0f)]
        public float masterVolume = 1f;

        private AudioSource seSource1;
        private AudioSource seSource2;
        private AudioSource bgmSource;

        private float defaultBGMVolume;
        public bool isBGMOn { get; private set; }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning($"[SoundManager] Duplicate SoundManager detected on '{gameObject.name}'. Destroying duplicate GameObject.");
                Destroy(gameObject);
                return;
            }

            _instance = this;

            if (transform.parent != null)
            {
                transform.SetParent(null);
            }
            DontDestroyOnLoad(gameObject);

            InitializeAudioSources();
        }

        private void InitializeAudioSources()
        {
            seSource1 = ResolveSource(ref seSourceObject1, "SE_AudioSource1");
            seSource2 = ResolveSource(ref seSourceObject2, "SE_AudioSource2");
            bgmSource = ResolveSource(ref bgmSourceObject, "BGM_AudioSource");

            if (bgmSource != null)
            {
                defaultBGMVolume = bgmSource.volume;
                isBGMOn = true;
            }
        }

        private AudioSource ResolveSource(ref GameObject sourceObject, string defaultName)
        {
            if (sourceObject != null)
            {
                var source = sourceObject.GetComponent<AudioSource>();
                if (source != null) return source;
            }

            Transform child = transform.Find(defaultName);
            if (child != null)
            {
                sourceObject = child.gameObject;
                var source = child.GetComponent<AudioSource>();
                if (source != null) return source;
            }

            // フォールバック: 子オブジェクトを新規作成して AudioSource をアタッチ
            GameObject fallbackObj = new GameObject(defaultName);
            fallbackObj.transform.SetParent(transform);
            sourceObject = fallbackObj;
            AudioSource newSource = fallbackObj.AddComponent<AudioSource>();
            newSource.playOnAwake = false;
            return newSource;
        }

        // Update is called once per frame
        void Update()
        {

        }

        public bool toggleBGM()
        {
            if (bgmSource == null) return false;

            if (isBGMOn)
            {
                bgmSource.volume = 0;
                isBGMOn = false;
            }
            else
            {
                bgmSource.volume = defaultBGMVolume;
                isBGMOn = true;
            }

            return isBGMOn;
        }

        public void playSE(AudioClip clip)
        {
            if (clip == null) return;

            if (seSource1 == null)
            {
                InitializeAudioSources();
            }

            if (seSource1 != null)
            {
                seSource1.clip = clip;
                seSource1.panStereo = 0;
                seSource1.Play();
            }
        }

        public void playFighterSE(AudioClip clip, bool isPlayerOne, float posX)
        {
            if (clip == null) return;

            var audioSource = isPlayerOne ? seSource1 : seSource2;
            if (audioSource == null)
            {
                InitializeAudioSources();
                audioSource = isPlayerOne ? seSource1 : seSource2;
            }

            if (audioSource != null)
            {
                audioSource.clip = clip;
                audioSource.panStereo = Mathf.Clamp(posX / 5f, -1f, 1f);
                audioSource.Play();
            }
        }
    }

}