using System.Threading.Tasks;
using Stride.Assets.Presentation.AssetEditors.EntityHierarchyEditor.EntityFactories;
using Stride.Assets.Presentation.AssetEditors.EntityHierarchyEditor.ViewModels;
using Stride.Core;
using Stride.Engine;

namespace Stride.Water.Editor;

/// <summary>Add entity > Water > Sea: at the height it is placed at.</summary>
[Display(10, "Sea", "Water")]
public class SeaEntityFactory : EntityFactory
{
    public override Task<Entity> CreateEntity(EntityHierarchyItemViewModel parent) =>
        CreateEntityWithComponent(ComputeNewName(parent, "Sea"), new SeaComponent());
}
