// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Carbon.Simulation.ECS;
using FluentAssertions;
using Xunit;

namespace Carbon.Creatures.Tests;

/// <summary>
/// ADR-044: a released attachment goes to the next entity, for example one monster's stat sheet to another monster. So
/// <see cref="IReusableAttachment.ResetForReuse"/> must bring every field back to a fresh instance's value. Every
/// reusable type in the Carbon assemblies is covered by reflection over its fields, so a new type or field is covered
/// without editing a list.
/// </summary>
public sealed class AttachmentResetCoverageTests
{
    /// <summary>Fields allowed to differ from a fresh instance after a reset. Empty: a reset equals a fresh instance.</summary>
    private static readonly string[] s_knownResetDeviations = [];

    public static TheoryData<Type> ReusableTypes()
    {
        TheoryData<Type> data = [];
        foreach (Type type in AllReusableTypes())
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void ReusableTypes_AreFound() =>
        AllReusableTypes().Select(t => t.Name).Should().Contain(["StatSheet", "ThreatTable", "PathWaypointBuffer"]);

    [Theory]
    [MemberData(nameof(ReusableTypes))]
    public void ResetForReuse_AfterEveryFieldWasWritten_MatchesAFreshInstance(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        IReusableAttachment used = (IReusableAttachment)CreateFresh(type);
        object fresh = CreateFresh(type);
        foreach (FieldInfo field in InstanceFields(type))
        {
            Dirty(field, used, field.GetValue(fresh));
        }

        used.ResetForReuse();

        List<string> deviations = [];
        foreach (FieldInfo field in InstanceFields(type))
        {
            if (!MatchesFresh(field.GetValue(used), field.GetValue(fresh)))
            {
                deviations.Add($"{type.Name}.{field.Name}");
            }
        }

        deviations.Should().BeSubsetOf(s_knownResetDeviations,
            "a reset instance must equal a fresh one: nothing of the previous entity (modifiers, threat, path) may reach the next");
    }

    private static List<Type> AllReusableTypes() =>
        [.. Directory.GetFiles(AppContext.BaseDirectory, "Carbon.*.dll")
            .Where(p => !Path.GetFileName(p).Contains(".Tests", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom)
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IReusableAttachment).IsAssignableFrom(t))
            .Distinct()
            .OrderBy(t => t.FullName, StringComparer.Ordinal)];

    // As the store's factories construct it: the parameterless constructor, or the one whose parameters all have defaults.
    private static object CreateFresh(Type type)
    {
        ConstructorInfo constructor = type.GetConstructors().Single(c => c.GetParameters().All(p => p.HasDefaultValue));
        return constructor.Invoke([.. constructor.GetParameters().Select(p => p.DefaultValue)]);
    }

    private static IEnumerable<FieldInfo> InstanceFields(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    // Writes a value distinct from the fresh one: arrays and lists get filled elements, and a non-readonly array is
    // replaced by a longer one, as when a table grows past its initial capacity.
    private static void Dirty(FieldInfo field, object target, object? freshValue)
    {
        Type type = field.FieldType;
        if (type.IsArray)
        {
            Array current = (Array)field.GetValue(target)!;
            Array array = field.IsInitOnly ? current : Array.CreateInstance(type.GetElementType()!, current.Length * 2);
            for (int i = 0; i < array.Length; i++)
            {
                array.SetValue(Filled(type.GetElementType()!), i);
            }

            if (!field.IsInitOnly)
            {
                field.SetValue(target, array);
            }

            return;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            IList list = (IList)field.GetValue(target)!;
            for (int i = 0; i < 3; i++)
            {
                _ = list.Add(Filled(type.GetGenericArguments()[0]));
            }

            return;
        }

        if (type.IsValueType)
        {
            object sample = Filled(type);
            field.SetValue(target, Equals(sample, freshValue) ? Activator.CreateInstance(type) : sample);
            return;
        }

        throw new NotSupportedException($"Add a way to dirty {field.DeclaringType!.Name}.{field.Name} ({type}) so its reset is covered.");
    }

    private static bool MatchesFresh(object? afterReset, object? fresh) => (afterReset, fresh) switch
    {
        // Arrays keep their length (capacity) but must be zeroed, at least as long as a fresh one.
        (Array reset, Array initial) => reset.Length >= initial.Length && reset.Cast<object?>().All(e => Equals(e, Default(reset.GetType().GetElementType()!))),

        // Lists keep their capacity but must be empty, like a fresh one.
        (IList reset, IList initial) => reset.Count == initial.Count,
        _ => Equals(afterReset, fresh),
    };

    private static object? Default(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    // Every byte 0x5A: a value distinct from the type default for any unmanaged type.
    private static object Filled(Type type)
    {
        MethodInfo method;
        try
        {
            // The unmanaged constraint rejects a type that holds references.
            method = typeof(AttachmentResetCoverageTests).GetMethod(nameof(FilledValue), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type);
        }
        catch (ArgumentException e)
        {
            throw new NotSupportedException($"Add a sample for {type} so its reset is covered.", e);
        }

        return method.Invoke(null, null)!;
    }

    private static T FilledValue<T>()
        where T : unmanaged
    {
        T value = default;
        MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1)).Fill(0x5A);
        return value;
    }
}
