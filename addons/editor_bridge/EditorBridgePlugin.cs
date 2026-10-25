#if TOOLS
using System.Linq;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The editor bridge (docs/decisions/0044): Claude works in this running
/// editor, on the scene the user has open, through commands that make the
/// same real nodes the user would, each one undo. Listens on 127.0.0.1 only
/// (<see cref="BridgeServer"/>), and only while this plugin is on.
///
/// Every reload of the project's code unloads this code, so the port is
/// closed before it goes and opened again by the new code on its first
/// frame, as <see cref="VineGenPlugin"/> rebuilds its panel.
/// </summary>
[Tool]
public partial class EditorBridgePlugin : EditorPlugin
{
    private BridgeServer _server;
    private RunCommands.Catcher _catcher;

    // False again after every reload (statics start over with the new code).
    private static bool _startedByThisCode;

    public override void _EnterTree()
    {
        AssemblyLoadContext.GetLoadContext(typeof(EditorBridgePlugin).Assembly)!.Unloading += OnUnloading;
        Start();
    }

    public override void _ExitTree()
    {
        AssemblyLoadContext.GetLoadContext(typeof(EditorBridgePlugin).Assembly)!.Unloading -= OnUnloading;
        Stop();
    }

    private void OnUnloading(AssemblyLoadContext context) => Stop();

    public override void _Process(double delta)
    {
        if (!_startedByThisCode)
            Start();
        _server?.Poll(delta);
    }

    private void Start()
    {
        Stop();
        Bridge.Register("commands", "Every command the editor answers, with its arguments.", """{"type":"object","properties":{}}""",
            _ => new JsonArray(Bridge.Commands.Select(c => (JsonNode)new JsonObject
            {
                ["name"] = c.Name,
                ["description"] = c.Description,
                ["schema"] = c.Schema.DeepClone(),
            }).ToArray()));
        ViewCommands.Register();
        PlaceCommands.Register();
        NodeCommands.Register();
        SceneCommands.Register();
        RunCommands.Register();
        _catcher = new RunCommands.Catcher();
        OS.AddLogger(_catcher);
        _server = new BridgeServer();
        _server.Start();
        _startedByThisCode = true;
    }

    private void Stop()
    {
        if (_catcher != null)
        {
            OS.RemoveLogger(_catcher);
            _catcher = null;
        }
        _server?.Stop();
        _server = null;
    }
}
#endif
