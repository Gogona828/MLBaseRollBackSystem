using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class UdpP2PTransport : MonoBehaviour
{
    private const int SerializedNetworkPacketSize = 14;

    [Header("Connection")]
    [SerializeField] private string remoteIp = "127.0.0.1";
    [SerializeField] private int localPort = 5000;
    [SerializeField] private int remotePort = 5001;
    [SerializeField] private bool autoStartOnAwake = true;

    [Header("Relay Compatibility")]
    [Tooltip("Usually false for the current DelayRelayServer. The server registers clients from normal binary packets.")]
    [SerializeField] private bool sendTextRegisterToRelay = false;
    [SerializeField] private string textRegisterMessage = "REGISTER";

    private UdpClient socket;
    private IPEndPoint remoteEndPoint;
    private bool started = false;

    private readonly ConcurrentQueue<NetworkPacket> receivedPackets = new ConcurrentQueue<NetworkPacket>();

    private readonly ConcurrentQueue<byte[]> controlPackets = new ConcurrentQueue<byte[]>();
    public bool TryDequeueControl(out byte[] payload) => controlPackets.TryDequeue(out payload);
    public void SendControl(byte[] payload)
    {
        if (!started || payload == null || payload.Length > 4096) return;
        byte[] data = new byte[payload.Length + 4];
        data[0]=82; data[1]=82; data[2]=65; data[3]=49;
        Buffer.BlockCopy(payload,0,data,4,payload.Length);
        try { socket.Send(data,data.Length,remoteEndPoint); }
        catch(SocketException e) { Debug.LogWarning("[LAN] Control send failed: "+e.Message); }
    }

    public bool IsStarted => started;

    public void Configure(string remoteIp, int localPort, int remotePort)
    {
        this.remoteIp = remoteIp;
        this.localPort = localPort;
        this.remotePort = remotePort;
    }

    private void Awake()
    {
        if (autoStartOnAwake)
        {
            StartTransport();
        }
    }

    public void StartTransport()
    {
        if (started)
        {
            Debug.Log("[UdpP2PTransport] Already started");
            return;
        }

        try
        {
            remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteIp), remotePort);

            // Important:
            // Use one UDP socket for both Send and Receive, bound to localPort.
            // DelayRelayServer registers clients by the UDP source endpoint.
            // If Send uses a separate unbound socket, the server replies to that temporary source port,
            // but Unity is receiving on localPort, so Hello/Ready/Start/Input never come back to Unity.
            socket = new UdpClient(localPort);

            started = true;
            BeginReceive();

            if (sendTextRegisterToRelay)
            {
                SendTextRegisterToRelay();
            }

            Debug.Log($"[UdpP2PTransport] Started local={localPort}, remote={remoteIp}:{remotePort}");
            FileLogger.WriteLine($"[UdpP2PTransport] Started local={localPort}, remote={remoteIp}:{remotePort}");
        }
        catch (Exception e)
        {
            started = false; socket?.Close(); socket = null;
            Debug.LogError($"[UdpP2PTransport] StartTransport failed: {e}");
            FileLogger.WriteLine($"[UdpP2PTransport] StartTransport failed: {e}");
        }
    }

    public void StopTransport()
    {
        if (!started)
        {
            return;
        }

        started = false;

        try
        {
            socket?.Close();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UdpP2PTransport] socket close failed: {e.Message}");
        }

        socket = null;
        remoteEndPoint = null;
        while(receivedPackets.TryDequeue(out _)) {}
        while(controlPackets.TryDequeue(out _)) {}

        Debug.Log("[UdpP2PTransport] Stopped");
        FileLogger.WriteLine("[UdpP2PTransport] Stopped");
    }

    public void Send(NetworkPacket packet)
    {
        if (!started)
        {
            Debug.LogWarning("[UdpP2PTransport] Send skipped because transport is not started");
            return;
        }

        if (socket == null || remoteEndPoint == null)
        {
            Debug.LogWarning("[UdpP2PTransport] Send skipped because socket or remoteEndPoint is null");
            return;
        }

        try
        {
            byte[] bytes = NetworkPacketSerializer.Serialize(packet);
            socket.Send(bytes, bytes.Length, remoteEndPoint);
            FileLogger.WriteLine($"[UdpP2PTransport] Sent {packet.packetType} to {remoteIp}:{remotePort} from localPort={localPort} player={packet.playerId} frame={packet.frame}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UdpP2PTransport] Send failed: {e}");
            FileLogger.WriteLine($"[UdpP2PTransport] Send failed: {e}");
        }
    }

    public bool TryDequeue(out NetworkPacket packet)
    {
        return receivedPackets.TryDequeue(out packet);
    }

    private void SendTextRegisterToRelay()
    {
        if (!started || socket == null || remoteEndPoint == null)
        {
            return;
        }

        try
        {
            string message = string.IsNullOrWhiteSpace(textRegisterMessage) ? "REGISTER" : textRegisterMessage.Trim();
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            socket.Send(bytes, bytes.Length, remoteEndPoint);
            FileLogger.WriteLine($"[UdpP2PTransport] Sent text register '{message}' to {remoteIp}:{remotePort} from localPort={localPort}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UdpP2PTransport] Text register failed: {e.Message}");
            FileLogger.WriteLine($"[UdpP2PTransport] Text register failed: {e.Message}");
        }
    }

    private void BeginReceive()
    {
        if (!started || socket == null)
        {
            return;
        }

        try
        {
            socket.BeginReceive(OnReceive, socket);
        }
        catch (Exception e)
        {
            if (started)
            {
                Debug.LogError($"[UdpP2PTransport] BeginReceive failed: {e}");
                FileLogger.WriteLine($"[UdpP2PTransport] BeginReceive failed: {e}");
            }
        }
    }

    private void OnReceive(IAsyncResult ar)
    {
        if (!started || socket == null)
        {
            return;
        }

        try
        {
            IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
            var receivingSocket = (UdpClient)ar.AsyncState;
            byte[] data = receivingSocket.EndReceive(ar, ref any);
            if (receivingSocket != socket || !any.Equals(remoteEndPoint)) return;
            if (data.Length >= 4 && data.Length <= 4100 && data[0]==82 && data[1]==82 && data[2]==65 && data[3]==49)
            {
                var payload = new byte[data.Length-4]; Buffer.BlockCopy(data,4,payload,0,payload.Length);
                controlPackets.Enqueue(payload); return;
            }

            if (data.Length != SerializedNetworkPacketSize)
            {
                string text = Encoding.UTF8.GetString(data).Trim();
                FileLogger.WriteLine($"[UdpP2PTransport] Ignored non-game UDP packet from {any.Address}:{any.Port} len={data.Length} text='{text}'");
                return;
            }

            NetworkPacket packet = NetworkPacketSerializer.Deserialize(data);

            if (!Enum.IsDefined(typeof(NetworkPacketType), packet.packetType))
            {
                FileLogger.WriteLine($"[UdpP2PTransport] Ignored unknown packetType={(byte)packet.packetType} from {any.Address}:{any.Port}");
                return;
            }

            receivedPackets.Enqueue(packet);
            FileLogger.WriteLine($"[UdpP2PTransport] Received {packet.packetType} from {any.Address}:{any.Port} player={packet.playerId} frame={packet.frame}");
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception e)
        {
            if (started)
            {
                Debug.LogError($"[UdpP2PTransport] Receive failed: {e}");
                FileLogger.WriteLine($"[UdpP2PTransport] Receive failed: {e}");
            }
        }
        finally
        {
            if (started && ReferenceEquals(ar.AsyncState, socket))
            {
                BeginReceive();
            }
        }
    }

    private void OnDestroy()
    {
        StopTransport();
    }
}
