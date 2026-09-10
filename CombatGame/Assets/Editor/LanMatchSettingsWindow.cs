using System.Net;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;

public class LanMatchSettingsWindow : EditorWindow
{
    private string address;
    private int port;
    private bool cpuExternalRelay;
    private int cpuDelayMs;
    [MenuItem("CombatGame/LAN Match Settings")]
    public static void Open() { GetWindow<LanMatchSettingsWindow>("LAN Match"); }
    private void OnEnable()
    {
        address=PlayerPrefs.GetString("CombatGame.LAN.IP", "127.0.0.1");
        port=PlayerPrefs.GetInt("CombatGame.LAN.Port", 6000);
        cpuExternalRelay=PlayerPrefs.GetInt("CombatGame.CPU.UseExternalRelay",0)==1;
        cpuDelayMs=PlayerPrefs.GetInt("CombatGame.CPU.DelayMs",100);
    }
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Mac LAN relay / FEP supervised", EditorStyles.boldLabel);
        address=EditorGUILayout.TextField("Mac server IPv4",address);
        port=EditorGUILayout.IntField("Server UDP port",port);
        EditorGUILayout.HelpBox("P1: 6000 / P2: 6001 (server base port + 1). Both PCs use the Mac's LAN IPv4. Local UDP port is automatic. Controls: A / D / Space. Save before Play; open the battle scene or select online VS.",MessageType.Info);
        bool valid=IPAddress.TryParse(address,out var ip) && ip.AddressFamily==AddressFamily.InterNetwork && port>=1024 && port<=65535;
        using(new EditorGUI.DisabledScope(!valid || EditorApplication.isPlaying))
        if(GUILayout.Button("Save and enable LAN + FEP"))
        {
            PlayerPrefs.SetString("CombatGame.LAN.IP",address.Trim());
            PlayerPrefs.SetInt("CombatGame.LAN.Port",port);
            PlayerPrefs.SetInt("CombatGame.LAN.Enabled",1); PlayerPrefs.Save();
            Debug.Log("LAN + FEP enabled. Player="+(port%2+1)+" relay="+address+":"+port);
        }
        if(GUILayout.Button("Use existing scene profiles")){PlayerPrefs.SetInt("CombatGame.LAN.Enabled",0);PlayerPrefs.Save();}
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("VS CPU relay", EditorStyles.boldLabel);
        cpuExternalRelay=EditorGUILayout.Toggle("Use external LAN relay",cpuExternalRelay);
        if (cpuExternalRelay)
            EditorGUILayout.HelpBox("VS CPU connects both clients to the server above (even port = P1, next port = P2). Start that server yourself; its --delay and other arguments apply. No dedicated relay is started.", MessageType.Info);
        else
        {
            cpuDelayMs=EditorGUILayout.IntField("Dedicated relay delay (ms)",cpuDelayMs);
            EditorGUILayout.HelpBox("VS CPU starts a dedicated localhost relay. A separately started server does not affect this mode. Default: 100 ms every 8–10 seconds.", MessageType.Info);
        }
        using(new EditorGUI.DisabledScope(EditorApplication.isPlaying || (cpuExternalRelay ? !valid : cpuDelayMs<0 || cpuDelayMs>10000)))
        if(GUILayout.Button("Save VS CPU relay settings"))
        {
            PlayerPrefs.SetInt("CombatGame.CPU.UseExternalRelay",cpuExternalRelay ? 1 : 0);
            PlayerPrefs.SetInt("CombatGame.CPU.DelayMs",cpuDelayMs);
            if(cpuExternalRelay)
            {
                PlayerPrefs.SetString("CombatGame.LAN.IP",address.Trim());
                PlayerPrefs.SetInt("CombatGame.LAN.Port",port);
            }
            PlayerPrefs.Save();
        }
    }
}
