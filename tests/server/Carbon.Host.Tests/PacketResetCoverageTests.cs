// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nalix.Abstractions;
using Nalix.Abstractions.Primitives;
using Nalix.Abstractions.Serialization;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-043, security: a pooled packet is reused for another player, so <see cref="IPoolable.ResetForPool"/> must clear
/// every serialized field. Covered by reflection over every server-to-client packet type in the contracts assemblies, so
/// a new packet or field is covered without editing a list.
/// </summary>
public sealed partial class PacketResetCoverageTests
{
    public static TheoryData<Type> ServerToClientPackets()
    {
        TheoryData<Type> data = [];
        foreach (Type type in AllServerToClientPackets())
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void ServerToClientPackets_AreFound() =>
        AllServerToClientPackets().Should().Contain(t => t.Name == "EntityDeltaBatch").And.HaveCountGreaterThan(20);

    [Theory]
    [MemberData(nameof(ServerToClientPackets))]
    public void ResetForPool_EverySerializedField_KeepsNothingOfThePreviousSend(Type packetType)
    {
        ArgumentNullException.ThrowIfNull(packetType);
        IPoolable packet = (IPoolable)Activator.CreateInstance(packetType)!;
        object fresh = Activator.CreateInstance(packetType)!;
        List<(PropertyInfo Property, object Sample)> written = [];
        foreach (PropertyInfo property in SerializedProperties(packetType))
        {
            object sample = Sample(property.PropertyType, property.GetValue(fresh));
            property.SetValue(packet, sample);
            written.Add((property, sample));
        }

        packet.ResetForPool();

        foreach ((PropertyInfo property, object sample) in written)
        {
            Leaks(property.GetValue(packet), sample).Should().BeFalse(
                $"{packetType.Name}.{property.Name} still carries the previous send's value after ResetForPool");
        }
    }

    /// <summary>
    /// The fields where a reset packet differs from a freshly constructed one: the reset gives the type default, the
    /// constructor the initializer. Each is set by the sink on every send (next test). A new field with an initializer
    /// fails the test below until it is added here and set by its sender.
    /// </summary>
    private static readonly string[] s_knownResetDeviations =
    [
        "EntityEquipmentChanged.Level",
        "EntitySpawn.Level",
        "PlayerStatsSync.EatProgress",
        "PlayerStatsSync.Level",
        "PlayerStatsSync.XpToNextLevel",
    ];

    [Fact]
    public void ResetForPool_EveryPooledType_MatchesAFreshPacketExceptTheKnownDeviations()
    {
        string sink = File.ReadAllText(Path.Combine(RepositoryRoot(), "server", "Carbon.Host", "WorldOutboundSink.cs"));
        HashSet<string> rented = [.. RentedType().Matches(sink).Select(m => m.Groups[1].Value)];
        List<string> deviations = [];
        foreach (Type type in AllServerToClientPackets().Where(t => rented.Contains(t.Name)))
        {
            IPoolable reset = (IPoolable)Activator.CreateInstance(type)!;
            foreach (PropertyInfo property in SerializedProperties(type))
            {
                property.SetValue(reset, Sample(property.PropertyType, property.GetValue(reset)));
            }

            reset.ResetForPool();
            object fresh = Activator.CreateInstance(type)!;
            deviations.AddRange(SerializedProperties(type)
                .Where(p => !SameValue(p.GetValue(reset), p.GetValue(fresh)))
                .Select(p => $"{type.Name}.{p.Name}"));
        }

        deviations.Order(StringComparer.Ordinal).Should().Equal(s_knownResetDeviations,
            "a reset packet must equal a fresh one, or the field must be listed here and set by the sink on every send");
    }

    [Fact]
    public void ResetForPool_FieldsWithAnInitializer_AreAlwaysSetByTheSink()
    {
        // A reset field goes back to its type's default, not to its initializer (Level = 1, EatProgress = -1, ...). A pooled
        // packet only matches a fresh one if the sink assigns such fields on every send.
        string sink = File.ReadAllText(Path.Combine(RepositoryRoot(), "server", "Carbon.Host", "WorldOutboundSink.cs"));
        HashSet<string> rented = [.. RentedType().Matches(sink).Select(m => m.Groups[1].Value)];
        List<string> unset = [];
        foreach (Type type in AllServerToClientPackets().Where(t => rented.Contains(t.Name)))
        {
            IPoolable reset = (IPoolable)Activator.CreateInstance(type)!;
            reset.ResetForPool();
            object fresh = Activator.CreateInstance(type)!;
            foreach (PropertyInfo property in SerializedProperties(type))
            {
                if (!SameValue(property.GetValue(reset), property.GetValue(fresh)) &&
                    !Regex.IsMatch(sink, $@"\.{property.Name}\s*=[^=]", RegexOptions.CultureInvariant))
                {
                    unset.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        rented.Should().HaveCountGreaterThan(15, "the sink leases its packets with Rent<T>()");
        unset.Should().BeEmpty("a pooled packet would send the type default instead of the field's initializer");
    }

    private static List<Type> AllServerToClientPackets() =>
        [.. Directory.GetFiles(AppContext.BaseDirectory, "Carbon.Contracts.*.dll")
            .Select(Assembly.LoadFrom)
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Namespace?.EndsWith(".S2C", StringComparison.Ordinal) == true &&
                typeof(IPoolable).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)];

    private static IEnumerable<PropertyInfo> SerializedProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<SerializeOrderAttribute>() is not null);

    // A value distinct from the type's default and from the field's initializer, so a reset to either is told apart from
    // a leftover.
    private static object Sample(Type type, object? initial)
    {
        if (type == typeof(string))
        {
            return "previous player";
        }

        if (type == typeof(bool))
        {
            return true;
        }

        if (type == typeof(Guid))
        {
            return Guid.NewGuid();
        }

        if (type == typeof(Bytes32))
        {
            Bytes32 value = default;
            MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1)).Fill(0x5A);
            return value;
        }

        if (type.IsEnum)
        {
            object candidate = Enum.ToObject(type, 0x5A);
            return Equals(candidate, initial) ? Enum.ToObject(type, 0x3C) : candidate;
        }

        if (type.IsPrimitive)
        {
            object candidate = Convert.ChangeType(91, type, System.Globalization.CultureInfo.InvariantCulture);
            return Equals(candidate, initial) ? Convert.ChangeType(37, type, System.Globalization.CultureInfo.InvariantCulture) : candidate;
        }

        if (type.IsArray)
        {
            Type element = type.GetElementType()!;
            Array array = Array.CreateInstance(element, 3);
            for (int i = 0; i < array.Length; i++)
            {
                array.SetValue(Sample(element, null), i);
            }

            return array;
        }

        throw new NotSupportedException($"Add a sample for {type} so its reset is covered.");
    }

    private static bool Leaks(object? afterReset, object sample) => afterReset switch
    {
        null => false,
        Array array => ReferenceEquals(array, sample) || (array.Length > 0 && SameValue(array, sample)),
        _ => SameValue(afterReset, sample),
    };

    private static bool SameValue(object? a, object? b) => a is Array x && b is Array y
        ? x.Length == y.Length && x.Cast<object?>().SequenceEqual(y.Cast<object?>())
        : Equals(a, b);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Carbon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Carbon.slnx not found above the test output.");
    }

    [GeneratedRegex(@"Rent<(\w+)>\(\)", RegexOptions.CultureInvariant)]
    private static partial Regex RentedType();
}
