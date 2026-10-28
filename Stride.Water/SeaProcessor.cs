using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Core.Serialization.Contents;
using Stride.Engine;
using Stride.Engine.Design;
using Stride.Games;
using Stride.Graphics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Stride.Shaders;
using Stride.Terrain;
using Buffer = Stride.Graphics.Buffer;

namespace Stride.Water;

/// <summary>
/// Draws seas: each as rings of flat grids round the camera, each ring's cells twice the one inside it (as the
/// terrain's), lifted into waves by its material's vertex stage (<c>SeaWaves.sdsl</c>), coloured and made see-through by
/// how much water is in front of what is drawn behind it (<c>SeaColor.sdsl</c>, <c>SeaAlpha.sdsl</c>), and glossy, so
/// it reflects the scene's sky light. What it builds is never saved.
/// </summary>
public sealed class SeaProcessor : EntityProcessor<SeaComponent, SeaProcessor.Built>
{
    /// <summary>How many of its own cells each ring reaches out from its middle.</summary>
    public const int RingCells = 64;

    private const string BuiltName = "Sea (built)";

    /// <summary>Where the camera is, in the editor (set by its plugin); none in the game, where it is the graphics compositor's first camera.</summary>
    public static Func<Vector3?>? EditorCamera { get; set; }

    private static readonly Logger Log = GlobalLogger.GetLogger("Sea");

    private IGraphicsDeviceService? graphics;

    public sealed class Built
    {
        public Entity? Holder;
        public int? State;
        public Material? Material;
        public Mesh[] Meshes = Array.Empty<Mesh>();
        public List<(Entity Ring, Model Model)> Rings = new();
        public Vector3? Followed;
        // Its settings last put on its material (they are put again only when they change: putting arrays on a
        // material recompiles its effect).
        public int? Set;

        // Its shore map: on the GPU, what it was built from, and one being built (off the game's thread). What its floor
        // was last seen as, and since when, so it is built again only once its floor has stood a moment.
        public Texture? Shore;
        public (object? Floor, object? Data, int Version, float Level, float Detail) ShoreFrom;
        public Task<(SeaShoreMap Map, float Width)>? ShoreBuilding;
        public (object? Floor, object? Data, int Version, float Level, float Detail) ShoreBuildingFrom;
        public (object? Floor, object? Data, int Version, float Level, float Detail)? ShoreSeen;
        public double ShoreSeenSince;
    }

    protected override void OnSystemAdd() => graphics = Services.GetService<IGraphicsDeviceService>();

    protected override Built GenerateComponentData(Entity entity, SeaComponent component) => new();

    protected override bool IsAssociatedDataValid(Entity entity, SeaComponent component, Built associatedData) => true;

    protected override void OnEntityComponentRemoved(Entity entity, SeaComponent component, Built data)
    {
        TearDown(data);
        if (data.Holder != null)
            data.Holder.Scene = null;
    }

    public override void Update(GameTime time)
    {
        var device = graphics?.GraphicsDevice;
        var root = (EntityManager as SceneInstance)?.RootScene;
        if (device == null || root == null)
            return;
        var camera = Camera() ?? Vector3.Zero;
        foreach (var (sea, data) in ComponentDatas)
        {
            var state = HashCode.Combine(sea.Levels, sea.CellSize, sea.Glossiness);
            if (state != data.State)
            {
                data.State = state;
                Build(sea, data, device, root, Services.GetService<ContentManager>());
            }
            if (data.Material == null)
                continue;
            var settings = Settings(sea);
            if (settings != data.Set)
            {
                data.Set = settings;
                Parameters(sea, data);
            }
            var level = sea.Entity.Transform.WorldMatrix.TranslationVector.Y;
            foreach (var pass in data.Material.Passes)
            {
                pass.Parameters.Set(SeaMapsKeys.SeaCamera, camera);
                pass.Parameters.Set(SeaMapsKeys.SeaLevel, level);
            }
            Shore(sea, data, device, time.Total.TotalSeconds);
            Follow(sea, data, camera);
        }
    }

    /// <summary>The scene's first terrain, for a sea given no floor.</summary>
    private TerrainComponent? FirstTerrain()
    {
        foreach (var entity in EntityManager)
        {
            if (entity.Get<TerrainComponent>() is { } terrain)
                return terrain;
        }
        return null;
    }

    /// <summary>
    /// Keeps its shore map to its floor: built again (off the game's thread) once its floor, level or detail has stood
    /// changed for a second, and put on its material once built. None without a floor.
    /// </summary>
    private void Shore(SeaComponent sea, Built data, GraphicsDevice device, double now)
    {
        var level = sea.Entity.Transform.WorldMatrix.TranslationVector.Y;
        var floor = sea.Floor ?? FirstTerrain();
        var from = ((object?)floor, (object?)floor?.Data, floor?.Version ?? 0, level, sea.ShoreDetail);

        if (data.ShoreBuilding is { IsCompleted: true } built)
        {
            data.ShoreBuilding = null;
            if (built.IsCompletedSuccessfully)
            {
                var (map, mapWidth) = built.Result;
                var texels = new Vector4[map.Size * map.Size];
                for (var i = 0; i < texels.Length; i++)
                    texels[i] = new Vector4(map.Distance[i], map.Depth[i], map.DirectionX[i], map.DirectionZ[i]);
                data.Shore?.Dispose();
                data.Shore = Texture.New2D(device, map.Size, map.Size, PixelFormat.R32G32B32A32_Float, texels);
                data.ShoreFrom = data.ShoreBuildingFrom;
                foreach (var pass in data.Material!.Passes)
                {
                    pass.Parameters.Set(SeaMapsKeys.ShoreMap, data.Shore);
                    pass.Parameters.Set(SeaMapsKeys.ShoreOrigin, new Vector2(map.OriginX, map.OriginZ));
                    pass.Parameters.Set(SeaMapsKeys.ShoreWidth, mapWidth);
                    pass.Parameters.Set(SeaMapsKeys.HasShore, 1f);
                }
            }
            else
            {
                Log.Error("The sea's shore could not be read from its floor.", built.Exception?.GetBaseException());
                data.ShoreFrom = data.ShoreBuildingFrom;
            }
        }

        if (floor?.Data is not { } ground || ground.Locations.Count == 0)
        {
            if (data.Shore != null || data.ShoreFrom.Floor != null)
            {
                foreach (var pass in data.Material!.Passes)
                    pass.Parameters.Set(SeaMapsKeys.HasShore, 0f);
                data.ShoreFrom = default;
            }
            return;
        }
        if (from == data.ShoreFrom || data.ShoreBuilding != null)
            return;
        if (data.ShoreSeen != from)
        {
            (data.ShoreSeen, data.ShoreSeenSince) = (from, now);
            // Built at once the first time; after a change, once it has stood a second.
            if (data.ShoreFrom.Floor != null)
                return;
        }
        else if (now - data.ShoreSeenSince < 1.0)
        {
            return;
        }

        // Over its floor's regions, and its surf's reach past them, as a square.
        var width = ground.RegionWidth;
        var origin = floor.Origin;
        var (minX, minZ, maxX, maxZ) = (ground.Locations.Min(l => l.X), ground.Locations.Min(l => l.Z), ground.Locations.Max(l => l.X) + 1, ground.Locations.Max(l => l.Z) + 1);
        var margin = 64f;
        var (x0, z0) = (origin.X + minX * width - margin, origin.Z + minZ * width - margin);
        var across = MathF.Max(maxX - minX, maxZ - minZ) * width + 2f * margin;
        var size = Math.Clamp((int)MathF.Ceiling(across / MathF.Max(sea.ShoreDetail, 0.5f)), 16, 2048);
        data.ShoreBuildingFrom = from;
        data.ShoreBuilding = Task.Run(() => (SeaShoreMap.Build(size, x0, z0, across, level, (x, z) => floor.HeightAt(new Vector3(x, 0f, z))), across));
    }

    private Vector3? Camera()
    {
        if (EditorCamera?.Invoke() is { } editor)
            return editor;
        var compositor = Services.GetService<SceneSystem>()?.GraphicsCompositor;
        return compositor?.Cameras.FirstOrDefault()?.Camera?.Entity?.Transform.WorldMatrix.TranslationVector;
    }

    private static void Build(SeaComponent sea, Built data, GraphicsDevice device, Scene root, ContentManager? content)
    {
        TearDown(data);
        if (data.Holder == null)
        {
            data.Holder = new Entity(BuiltName);
            // Standing for its sea in the editor (picked as it), as it is in no edited scene.
            data.Holder.Tags.Set(GeneratedEntity.OwnerKey, sea.Entity);
            root.Entities.Add(data.Holder);
        }
        data.Material = Rendering.Material.New(device, new MaterialDescriptor
        {
            Attributes =
            {
                Displacement = new SeaWavesFeature(),
                Diffuse = new MaterialDiffuseMapFeature(new SeaNode("SeaColor")),
                DiffuseModel = new MaterialDiffuseLambertModelFeature(),
                MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(sea.Glossiness)),
                // Water reflects about 2% of light head on, all of it at a grazing angle (the microfacet model's Fresnel).
                Specular = new MaterialSpecularMapFeature { SpecularMap = new ComputeColor(new Color4(0.02f, 0.02f, 0.02f, 1f)) },
                // The sky's reflection by the polynomial fit, not the lookup texture, which the editor's content may not have.
                SpecularModel = new MaterialSpecularMicrofacetModelFeature { Environment = new MaterialSpecularMicrofacetEnvironmentGGXPolynomial() },
                Transparency = new MaterialTransparencyBlendFeature { Alpha = new SeaNode("SeaAlpha") },
                CullMode = CullMode.None,
            },
        }, content);
        data.Meshes = new[] { Grid(device, null) }.Concat(Enumerable.Range(0, 4).Select(o => Grid(device, new Int2(o % 2, o / 2)))).ToArray();
        for (var level = 0; level < Math.Clamp(sea.Levels, 1, 14); level++)
        {
            var model = new Model { data.Material, data.Meshes[level == 0 ? 0 : 1] };
            var ring = new Entity($"Ring {level}") { new ModelComponent { Model = model, IsShadowCaster = false } };
            ring.Tags.Set(GeneratedEntity.OwnerKey, sea.Entity);
            data.Holder.AddChild(ring);
            data.Rings.Add((ring, model));
        }
        data.Followed = null;
        data.Set = null;
        data.ShoreFrom = default;
        data.ShoreSeen = null;
    }

    private static void TearDown(Built data)
    {
        foreach (var (ring, _) in data.Rings)
            ring.Scene = null;
        data.Holder?.Transform.Children.Clear();
        data.Rings.Clear();
        foreach (var mesh in data.Meshes)
        {
            mesh.Draw.IndexBuffer?.Buffer.Dispose();
            foreach (var binding in mesh.Draw.VertexBuffers)
                binding.Buffer.Dispose();
        }
        data.Meshes = Array.Empty<Mesh>();
        data.Material = null;
        data.Shore?.Dispose();
        data.Shore = null;
    }

    /// <summary>
    /// Its settings as its shaders take them. Its waves made from its wind: twelve, from its wave length down to about
    /// a tenth of it, each turned off the wind by a fixed scatter that widens as they shorten (as wind seas are, the
    /// small waves running every way), each as high as its length gives at one steepness, and moving as fast as
    /// deep-water waves that long do. And sixteen ripples shorter still, for its surface's detail.
    /// </summary>
    private static int Settings(SeaComponent sea)
    {
        var hash = new HashCode();
        hash.Add(sea.WindDirection);
        hash.Add(sea.WaveHeight);
        hash.Add(sea.WaveLength);
        hash.Add(sea.Choppiness);
        hash.Add(sea.Speed);
        hash.Add(sea.Whitecaps);
        hash.Add(sea.ShallowColor);
        hash.Add(sea.DeepColor);
        hash.Add(sea.FoamColor);
        hash.Add(sea.Clarity);
        hash.Add(sea.FoamWidth);
        hash.Add(sea.Floor);
        hash.Add(sea.SurfDepth);
        hash.Add(sea.SurfDistance);
        hash.Add(sea.BreakerHeight);
        hash.Add(sea.BreakerLength);
        return hash.ToHashCode();
    }

    private static void Parameters(SeaComponent sea, Built data)
    {
        const int count = 12, rippleCount = 16;
        var waves = new Vector4[count];
        var motion = new Vector4[count];
        var ripples = new Vector4[rippleCount];
        var lengths = new float[count];
        var total = 0f;
        for (var i = 0; i < count; i++)
        {
            // Each a little longer or shorter than the even step, so they never line up into a pattern.
            lengths[i] = MathF.Max(sea.WaveLength * MathF.Pow(0.81f, i) * (0.8f + 0.4f * Scatter(i + 20)), 0.05f);
            total += lengths[i];
        }
        var reach = 0f;
        for (var i = 0; i < count; i++)
        {
            var spread = 20f + 70f * i / (count - 1);
            var angle = MathUtil.DegreesToRadians(sea.WindDirection + spread * (2f * Scatter(i) - 1f));
            var k = 2f * MathF.PI / lengths[i];
            // Together about three quarters of the height asked, trough to crest, as they seldom all peak at once.
            var amplitude = sea.WaveHeight * 0.75f * lengths[i] / total;
            reach += amplitude;
            waves[i] = new Vector4(MathF.Cos(angle), MathF.Sin(angle), k, amplitude);
            motion[i] = new Vector4(MathF.Sqrt(9.81f * k) * sea.Speed, Math.Clamp(sea.Choppiness, 0f, 1f), 2f * MathF.PI * Scatter(i + 90), 0f);
        }
        var shortest = lengths[count - 1];
        for (var i = 0; i < rippleCount; i++)
        {
            var length = MathF.Max(shortest * MathF.Pow(0.8f, i + 1), 0.08f);
            var angle = MathUtil.DegreesToRadians(sea.WindDirection + 120f * (2f * Scatter(i + 50) - 1f));
            var k = 2f * MathF.PI / length;
            // Each about as steep as the others: a few hundredths, more on a choppier sea.
            ripples[i] = new Vector4(MathF.Cos(angle), MathF.Sin(angle), k, (0.025f + 0.04f * sea.Choppiness) / k);
        }
        // Its breakers, moving as waves that long do in its surf's water.
        var breakerK = 2f * MathF.PI / MathF.Max(sea.BreakerLength, 0.5f);
        var breaker = new Vector4(breakerK, MathF.Sqrt(9.81f * breakerK * MathF.Tanh(breakerK * MathF.Max(sea.SurfDepth, 0.1f))) * sea.Speed,
            sea.BreakerHeight, sea.SurfDepth);
        foreach (var pass in data.Material!.Passes)
        {
            var parameters = pass.Parameters;
            parameters.Set(SeaMapsKeys.Waves, waves);
            parameters.Set(SeaMapsKeys.WaveMotion, motion);
            parameters.Set(SeaMapsKeys.Ripples, ripples);
            parameters.Set(SeaMapsKeys.WaveReach, reach);
            parameters.Set(SeaMapsKeys.Whitecaps, sea.Whitecaps);
            parameters.Set(SeaMapsKeys.SeaCells, (float)RingCells);
            parameters.Set(SeaMapsKeys.ShallowColor, (Vector4)sea.ShallowColor);
            parameters.Set(SeaMapsKeys.DeepColor, (Vector4)sea.DeepColor);
            parameters.Set(SeaMapsKeys.FoamColor, (Vector4)sea.FoamColor);
            parameters.Set(SeaMapsKeys.Clarity, sea.Clarity);
            parameters.Set(SeaMapsKeys.FoamWidth, sea.FoamWidth);
            parameters.Set(SeaMapsKeys.Breaker, breaker);
            parameters.Set(SeaMapsKeys.SurfDistance, sea.SurfDistance);
        }
    }

    /// <summary>A fixed scatter, 0 to 1, by index: the same waves every time.</summary>
    private static float Scatter(int i)
    {
        var x = MathF.Sin(i * 12.9898f + 78.233f) * 43758.5453f;
        return x - MathF.Floor(x);
    }

    /// <summary>Moves the rings round the camera, at its height: each to its own cells' doubled step, with the hole for the ring inside it where that ring is.</summary>
    private static void Follow(SeaComponent sea, Built data, Vector3 camera)
    {
        var level = sea.Entity.Transform.WorldMatrix.TranslationVector.Y;
        // Bounded as high and low as its waves go.
        var reach = sea.WaveHeight + 1f;
        if (data.Followed is { } was && Vector3.DistanceSquared(was, camera) < 1e-4f && data.Rings.Count > 0 && data.Rings[0].Ring.Transform.Position.Y == level
            && data.Meshes[0].BoundingBox.Maximum.Y == reach)
            return;
        data.Followed = camera;
        foreach (var mesh in data.Meshes)
        {
            mesh.BoundingBox = new BoundingBox(new Vector3(mesh.BoundingBox.Minimum.X, -reach, mesh.BoundingBox.Minimum.Z), new Vector3(mesh.BoundingBox.Maximum.X, reach, mesh.BoundingBox.Maximum.Z));
            mesh.BoundingSphere = BoundingSphere.FromBox(mesh.BoundingBox);
        }
        var inner = Vector2.Zero;
        for (var i = 0; i < data.Rings.Count; i++)
        {
            var size = MathF.Max(sea.CellSize, 0.01f) * (1 << i);
            var step = 2f * size;
            var middle = new Vector2(MathF.Floor(camera.X / step) * step, MathF.Floor(camera.Z / step) * step);
            var (ring, model) = data.Rings[i];
            if (i > 0)
            {
                var offset = new Int2((int)MathF.Round((inner.X - middle.X) / size), (int)MathF.Round((inner.Y - middle.Y) / size));
                var mesh = data.Meshes[1 + Math.Clamp(offset.X, 0, 1) + 2 * Math.Clamp(offset.Y, 0, 1)];
                if (model.Meshes[0] != mesh)
                    model.Meshes[0] = mesh;
            }
            ring.Transform.Scale = new Vector3(size, 1f, size);
            ring.Transform.Position = new Vector3(middle.X, level, middle.Y);
            inner = middle;
        }
    }

    /// <summary>
    /// A flat grid of unit cells, <see cref="RingCells"/> each way from its middle: whole, or a ring round a hole half as
    /// wide, as far off its middle as the ring inside it can be (0 or 1 cells each way).
    /// </summary>
    private static Mesh Grid(GraphicsDevice device, Int2? hole)
    {
        const int across = 2 * RingCells + 1;
        var vertices = new VertexPositionNormalTexture[across * across];
        for (var z = 0; z < across; z++)
        {
            for (var x = 0; x < across; x++)
                vertices[z * across + x] = new VertexPositionNormalTexture(new Vector3(x - RingCells, 0f, z - RingCells), Vector3.UnitY, Vector2.Zero);
        }
        var indices = new List<int>();
        for (var z = 0; z < across - 1; z++)
        {
            for (var x = 0; x < across - 1; x++)
            {
                if (hole is { } h && InHole(x - RingCells, h.X) && InHole(z - RingCells, h.Y))
                    continue;
                var a = z * across + x;
                // Clockwise seen from above (x right, z towards the viewer), as Stride draws front faces.
                indices.AddRange(new[] { a, a + 1, a + across + 1, a, a + across + 1, a + across });
            }
        }
        var bounds = new BoundingBox(new Vector3(-RingCells - 2, -1f, -RingCells - 2), new Vector3(RingCells + 2, 1f, RingCells + 2));
        var draw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            DrawCount = indices.Count,
            IndexBuffer = new IndexBufferBinding(Buffer.Index.New(device, indices.ToArray()), true, indices.Count),
            VertexBuffers = [new VertexBufferBinding(Buffer.Vertex.New(device, vertices), VertexPositionNormalTexture.Layout, vertices.Length)],
        };
        return new Mesh { Draw = draw, BoundingBox = bounds, BoundingSphere = BoundingSphere.FromBox(bounds) };
    }

    private static bool InHole(int cell, int offset) => cell >= offset - RingCells / 2 && cell < offset + RingCells / 2;
}

/// <summary>
/// A sea material's displacement: its rings' vertices moved into waves (<c>SeaWaves.sdsl</c>); and its surface turned by
/// its ripples at each pixel, before it is lit (<c>SeaRipples.sdsl</c>).
/// </summary>
[DataContract("SeaWavesFeature")]
[Display("Sea waves")]
public sealed class SeaWavesFeature : MaterialFeature, IMaterialDisplacementFeature
{
    public override void GenerateShader(MaterialGeneratorContext context)
    {
        context.SetStreamFinalModifier<SeaWavesFeature>(MaterialShaderStage.Vertex, new ShaderClassSource("SeaWaves"));
        context.AddShaderSource(MaterialShaderStage.Pixel, new ShaderClassSource("SeaRipples"));
    }
}

/// <summary>One of a sea's shaders (<c>SeaColor</c>, <c>SeaAlpha</c>) as a material's colour or scalar.</summary>
internal sealed class SeaNode(string shader) : ComputeNode, IComputeColor, IComputeScalar
{
    public bool HasChanged => false;

    public override ShaderSource GenerateShaderSource(ShaderGeneratorContext context, MaterialComputeColorKeys baseKeys) =>
        new ShaderClassSource(shader);
}
