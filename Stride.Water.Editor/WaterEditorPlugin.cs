using Stride.Core.Assets.Editor.Annotations;
using Stride.Editor;
using Stride.Water;

[assembly: TypeImage(typeof(SeaComponent), "SeaComponent.png")]

namespace Stride.Water.Editor;

/// <summary>
/// The Game Studio's entry point into this package: its menu entries and editor services are found through it.
/// </summary>
public sealed class WaterEditorPlugin : StrideAssetsPlugin
{
}
