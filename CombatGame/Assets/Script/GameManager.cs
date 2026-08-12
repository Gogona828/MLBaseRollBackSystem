using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Footsies
{
    public class GameManager : Singleton<GameManager>
    {
        public enum BattleMode
        {
            OnlineVsPlayer,
            OfflineVsCPU,
        }

        public enum SceneIndex
        {
            Title = 1,
            Battle = 2,
        }

        public AudioClip menuSelectAudioClip;

        public SceneIndex currentScene { get; private set; }
        public BattleMode battleMode { get; private set; } = BattleMode.OnlineVsPlayer;
        public bool isVsCPU { get { return battleMode == BattleMode.OfflineVsCPU; } }
        public bool isOfflineMode { get { return battleMode == BattleMode.OfflineVsCPU; } }

        private void Awake()
        {
            DontDestroyOnLoad(this.gameObject);

            Application.targetFrameRate = 60;
        }

        private void Start()
        {
            LoadTitleScene();
        }

        private void Update()
        {
            if(currentScene == SceneIndex.Battle)
            {
                if(Input.GetButtonDown("Cancel"))
                {
                    LoadTitleScene();
                }
            }
        }

        public void LoadTitleScene()
        {
            SceneManager.LoadScene((int)SceneIndex.Title);
            currentScene = SceneIndex.Title;
        }

        public void LoadVsPlayerScene()
        {
            battleMode = BattleMode.OnlineVsPlayer;
            LoadBattleScene();
        }

        public void LoadVsCPUScene()
        {
            battleMode = BattleMode.OfflineVsCPU;
            LoadBattleScene();
        }

        private void LoadBattleScene()
        {
            SceneManager.LoadScene((int)SceneIndex.Battle);
            currentScene = SceneIndex.Battle;

            if(menuSelectAudioClip != null)
            {
                SoundManager.Instance.playSE(menuSelectAudioClip);
            }
        }
    }

}
