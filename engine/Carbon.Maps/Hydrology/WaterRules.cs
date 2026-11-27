// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text.Json;

namespace Carbon.Maps.Hydrology;

/// <summary>
/// Movement and oxygen tuning in water, from <c>resources/config/water.json</c> (ADR-032 D10–D11). The server and
/// client prediction read the same document (ADR-014), so a predicted swimmer moves at the server's speed.
/// </summary>
public sealed record WaterRules
{
    /// <summary>The config document name.</summary>
    public const string DocumentName = "water";

    /// <summary>Gets the shipped defaults (the same values as <c>water.json</c>).</summary>
    public static WaterRules Default { get; } = new();

    /// <summary>Gets the speed multiplier at the waterline (Shore depth).</summary>
    public float ShoreSpeed { get; init; } = 0.80f;

    /// <summary>Gets the speed multiplier wading waist-deep (Shallow depth).</summary>
    public float ShallowSpeed { get; init; } = 0.60f;

    /// <summary>Gets the speed multiplier swimming on the surface (Medium depth).</summary>
    public float MediumSpeed { get; init; } = 0.50f;

    /// <summary>Gets the speed multiplier diving (Deep depth).</summary>
    public float DeepSpeed { get; init; } = 0.45f;

    /// <summary>Gets the speed multiplier diving in the abyss.</summary>
    public float AbyssSpeed { get; init; } = 0.40f;

    /// <summary>Gets the full oxygen pool in seconds of diving.</summary>
    public float MaxOxygenSeconds { get; init; } = 30f;

    /// <summary>Gets the oxygen used per second diving in Deep water.</summary>
    public float DeepDrainPerSecond { get; init; } = 1f;

    /// <summary>Gets the oxygen used per second diving in the abyss.</summary>
    public float AbyssDrainPerSecond { get; init; } = 1.5f;

    /// <summary>Gets the oxygen regained per second anywhere but diving.</summary>
    public float RefillPerSecond { get; init; } = 10f;

    /// <summary>Gets the status effect (<c>status_effects.json</c> key) applied while out of oxygen.</summary>
    public string DrowningEffect { get; init; } = "drowning";

    /// <summary>Gets the swim state for a depth tier.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The swim state.</returns>
    public static SwimState SwimStateOf(WaterDepthTier tier) => tier switch
    {
        WaterDepthTier.Dry => SwimState.Walking,
        WaterDepthTier.Shore or WaterDepthTier.Shallow => SwimState.Wading,
        WaterDepthTier.Medium => SwimState.Swimming,
        WaterDepthTier.Deep or WaterDepthTier.Abyss => SwimState.Diving,
        _ => SwimState.Walking,
    };

    /// <summary>
    /// Gets the movement speed multiplier at a depth: each tier's value holds at the depth the tier starts at, and
    /// the speed blends linearly between them, so it never steps at a tile border.
    /// </summary>
    /// <param name="depth">Water depth (<see cref="WaterDepth"/>); 0 or less is dry land.</param>
    /// <returns>The multiplier; 1 on dry land.</returns>
    public float SpeedAtDepth(float depth)
    {
        if (!(depth > 0f))
        {
            return 1f;
        }

        Span<float> depths = [WaterDepth.MinDepth, WaterDepth.ShallowFrom, WaterDepth.MediumFrom, WaterDepth.DeepFrom, WaterDepth.AbyssFrom];
        Span<float> speeds = [this.ShoreSpeed, this.ShallowSpeed, this.MediumSpeed, this.DeepSpeed, this.AbyssSpeed];
        if (depth <= depths[0])
        {
            return speeds[0];
        }

        for (int i = 1; i < depths.Length; i++)
        {
            if (depth < depths[i])
            {
                float t = (depth - depths[i - 1]) / (depths[i] - depths[i - 1]);
                return speeds[i - 1] + ((speeds[i] - speeds[i - 1]) * t);
            }
        }

        return speeds[^1];
    }

    /// <summary>Gets the oxygen used per second at a depth tier: zero unless diving.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>Seconds of oxygen per second.</returns>
    public float DrainPerSecond(WaterDepthTier tier) => tier switch
    {
        WaterDepthTier.Deep => this.DeepDrainPerSecond,
        WaterDepthTier.Abyss => this.AbyssDrainPerSecond,
        WaterDepthTier.Dry or WaterDepthTier.Shore or WaterDepthTier.Shallow or WaterDepthTier.Medium => 0f,
        _ => 0f,
    };

    /// <summary>Parses and validates <c>water.json</c>.</summary>
    /// <param name="json">The document.</param>
    /// <returns>The rules.</returns>
    /// <exception cref="InvalidDataException">The document is malformed or a value is out of range.</exception>
    public static WaterRules LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement speed = Required(root, "speed");
            JsonElement oxygen = Required(root, "oxygen");
            JsonElement drain = Required(oxygen, "drainPerSecond");
            WaterRules rules = new()
            {
                ShoreSpeed = Number(speed, "shore"),
                ShallowSpeed = Number(speed, "shallow"),
                MediumSpeed = Number(speed, "medium"),
                DeepSpeed = Number(speed, "deep"),
                AbyssSpeed = Number(speed, "abyss"),
                MaxOxygenSeconds = Number(oxygen, "maxSeconds"),
                DeepDrainPerSecond = Number(drain, "deep"),
                AbyssDrainPerSecond = Number(drain, "abyss"),
                RefillPerSecond = Number(oxygen, "refillPerSecond"),
                DrowningEffect = Required(oxygen, "drowningEffect").GetString() ?? string.Empty,
            };
            rules.Validate();
            return rules;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"water.json is invalid: {ex.Message}", ex);
        }
    }

    private void Validate()
    {
        Range(this.ShoreSpeed, 0.05f, 1.5f, "speed.shore");
        Range(this.ShallowSpeed, 0.05f, 1.5f, "speed.shallow");
        Range(this.MediumSpeed, 0.05f, 1.5f, "speed.medium");
        Range(this.DeepSpeed, 0.05f, 1.5f, "speed.deep");
        Range(this.AbyssSpeed, 0.05f, 1.5f, "speed.abyss");
        Range(this.MaxOxygenSeconds, 1f, 600f, "oxygen.maxSeconds");
        Range(this.DeepDrainPerSecond, 0f, 100f, "oxygen.drainPerSecond.deep");
        Range(this.AbyssDrainPerSecond, 0f, 100f, "oxygen.drainPerSecond.abyss");
        Range(this.RefillPerSecond, 0.01f, 1000f, "oxygen.refillPerSecond");
        if (string.IsNullOrWhiteSpace(this.DrowningEffect))
        {
            throw new InvalidDataException("water.json oxygen.drowningEffect must name a status effect.");
        }
    }

    private static JsonElement Required(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new InvalidDataException($"water.json is missing '{name}'.");

    private static float Number(JsonElement parent, string name) => Required(parent, name).GetSingle();

    private static void Range(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
        {
            throw new InvalidDataException($"water.json '{name}' = {value} is outside [{min}, {max}].");
        }
    }
}
