using System;
using System.Threading.Tasks;
using Stride.Assets.Presentation.AssetEditors.EntityHierarchyEditor.Game;
using Stride.Assets.Presentation.AssetEditors.EntityHierarchyEditor.Services;
using Stride.Assets.Presentation.AssetEditors.EntityHierarchyEditor.ViewModels;
using Stride.Assets.Presentation.AssetEditors.GameEditor.Game;
using Stride.Assets.Presentation.AssetEditors.GameEditor.Services;
using Stride.Core.Mathematics;
using Stride.Editor.EditorGame.Game;

namespace Stride.Water.Editor;

/// <summary>In the scene editor, has seas follow its camera (see <see cref="SeaProcessor.EditorCamera"/>).</summary>
[EditorGameService(typeof(EntityHierarchyEditorController))]
public sealed class SeaCameraService : EditorGameServiceBase
{
    public SeaCameraService(EntityHierarchyEditorViewModel editor, IEditorGameController controller)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(controller);
    }

    protected override Task<bool> Initialize(EditorServiceGame editorGame)
    {
        var camera = editorGame.EditorServices.Get<IEditorGameCameraService>();
        SeaProcessor.EditorCamera = () => camera is { } c ? Matrix.Invert(c.ViewMatrix).TranslationVector : null;
        return Task.FromResult(true);
    }
}
