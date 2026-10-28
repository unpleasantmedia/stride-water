using System.ComponentModel;
using Stride.Core;
using Stride.Core.Annotations;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Engine.Design;

namespace Stride.Water;

/// <summary>
/// A sea: water at its entity's height, out to the horizon round the camera (see <see cref="SeaProcessor"/>), moved by
/// waves from its wind, see-through and pale where it is shallow, deep and opaque further out, foaming at the shore, and
/// reflecting the sky. How deep it is comes from what is drawn behind it, so it needs nothing else in the scene; with a
/// terrain in its scene (the terrain plugin, through the plugin API), its waves calm and break at the terrain's shore.
/// </summary>
[DataContract("SeaComponent")]
[Display("Sea", Expand = ExpandRule.Once)]
[ComponentCategory("Water")]
[ComponentOrder(30200)]
[DefaultEntityComponentProcessor(typeof(SeaProcessor), ExecutionMode = ExecutionMode.Runtime | ExecutionMode.Editor)]
public sealed class SeaComponent : EntityComponent
{
    /// <summary>Where the wind blows towards, in degrees round from +x towards +z.</summary>
    [DataMember(0), Display("Wind direction", category: "Waves"), DataMemberRange(0, 360, 1, 15, 0), DefaultValue(30f)]
    public float WindDirection { get; set; } = 30f;

    /// <summary>How high its biggest waves are, from trough to crest, in metres.</summary>
    [DataMember(10), Display("Wave height", category: "Waves"), DataMemberRange(0, 20, 0.05, 0.5, 2), DefaultValue(0.8f)]
    public float WaveHeight { get; set; } = 0.8f;

    /// <summary>How long its biggest waves are, crest to crest, in metres (its smaller ones are shorter).</summary>
    [DataMember(20), Display("Wave length", category: "Waves"), DataMemberRange(1, 500, 1, 10, 1), DefaultValue(24f)]
    public float WaveLength { get; set; } = 24f;

    /// <summary>How sharp its crests are, 0 (rolling) to 1 (as sharp as they go).</summary>
    [DataMember(30), Display("Choppiness", category: "Waves"), DataMemberRange(0, 1, 0.01, 0.1, 2), DefaultValue(0.6f)]
    public float Choppiness { get; set; } = 0.6f;

    /// <summary>How fast its waves move, as a share of how fast waves that long move on a real sea.</summary>
    [DataMember(40), Display("Speed", category: "Waves"), DataMemberRange(0, 5, 0.05, 0.25, 2), DefaultValue(1f)]
    public float Speed { get; set; } = 1f;

    /// <summary>How much its highest crests foam, 0 (none) to 1.</summary>
    [DataMember(50), Display("Whitecaps", category: "Waves"), DataMemberRange(0, 1, 0.01, 0.1, 2), DefaultValue(0.3f)]
    public float Whitecaps { get; set; } = 0.3f;

    /// <summary>How deep the water is where its waves start to break, in metres: its surf is the water shallower than this.</summary>
    [DataMember(70), Display("Surf depth", category: "Surf"), DataMemberRange(0.1, 50, 0.1, 1, 1), DefaultValue(3f)]
    public float SurfDepth { get; set; } = 3f;

    /// <summary>How far from the shore, at most, its breakers appear, in metres (fading in over the outer third of it).</summary>
    [DataMember(75), Display("Surf distance", category: "Surf"), DataMemberRange(1, 1000, 1, 10, 0), DefaultValue(60f)]
    public float SurfDistance { get; set; } = 60f;

    /// <summary>How high its breakers get, trough to crest, in metres.</summary>
    [DataMember(80), Display("Breaker height", category: "Surf"), DataMemberRange(0, 10, 0.05, 0.5, 2), DefaultValue(1.2f)]
    public float BreakerHeight { get; set; } = 1.2f;

    /// <summary>How far apart its breakers are, crest to crest, in metres.</summary>
    [DataMember(90), Display("Breaker length", category: "Surf"), DataMemberRange(2, 200, 0.5, 5, 1), DefaultValue(16f)]
    public float BreakerLength { get; set; } = 16f;

    /// <summary>How finely its shore is mapped: metres between the places it is read at.</summary>
    [DataMember(95), Display("Shore detail", category: "Surf"), DataMemberRange(1, 64, 1, 4, 1), DefaultValue(4f)]
    public float ShoreDetail { get; set; } = 4f;

    /// <summary>Its colour in shallow water, and (its alpha) how much can be seen through it there.</summary>
    [DataMember(100), Display("Shallow colour", category: "Colour")]
    public Color4 ShallowColor { get; set; } = new(0.10f, 0.45f, 0.42f, 0.25f);

    /// <summary>Its colour in deep water, where nothing is seen through it.</summary>
    [DataMember(110), Display("Deep colour", category: "Colour")]
    public Color4 DeepColor { get; set; } = new(0.01f, 0.07f, 0.12f, 1f);

    /// <summary>How many metres of water it takes to be all its deep colour.</summary>
    [DataMember(120), Display("Clarity", category: "Colour"), DataMemberRange(0.1, 100, 0.5, 5, 1), DefaultValue(4f)]
    public float Clarity { get; set; } = 4f;

    [DataMember(130), Display("Foam colour", category: "Colour")]
    public Color4 FoamColor { get; set; } = new(0.92f, 0.95f, 0.95f, 1f);

    /// <summary>How deep the water still foams at the shore, in metres.</summary>
    [DataMember(140), Display("Foam width", category: "Colour"), DataMemberRange(0, 10, 0.1, 0.5, 2), DefaultValue(0.6f)]
    public float FoamWidth { get; set; } = 0.6f;

    /// <summary>How smooth it is: 1 a mirror, lower a blurrier reflection of the sky.</summary>
    [DataMember(150), Display("Glossiness", category: "Colour"), DataMemberRange(0, 1, 0.01, 0.1, 2), DefaultValue(0.92f)]
    public float Glossiness { get; set; } = 0.92f;

    /// <summary>How wide its finest cells are, round the camera, in metres.</summary>
    [DataMember(200), Display("Cell size", category: "Detail"), DataMemberRange(0.1, 10, 0.1, 1, 2), DefaultValue(1f)]
    public float CellSize { get; set; } = 1f;

    /// <summary>How many rings it is drawn with: each doubles how far it reaches.</summary>
    [DataMember(210), Display("Levels", category: "Detail"), DataMemberRange(1, 14, 1, 1, 0), DefaultValue(11)]
    public int Levels { get; set; } = 11;
}
