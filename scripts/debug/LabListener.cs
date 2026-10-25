using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace DillControl.Debug;

/// <summary>
/// The running asset lab's ear (docs/decisions/0047): 127.0.0.1 only, one
/// JSON line in and one JSON line out per connection, carrying the token it
/// wrote to <see cref="LabPlay.ListenerPath"/>. The editor's <c>lab_*</c>
/// commands forward to it. Polled from the frame; a handler may take frames.
/// </summary>
public sealed class LabListener
{
    private const int FirstPort = 47400;
    private const int Ports = 10;
    private const double IdleSeconds = 10.0;

    private TcpServer _server;
    private string _token;
    private readonly List<Client> _clients = new();
    private readonly Func<Godot.Collections.Dictionary, Task<Godot.Collections.Dictionary>> _handle;

    public int Port { get; private set; }

    public LabListener(Func<Godot.Collections.Dictionary, Task<Godot.Collections.Dictionary>> handle) => _handle = handle;

    private sealed class Client(StreamPeerTcp peer)
    {
        public readonly StreamPeerTcp Peer = peer;
        public readonly List<byte> Received = new();
        public Task<Godot.Collections.Dictionary> Work;
        public double Age;
    }

    public bool Start()
    {
        for (int p = FirstPort; p < FirstPort + Ports; p++)
        {
            var server = new TcpServer();
            if (server.Listen((ushort)p, "127.0.0.1") != Error.Ok)
            {
                continue;
            }
            _server = server;
            Port = p;
            _token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var info = new Godot.Collections.Dictionary { ["port"] = p, ["token"] = _token, ["pid"] = OS.GetProcessId() };
            using FileAccess file = FileAccess.Open(LabPlay.ListenerPath, FileAccess.ModeFlags.Write);
            file?.StoreString(Json.Stringify(info));
            GD.Print($"[lab] listening on 127.0.0.1:{p}");
            return true;
        }
        GD.PushWarning($"[lab] no free port in {FirstPort}-{FirstPort + Ports - 1}: the lab cannot be driven from the editor");
        return false;
    }

    public void Stop()
    {
        foreach (Client client in _clients)
        {
            client.Peer.DisconnectFromHost();
        }
        _clients.Clear();
        _server?.Stop();
        _server = null;
        if (FileAccess.FileExists(LabPlay.ListenerPath))
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(LabPlay.ListenerPath));
        }
    }

    public void Poll(double delta)
    {
        if (_server == null)
        {
            return;
        }
        while (_server.IsConnectionAvailable())
        {
            _clients.Add(new Client(_server.TakeConnection()));
        }
        for (int i = _clients.Count - 1; i >= 0; i--)
        {
            Client client = _clients[i];
            client.Age += delta;
            if (!Step(client))
            {
                client.Peer.DisconnectFromHost();
                _clients.RemoveAt(i);
            }
        }
    }

    private bool Step(Client client)
    {
        StreamPeerTcp peer = client.Peer;
        peer.Poll();
        if (peer.GetStatus() != StreamPeerTcp.Status.Connected)
        {
            return false;
        }
        if (client.Work != null)
        {
            if (!client.Work.IsCompleted)
            {
                return true;
            }
            Godot.Collections.Dictionary reply = client.Work.IsFaulted
                ? new Godot.Collections.Dictionary { ["ok"] = false, ["error"] = client.Work.Exception?.GetBaseException().Message ?? "failed" }
                : client.Work.Result;
            peer.PutData(Encoding.UTF8.GetBytes(Json.Stringify(reply) + "\n"));
            return false;
        }
        int available = peer.GetAvailableBytes();
        if (available > 0)
        {
            Godot.Collections.Array got = peer.GetData(available);
            client.Received.AddRange(got[1].AsByteArray());
        }
        int end = client.Received.IndexOf((byte)'\n');
        if (end < 0)
        {
            return client.Age < IdleSeconds;
        }
        Variant parsed = Json.ParseString(Encoding.UTF8.GetString(client.Received.GetRange(0, end).ToArray()));
        if (parsed.VariantType != Variant.Type.Dictionary || parsed.AsGodotDictionary().GetValueOrDefault("token", "").AsString() != _token)
        {
            client.Work = Task.FromResult(new Godot.Collections.Dictionary { ["ok"] = false, ["error"] = "bad request or token" });
            return true;
        }
        client.Work = Run(parsed.AsGodotDictionary());
        return true;
    }

    private async Task<Godot.Collections.Dictionary> Run(Godot.Collections.Dictionary request)
    {
        try
        {
            Godot.Collections.Dictionary result = await _handle(request);
            result["ok"] = true;
            return result;
        }
        catch (Exception e)
        {
            return new Godot.Collections.Dictionary { ["ok"] = false, ["error"] = e.Message };
        }
    }
}
