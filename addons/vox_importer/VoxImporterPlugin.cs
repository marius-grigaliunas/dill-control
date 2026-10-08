#if TOOLS
using Godot;

namespace DillControl.Editor;

/// <summary>Registers <see cref="VoxSceneImporter"/>, so <c>.vox</c> files
/// import as scenes. Ours, not a third-party addon: Godot only finds editor
/// plugins under <c>addons/</c>.</summary>
[Tool]
public partial class VoxImporterPlugin : EditorPlugin
{
    private VoxSceneImporter _importer;

    public override void _EnterTree()
    {
        _importer = new VoxSceneImporter();
        AddSceneFormatImporterPlugin(_importer);
    }

    public override void _ExitTree()
    {
        RemoveSceneFormatImporterPlugin(_importer);
        _importer = null;
    }
}
#endif
