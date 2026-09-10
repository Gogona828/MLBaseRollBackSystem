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
            LocalVsCPU,
        }

        public enum SceneIndex
        {
            Title = 1,
            Battle = 2,
        }

        public AudioClip menuSelectAudioClip;

        public SceneIndex currentScene { get; private set; }
        public BattleMode battleMode { get; private set; } = BattleMode.OnlineVsPlayer;
        public bool isVsCPU { get { return battleMode == BattleMode.LocalVsCPU; } }
        public bool isOfflineMode { get { return false; } }
        public int CpuMatchPort => LocalCpuMatch.IsCpuClient ? LocalCpuMatch.CpuRelayPort : cpuMatch.Port;
        public string CpuMatchAddress => LocalCpuMatch.IsCpuClient ? LocalCpuMatch.CpuRelayAddress : cpuMatch.Address;
        private LocalCpuMatch cpuMatch;
        private string cpuError;
        private float cpuStartTime;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this.gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(this.gameObject);

            Application.targetFrameRate = 60;
        }

        private void Start()
        {
            if (LocalCpuMatch.IsCpuClient)
            {
                battleMode = BattleMode.LocalVsCPU;
                Application.runInBackground = true;
                LoadBattleScene();
            }
            else LoadTitleScene();
        }

        private void Update()
        {
            if (LocalCpuMatch.IsCpuClient && !LocalCpuMatch.ParentIsAlive()) { Application.Quit(); return; }
            if (cpuMatch != null && (cpuMatch.HasExited ||
                (Time.realtimeSinceStartup - cpuStartTime > 45f && currentScene == SceneIndex.Battle &&
                 FindObjectOfType<NetworkSessionManager>()?.Running != true)))
            {
                cpuError = "CPUクライアントに接続できませんでした。Python 3.10以上とCPU Clientのビルド、ログを確認してください。";
                LoadTitleScene();
            }
            if(currentScene == SceneIndex.Battle && !LocalCpuMatch.IsCpuClient)
            {
                if(Input.GetButtonDown("Cancel"))
                {
                    LoadTitleScene();
                }
            }
        }

        public void LoadTitleScene()
        {
            if (LocalCpuMatch.IsCpuClient) { Application.Quit(); return; }
            cpuMatch?.Dispose();
            cpuMatch = null;
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
            if (LocalCpuMatch.IsCpuClient || cpuMatch != null) return;
            cpuError = null;
            cpuMatch = new LocalCpuMatch();
            try
            {
                cpuMatch.Start();
                cpuStartTime = Time.realtimeSinceStartup;
                battleMode = BattleMode.LocalVsCPU;
                LoadBattleScene();
            }
            catch (System.Exception ex)
            {
                cpuMatch.Dispose(); cpuMatch = null;
                cpuError = ex.Message;
                Debug.LogException(ex);
            }
        }

        public new void OnDestroy() { cpuMatch?.Dispose(); base.OnDestroy(); }
        private void OnApplicationQuit() { cpuMatch?.Dispose(); }
        private void OnGUI()
        {
            if (string.IsNullOrEmpty(cpuError)) return;
            GUI.Box(new Rect(20, 20, Screen.width - 40, 100), cpuError);
            if (GUI.Button(new Rect(30, 85, 100, 25), "閉じる")) cpuError = null;
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
