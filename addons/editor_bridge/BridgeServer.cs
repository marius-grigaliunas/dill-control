#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The bridge's listener: Godot's own <see cref="TcpServer"/> on 127.0.0.1,
/// polled from the plugin's <c>_Process</c>, so a command runs on the
/// editor's main thread. Just enough HTTP for one JSON command a request:
/// <c>POST /cmd/&lt;name&gt;</c> with the token in <c>X-Bridge-Token</c>.
/// Its port and token are in <c>.godot/editor_bridge.json</c> while it
/// listens, and only then.
/// </summary>
public sealed class BridgeServer
{
    public const string InfoPath = "res://.godot/editor_bridge.json";
    private const int FirstPort = 47380;
    private const int Ports = 10;
    private const double IdleSeconds = 15.0;
    private const int MaxRequestBytes = 4 << 20;

    private TcpServer _server;
    private string _token;
    private readonly List<Client> _clients = new();

    public int Port { get; private set; }

    public bool Listening => _server != null && _server.IsListening();

    public bool Start()
    {
        for (int p = FirstPort; p < FirstPort + Ports; p++)
        {
            var server = new TcpServer();
            if (server.Listen((ushort)p, "127.0.0.1") != Error.Ok)
                continue;
            _server = server;
            Port = p;
            _token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var info = new JsonObject { ["port"] = p, ["token"] = _token, ["pid"] = OS.GetProcessId() };
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(InfoPath), info.ToJsonString());
            GD.Print($"[bridge] listening on 127.0.0.1:{p}");
            return true;
        }
        GD.PushWarning($"[bridge] no free port in {FirstPort}-{FirstPort + Ports - 1}: the bridge is off");
        return false;
    }

    public void Stop()
    {
        foreach (Client client in _clients)
            client.Peer.DisconnectFromHost();
        _clients.Clear();
        if (_server != null)
        {
            _server.Stop();
            _server = null;
            GD.Print("[bridge] stopped");
        }
        string info = ProjectSettings.GlobalizePath(InfoPath);
        if (System.IO.File.Exists(info))
            System.IO.File.Delete(info);
    }

    public void Poll(double delta)
    {
        if (_server == null)
            return;
        while (_server.IsConnectionAvailable())
            _clients.Add(new Client(_server.TakeConnection()));
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

    private sealed class Client(StreamPeerTcp peer)
    {
        public readonly StreamPeerTcp Peer = peer;
        public readonly List<byte> Received = new();
        public Task<(int Status, string Body)> Work;
        public double Age;
    }

    // False once the client is done with.
    private bool Step(Client client)
    {
        StreamPeerTcp peer = client.Peer;
        peer.Poll();
        if (peer.GetStatus() != StreamPeerTcp.Status.Connected)
            return false;

        if (client.Work != null)
        {
            if (!client.Work.IsCompleted)
                return true;
            (int status, string reply) = client.Work.IsFaulted
                ? (500, Failure(client.Work.Exception?.GetBaseException().Message))
                : client.Work.Result;
            Respond(peer, status, reply);
            return false;
        }

        int available = peer.GetAvailableBytes();
        if (available > 0)
        {
            Godot.Collections.Array got = peer.GetData(available);
            if ((Error)got[0].AsInt32() != Error.Ok)
                return false;
            client.Received.AddRange(got[1].AsByteArray());
            if (client.Received.Count > MaxRequestBytes)
            {
                Respond(peer, 413, Failure("request too large"));
                return false;
            }
        }
        if (Parse(client.Received, out string method, out string path, out Dictionary<string, string> headers, out string body))
        {
            client.Work = Handle(method, path, headers, body);
            return true;
        }
        return client.Age < IdleSeconds;
    }

    private static bool Parse(List<byte> bytes, out string method, out string path, out Dictionary<string, string> headers, out string body)
    {
        method = path = body = null;
        headers = null;
        byte[] data = bytes.ToArray();
        int end = IndexOf(data, "\r\n\r\n"u8);
        if (end < 0)
            return false;
        string[] lines = Encoding.ASCII.GetString(data, 0, end).Split("\r\n");
        string[] request = lines[0].Split(' ');
        if (request.Length < 2)
            return false;
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        int length = headers.TryGetValue("Content-Length", out string l) && int.TryParse(l, out int n) ? n : 0;
        if (data.Length - (end + 4) < length)
            return false;
        method = request[0];
        path = request[1];
        body = Encoding.UTF8.GetString(data, end + 4, length);
        return true;
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> what) => data.AsSpan().IndexOf(what);

    private async Task<(int, string)> Handle(string method, string path, Dictionary<string, string> headers, string body)
    {
        // A web page could reach a port on localhost; it always says where it is from.
        if (headers.ContainsKey("Origin"))
            return (403, Failure("requests from a web page are refused"));
        if (!headers.TryGetValue("X-Bridge-Token", out string token) || token != _token)
            return (403, Failure("wrong or missing token: read it from .godot/editor_bridge.json"));
        if (method != "POST" || !path.StartsWith("/cmd/"))
            return (404, Failure("POST /cmd/<name>"));
        string name = path["/cmd/".Length..];
        if (!Bridge.TryGet(name, out Bridge.Command command))
            return (200, Failure($"no command '{name}'; 'commands' lists them"));
        try
        {
            JsonObject args = string.IsNullOrWhiteSpace(body) ? new JsonObject() : JsonNode.Parse(body) as JsonObject
                ?? throw new Bridge.Refused("the arguments must be a JSON object");
            JsonNode result = await command.Run(args);
            return (200, new JsonObject { ["ok"] = true, ["result"] = result }.ToJsonString());
        }
        catch (Exception e)
        {
            if (e is not Bridge.Refused)
                GD.PushError($"[bridge] {name}: {e}");
            return (200, Failure(e is Bridge.Refused ? e.Message : $"{e.GetType().Name}: {e.Message}"));
        }
    }

    private static string Failure(string message) => new JsonObject { ["ok"] = false, ["error"] = message }.ToJsonString();

    private static void Respond(StreamPeerTcp peer, int status, string body)
    {
        byte[] content = Encoding.UTF8.GetBytes(body);
        string reason = status switch { 200 => "OK", 403 => "Forbidden", 404 => "Not Found", 413 => "Too Large", _ => "Error" };
        byte[] head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
        peer.PutData(head);
        peer.PutData(content);
    }
}
#endif
