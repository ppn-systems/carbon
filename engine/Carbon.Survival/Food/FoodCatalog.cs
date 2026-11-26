// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Carbon.Survival.Serialization;

namespace Carbon.Survival.Food;

/// <summary>
/// Immutable lookup of <see cref="FoodDefinition"/> by item id and by dense index (the index is what
/// unmanaged components store).
/// </summary>
public sealed class FoodCatalog
{
    /// <summary>Largest allowed eat duration in seconds.</summary>
    public const float MaxEatSeconds = 30f;

    /// <summary>Largest allowed absolute mood offset of a food memory.</summary>
    public const float MaxMoodOffset = 30f;

    private readonly FoodDefinition[] _foods;
    private readonly FrozenDictionary<string, int> _indexById;

    /// <summary>
    /// Initializes a new instance of the <see cref="FoodCatalog"/> class and validates every entry.
    /// </summary>
    /// <param name="foods">Food definitions.</param>
    /// <param name="foodMax">Food capacity from <see cref="Config.NeedsConfig.FoodMax"/>; nutrition may not exceed it.</param>
    /// <exception cref="InvalidDataException">Thrown on duplicate ids or out-of-range values.</exception>
    public FoodCatalog(IEnumerable<FoodDefinition> foods, float foodMax)
    {
        ArgumentNullException.ThrowIfNull(foods);

        List<FoodDefinition> list = [];
        Dictionary<string, int> index = new(StringComparer.Ordinal);
        foreach (FoodDefinition food in foods)
        {
            if (food is null)
            {
                throw new InvalidDataException("food.json contains a null entry.");
            }

            Validate(food, foodMax);
            if (!index.TryAdd(food.ItemId, list.Count))
            {
                throw new InvalidDataException($"food.json has duplicate itemId '{food.ItemId}'.");
            }

            list.Add(food);
        }

        _foods = [.. list];
        _indexById = index.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Gets the number of foods.</summary>
    public int Count => _foods.Length;

    /// <summary>Gets all foods in file order.</summary>
    public ReadOnlySpan<FoodDefinition> All => _foods;

    /// <summary>
    /// Parses and validates a <c>food.json</c> payload (a JSON array of <see cref="FoodDefinition"/>).
    /// </summary>
    /// <param name="json">JSON array text.</param>
    /// <param name="foodMax">Food capacity from <see cref="Config.NeedsConfig.FoodMax"/>.</param>
    /// <returns>The validated catalog.</returns>
    public static FoodCatalog LoadFromJson(string json, float foodMax)
    {
        ArgumentNullException.ThrowIfNull(json);

        FoodDefinition[] foods = JsonSerializer.Deserialize(json, SurvivalJsonContext.Default.FoodDefinitionArray) ?? [];
        return new FoodCatalog(foods, foodMax);
    }

    /// <summary>
    /// Looks up the dense index of a food by item id.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="index">Dense index when found.</param>
    /// <returns><see langword="true"/> when the item is edible.</returns>
    public bool TryGetIndex(string itemId, out int index) => _indexById.TryGetValue(itemId, out index);

    /// <summary>
    /// Gets a food by dense index.
    /// </summary>
    /// <param name="index">Dense index from <see cref="TryGetIndex"/>.</param>
    /// <returns>The food definition.</returns>
    public FoodDefinition Get(int index) => _foods[index];

    private static void Validate(FoodDefinition food, float foodMax)
    {
        if (string.IsNullOrWhiteSpace(food.ItemId))
        {
            throw new InvalidDataException("food.json entry is missing itemId.");
        }

        Require(food.Nutrition > 0f && food.Nutrition <= foodMax, food, "nutrition", $"(0, {foodMax}]");
        Require(food.PoisonChance >= 0f && food.PoisonChance <= 1f, food, "poisonChance", "[0, 1]");
        Require(MathF.Abs(food.MoodOffset) <= MaxMoodOffset, food, "moodOffset", $"[-{MaxMoodOffset}, {MaxMoodOffset}]");
        Require(food.MoodDurationSeconds >= 0f && food.MoodDurationSeconds <= 3_600f, food, "moodDurationSeconds", "[0, 3600]");
        Require(food.EatSeconds > 0f && food.EatSeconds <= MaxEatSeconds, food, "eatSeconds", $"(0, {MaxEatSeconds}]");
        Require(Enum.IsDefined(food.Quality), food, "quality", "raw|simple|fine|lavish");
    }

    private static void Require(bool condition, FoodDefinition food, string field, string range)
    {
        if (!condition)
        {
            throw new InvalidDataException($"food.json '{food.ItemId}'.{field} is outside {range}.");
        }
    }
}
