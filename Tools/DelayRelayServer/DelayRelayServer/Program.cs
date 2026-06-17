using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    private const int DefaultListenPort = 6000;
    private const int DefaultDelayMilliseconds = 100;
    private const int DefaultJitterMilliseconds = 0;
    private const double DefaultPacketLossPercent = 0.0;
    private const int SerializedNetworkPacketSize = 14;

    private static readonly object EndpointGate = new object();
    private static readonly object LogGate = new object();
    private static readonly SemaphoreSlim SendGate = new SemaphoreSlim(1, 1);

    private static IPEndPoint? player0Endpoint;
    private static IPEndPoint? player1Endpoint;
    private static StreamWriter? logWriter;

    static async Task Main(string[] args)
    {
        if (ContainsHelpOption(args))
        {
            PrintUsage();
            return;
        }

        ServerSettings settings = ParseSettings(args);
        NetworkCondition networkCondition = new NetworkCondition(
            settings.DelayMilliseconds,
            settings.JitterMilliseconds,
            settings.PacketLossPercent
        );

        Directory.CreateDirectory("logs");

        string logPath = Path.Combine(
            "logs",
            $"relay_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        );

        logWriter = new StreamWriter(logPath, append: false, Encoding.UTF8)
        {
            AutoFlush = true
        };

        logWriter.WriteLine(
            "event,time_utc_ms,from_ip,from_port,to_ip,to_port,packet_type,player_id,frame,input_bits,start_delay_frames,base_delay_ms,jitter_ms,packet_loss_percent,applied_delay_ms,message"
        );

        using UdpClient udp = new UdpClient(settings.ListenPort);

        Console.WriteLine("[DelayServer] Started");
        Console.WriteLine($"[DelayServer] listenPort={settings.ListenPort}");
        Console.WriteLine($"[DelayServer] baseDelayMilliseconds={networkCondition.BaseDelayMilliseconds}");
        Console.WriteLine($"[DelayServer] jitterMilliseconds={networkCondition.JitterMilliseconds}");
        Console.WriteLine($"[DelayServer] packetLossPercent={networkCondition.PacketLossPercent.ToString("0.###", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"[DelayServer] logFile={logPath}");
        Console.WriteLine("[DelayServer] positional args: ./DelayRelayServer [listenPort] [delayMilliseconds] [jitterMilliseconds] [packetLossPercent]");
        Console.WriteLine("[DelayServer] named args:      ./DelayRelayServer --port 6000 --delay 100 --jitter 20 --loss 5");

        while (true)
        {
            UdpReceiveResult result = await udp.ReceiveAsync();
            byte[] data = result.Buffer;
            IPEndPoint from = CopyEndpoint(result.RemoteEndPoint);

            if (IsTextRegisterPacket(data))
            {
                WriteLog(
                    "TEXT_REGISTER_IGNORED",
                    from,
                    null,
                    "TextRegister",
                    -1,
                    -1,
                    0,
                    0,
                    networkCondition,
                    null,
                    "Text REGISTER received. This server registers player slots from binary NetworkPacket.playerId."
                );
                await SendTextAsync(udp, from, "REGISTERED");
                continue;
            }

            if (!TryParsePacket(data, out ParsedPacket packet))
            {
                WriteLog(
                    "INVALID",
                    from,
                    null,
                    "Unknown",
                    -1,
                    -1,
                    0,
                    0,
                    networkCondition,
                    null,
                    $"Invalid packet length={data.Length}. Expected {SerializedNetworkPacketSize}."
                );
                continue;
            }

            if (packet.PlayerId != 0 && packet.PlayerId != 1)
            {
                WriteLog(
                    "INVALID_PLAYER",
                    from,
                    null,
                    packet.PacketTypeName,
                    packet.PlayerId,
                    packet.Frame,
                    packet.InputBits,
                    packet.StartDelayFrames,
                    networkCondition,
                    null,
                    "playerId must be 0 or 1"
                );
                continue;
            }

            RegisterPlayerEndpoint(packet.PlayerId, from, networkCondition);
            IPEndPoint? target = ResolveTarget(packet.PlayerId);

            WriteLog(
                "RECV",
                from,
                target,
                packet.PacketTypeName,
                packet.PlayerId,
                packet.Frame,
                packet.InputBits,
                packet.StartDelayFrames,
                networkCondition,
                null,
                "received"
            );

            if (target == null)
            {
                WriteLog(
                    "WAIT",
                    from,
                    null,
                    packet.PacketTypeName,
                    packet.PlayerId,
                    packet.Frame,
                    packet.InputBits,
                    packet.StartDelayFrames,
                    networkCondition,
                    null,
                    "target player not registered yet"
                );
                continue;
            }

            _ = RelayAfterDelayAsync(
                udp,
                data,
                from,
                target,
                packet,
                networkCondition
            );
        }
    }

    private static ServerSettings ParseSettings(string[] args)
    {
        List<string> positionalArgs = new List<string>();
        Dictionary<string, string> namedArgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positionalArgs.Add(arg);
                continue;
            }

            string option = arg.Substring(2);
            string value = "true";
            int equalIndex = option.IndexOf('=');

            if (equalIndex >= 0)
            {
                value = option.Substring(equalIndex + 1);
                option = option.Substring(0, equalIndex);
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            namedArgs[NormalizeOptionName(option)] = value;
        }

        int listenPort = DefaultListenPort;
        int delayMilliseconds = DefaultDelayMilliseconds;
        int jitterMilliseconds = DefaultJitterMilliseconds;
        double packetLossPercent = DefaultPacketLossPercent;

        if (positionalArgs.Count >= 1 && int.TryParse(positionalArgs[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort))
        {
            listenPort = parsedPort;
        }

        if (positionalArgs.Count >= 2 && int.TryParse(positionalArgs[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDelay))
        {
            delayMilliseconds = parsedDelay;
        }

        if (positionalArgs.Count >= 3 && int.TryParse(positionalArgs[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedJitter))
        {
            jitterMilliseconds = parsedJitter;
        }

        if (positionalArgs.Count >= 4 && double.TryParse(positionalArgs[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedLoss))
        {
            packetLossPercent = parsedLoss;
        }

        if (TryGetNamedInt(namedArgs, "port", out int namedPort))
        {
            listenPort = namedPort;
        }

        if (TryGetNamedInt(namedArgs, "delay", out int namedDelay))
        {
            delayMilliseconds = namedDelay;
        }

        if (TryGetNamedInt(namedArgs, "jitter", out int namedJitter))
        {
            jitterMilliseconds = namedJitter;
        }

        if (TryGetNamedDouble(namedArgs, "loss", out double namedLoss))
        {
            packetLossPercent = namedLoss;
        }

        listenPort = Clamp(listenPort, 1, 65535);
        delayMilliseconds = Math.Max(0, delayMilliseconds);
        jitterMilliseconds = Math.Max(0, jitterMilliseconds);
        packetLossPercent = Clamp(packetLossPercent, 0.0, 100.0);

        return new ServerSettings(
            listenPort,
            delayMilliseconds,
            jitterMilliseconds,
            packetLossPercent
        );
    }

    private static string NormalizeOptionName(string optionName)
    {
        return optionName.Replace("-", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant() switch
        {
            "listenport" => "port",
            "port" => "port",
            "delaymilliseconds" => "delay",
            "delayms" => "delay",
            "basedelay" => "delay",
            "basedelaymilliseconds" => "delay",
            "delay" => "delay",
            "jittermilliseconds" => "jitter",
            "jitterms" => "jitter",
            "jitter" => "jitter",
            "packetloss" => "loss",
            "packetlosspercent" => "loss",
            "lossrate" => "loss",
            "loss" => "loss",
            _ => optionName
        };
    }

    private static bool TryGetNamedInt(Dictionary<string, string> namedArgs, string key, out int value)
    {
        value = 0;
        return namedArgs.TryGetValue(key, out string? text)
               && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetNamedDouble(Dictionary<string, string> namedArgs, string key, out double value)
    {
        value = 0.0;
        return namedArgs.TryGetValue(key, out string? text)
               && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private static bool ContainsHelpOption(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.Equals("-h", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("/h", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("/?", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("DelayRelayServer");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  ./DelayRelayServer [listenPort] [delayMilliseconds] [jitterMilliseconds] [packetLossPercent]");
        Console.WriteLine("  ./DelayRelayServer --port 6000 --delay 100 --jitter 20 --loss 5");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  listenPort / --port              UDP listen port. Default: 6000");
        Console.WriteLine("  delayMilliseconds / --delay      Base one-way relay delay in ms. Default: 100");
        Console.WriteLine("  jitterMilliseconds / --jitter    Random +/- jitter in ms. Default: 0");
        Console.WriteLine("  packetLossPercent / --loss       Packet loss rate in percent, 0-100. Default: 0");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  ./DelayRelayServer 6000 100 20 5");
        Console.WriteLine("  -> delay is randomized to 80-120ms, and 5% of packets are dropped.");
    }

    private static void RegisterPlayerEndpoint(int playerId, IPEndPoint endpoint, NetworkCondition networkCondition)
    {
        lock (EndpointGate)
        {
            if (playerId == 0)
            {
                if (player0Endpoint == null || !SameEndpoint(player0Endpoint, endpoint))
                {
                    player0Endpoint = CopyEndpoint(endpoint);
                    WriteLog("REGISTER", endpoint, null, "Unknown", playerId, -1, 0, 0, networkCondition, null, "registered player0");
                }
            }
            else if (playerId == 1)
            {
                if (player1Endpoint == null || !SameEndpoint(player1Endpoint, endpoint))
                {
                    player1Endpoint = CopyEndpoint(endpoint);
                    WriteLog("REGISTER", endpoint, null, "Unknown", playerId, -1, 0, 0, networkCondition, null, "registered player1");
                }
            }
        }
    }

    private static IPEndPoint? ResolveTarget(int fromPlayerId)
    {
        lock (EndpointGate)
        {
            return fromPlayerId switch
            {
                0 => player1Endpoint == null ? null : CopyEndpoint(player1Endpoint),
                1 => player0Endpoint == null ? null : CopyEndpoint(player0Endpoint),
                _ => null
            };
        }
    }

    private static async Task RelayAfterDelayAsync(
        UdpClient udp,
        byte[] payload,
        IPEndPoint from,
        IPEndPoint target,
        ParsedPacket packet,
        NetworkCondition networkCondition)
    {
        if (ShouldDropPacket(networkCondition.PacketLossPercent))
        {
            WriteLog(
                "DROP",
                from,
                target,
                packet.PacketTypeName,
                packet.PlayerId,
                packet.Frame,
                packet.InputBits,
                packet.StartDelayFrames,
                networkCondition,
                0,
                $"dropped by packetLossPercent={networkCondition.PacketLossPercent.ToString("0.###", CultureInfo.InvariantCulture)}"
            );
            return;
        }

        int appliedDelayMilliseconds = CalculateAppliedDelayMilliseconds(networkCondition);

        if (appliedDelayMilliseconds > 0)
        {
            await Task.Delay(appliedDelayMilliseconds);
        }

        await SendGate.WaitAsync();
        try
        {
            await udp.SendAsync(payload, payload.Length, target);
        }
        finally
        {
            SendGate.Release();
        }

        WriteLog(
            "SEND",
            from,
            target,
            packet.PacketTypeName,
            packet.PlayerId,
            packet.Frame,
            packet.InputBits,
            packet.StartDelayFrames,
            networkCondition,
            appliedDelayMilliseconds,
            $"forwarded {payload.Length} bytes"
        );
    }

    private static bool ShouldDropPacket(double packetLossPercent)
    {
        if (packetLossPercent <= 0.0)
        {
            return false;
        }

        if (packetLossPercent >= 100.0)
        {
            return true;
        }

        return Random.Shared.NextDouble() * 100.0 < packetLossPercent;
    }

    private static int CalculateAppliedDelayMilliseconds(NetworkCondition networkCondition)
    {
        int jitterOffsetMilliseconds = 0;

        if (networkCondition.JitterMilliseconds > 0)
        {
            jitterOffsetMilliseconds = Random.Shared.Next(
                -networkCondition.JitterMilliseconds,
                networkCondition.JitterMilliseconds + 1
            );
        }

        return Math.Max(0, networkCondition.BaseDelayMilliseconds + jitterOffsetMilliseconds);
    }

    private static bool TryParsePacket(byte[] data, out ParsedPacket packet)
    {
        packet = default;

        // Unity NetworkPacketSerializer writes exactly 14 bytes:
        // packetType: byte
        // playerId: int
        // frame: int
        // inputBits: byte
        // startDelayFrames: int
        if (data.Length != SerializedNetworkPacketSize)
        {
            return false;
        }

        byte packetType = data[0];
        int playerId = BitConverter.ToInt32(data, 1);
        int frame = BitConverter.ToInt32(data, 5);
        byte inputBits = data[9];
        int startDelayFrames = BitConverter.ToInt32(data, 10);

        packet = new ParsedPacket(
            packetType,
            PacketTypeToName(packetType),
            playerId,
            frame,
            inputBits,
            startDelayFrames
        );

        return packetType is >= 1 and <= 4;
    }

    private static string PacketTypeToName(byte packetType)
    {
        return packetType switch
        {
            1 => "Hello",
            2 => "Ready",
            3 => "Start",
            4 => "Input",
            _ => $"Unknown({packetType})"
        };
    }

    private static bool IsTextRegisterPacket(byte[] payload)
    {
        if (payload.Length == 0 || payload.Length > 64)
        {
            return false;
        }

        for (int i = 0; i < payload.Length; i++)
        {
            if (payload[i] < 0x09 || payload[i] > 0x7E)
            {
                return false;
            }
        }

        string text = Encoding.UTF8.GetString(payload).Trim();
        return text.Equals("REGISTER", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("REGISTER ", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SendTextAsync(UdpClient udp, IPEndPoint to, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);

        await SendGate.WaitAsync();
        try
        {
            await udp.SendAsync(bytes, bytes.Length, to);
        }
        finally
        {
            SendGate.Release();
        }
    }

    private static void WriteLog(
        string eventName,
        IPEndPoint from,
        IPEndPoint? to,
        string packetType,
        int playerId,
        int frame,
        byte inputBits,
        int startDelayFrames,
        NetworkCondition networkCondition,
        int? appliedDelayMilliseconds,
        string message)
    {
        lock (LogGate)
        {
            long timeUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string packetLossText = networkCondition.PacketLossPercent.ToString("0.###", CultureInfo.InvariantCulture);
            string appliedDelayText = appliedDelayMilliseconds.HasValue
                ? appliedDelayMilliseconds.Value.ToString(CultureInfo.InvariantCulture)
                : "";

            string line = string.Join(",",
                Escape(eventName),
                timeUtcMs.ToString(CultureInfo.InvariantCulture),
                Escape(from.Address.ToString()),
                from.Port.ToString(CultureInfo.InvariantCulture),
                Escape(to?.Address.ToString() ?? ""),
                (to?.Port ?? 0).ToString(CultureInfo.InvariantCulture),
                Escape(packetType),
                playerId.ToString(CultureInfo.InvariantCulture),
                frame.ToString(CultureInfo.InvariantCulture),
                inputBits.ToString(CultureInfo.InvariantCulture),
                startDelayFrames.ToString(CultureInfo.InvariantCulture),
                networkCondition.BaseDelayMilliseconds.ToString(CultureInfo.InvariantCulture),
                networkCondition.JitterMilliseconds.ToString(CultureInfo.InvariantCulture),
                packetLossText,
                appliedDelayText,
                Escape(message)
            );

            logWriter?.WriteLine(line);

            string toText = to == null ? "-" : $"{to.Address}:{to.Port}";
            string appliedDelayForConsole = appliedDelayMilliseconds.HasValue ? $" appliedDelay={appliedDelayMilliseconds.Value}ms" : "";
            Console.WriteLine(
                $"[DelayServer] {eventName} {packetType} p={playerId} frame={frame} {from.Address}:{from.Port} -> {toText} baseDelay={networkCondition.BaseDelayMilliseconds}ms jitter=+/-{networkCondition.JitterMilliseconds}ms loss={packetLossText}%{appliedDelayForConsole} {message}"
            );
        }
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static bool SameEndpoint(IPEndPoint a, IPEndPoint b)
    {
        return a.Address.Equals(b.Address) && a.Port == b.Port;
    }

    private static IPEndPoint CopyEndpoint(IPEndPoint endpoint)
    {
        return new IPEndPoint(endpoint.Address, endpoint.Port);
    }

    private readonly record struct ServerSettings(
        int ListenPort,
        int DelayMilliseconds,
        int JitterMilliseconds,
        double PacketLossPercent
    );

    private readonly record struct NetworkCondition(
        int BaseDelayMilliseconds,
        int JitterMilliseconds,
        double PacketLossPercent
    );

    private readonly record struct ParsedPacket(
        byte PacketType,
        string PacketTypeName,
        int PlayerId,
        int Frame,
        byte InputBits,
        int StartDelayFrames
    );
}
