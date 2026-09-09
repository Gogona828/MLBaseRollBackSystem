using System.Net;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;

public class LanMatchSettingsWindow : EditorWindow
{
    private string address;
    private int port;
    [MenuItem("CombatGame/LAN Match Settings")]
    public static void Open() { GetWindow<LanMatchSettingsWindow>("LAN Match"); }
    private void OnEnable()
    {
        address=PlayerPrefs.GetString("CombatGame.LAN.IP", "127.0.0.1");
        port=PlayerPrefs.GetInt("CombatGame.LAN.Port", 6000);
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
    }
}
