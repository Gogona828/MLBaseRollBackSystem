using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public class UdpP2PTransport : MonoBehaviour
{
    [Header("Connection")]
    [SerializeField] private string remoteIp = "127.0.0.1";
    [SerializeField] private int localPort = 5000;
    [SerializeField] private int remotePort = 5001;
    [SerializeField] private bool autoStartOnAwake = true;

    private UdpClient sender;
    private UdpClient receiver;
    private IPEndPoint remoteEndPoint;
    private bool started = false;

    private readonly ConcurrentQueue<NetworkPacket> receivedPackets = new ConcurrentQueue<NetworkPacket>();

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

            sender = new UdpClient();
            receiver = new UdpClient(localPort);

            started = true;
            BeginReceive();

            Debug.Log($"[UdpP2PTransport] Started local={localPort}, remote={remoteIp}:{remotePort}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UdpP2PTransport] StartTransport failed: {e}");
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
            sender?.Close();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UdpP2PTransport] sender close failed: {e.Message}");
        }

        try
        {
            receiver?.Close();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UdpP2PTransport] receiver close failed: {e.Message}");
        }

        Debug.Log("[UdpP2PTransport] Stopped");
    }

    public void Send(NetworkPacket packet)
    {
        if (!started)
        {
            Debug.LogWarning("[UdpP2PTransport] Send skipped because transport is not started");
            return;
        }

        try
        {
            byte[] bytes = NetworkPacketSerializer.Serialize(packet);
            sender.Send(bytes, bytes.Length, remoteEndPoint);
            FileLogger.WriteLine($"[UdpP2PTransport] Sent {packet.packetType} to {remoteIp}:{remotePort} player={packet.playerId} frame={packet.frame}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UdpP2PTransport] Send failed: {e}");
        }
    }

    public bool TryDequeue(out NetworkPacket packet)
    {
        return receivedPackets.TryDequeue(out packet);
    }

    private void BeginReceive()
    {
        if (!started || receiver == null)
        {
            return;
        }

        try
        {
            receiver.BeginReceive(OnReceive, null);
        }
        catch (Exception e)
        {
            Debug.LogError($"[UdpP2PTransport] BeginReceive failed: {e}");
        }
    }

    private void OnReceive(IAsyncResult ar)
    {
        if (!started || receiver == null)
        {
            return;
        }

        try
        {
            IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
            byte[] data = receiver.EndReceive(ar, ref any);

            NetworkPacket packet = NetworkPacketSerializer.Deserialize(data);
            receivedPackets.Enqueue(packet);

            FileLogger.WriteLine($"[UdpP2PTransport] Received {packet.packetType} from {any.Address}:{any.Port} player={packet.playerId} frame={packet.frame}");
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception e)
        {
            Debug.LogError($"[UdpP2PTransport] Receive failed: {e}");
        }
        finally
        {
            if (started)
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
