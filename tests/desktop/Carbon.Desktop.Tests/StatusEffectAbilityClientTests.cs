// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Combat.Statuses.Config;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.StatusEffects;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.Atlases;
using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>ADR-036 D9–D10 on the client: telegraph tints, the blindness edge and the new magnitude lines.</summary>
public sealed class StatusEffectAbilityClientTests
{
    private static readonly StatusEffectCatalog s_shipped = StatusEffectCatalog.LoadShippedDefaults();

    [Fact]
    public void Resolve_PoisonArcherAndFireWraith_TintTheTelegraphByTheirFirstEffect()
    {
        IWorldReadModel world = World();
        world.ResolveMonsterNameKey(105).Returns("monster.skeleton_archer.name");
        world.ResolveMonsterNameKey(104).Returns("monster.ash_wraith.name");
        MonsterEffectTints tints = new();

        tints.Resolve(world, 105).Should().Be(StatusEffectTints.Poison, "poison-tipped arrows aim green");
        tints.Resolve(world, 104).Should().Be(StatusEffectTints.Burn, "the wraith's first effect is burning");
        tints.Resolve(world, 999).Should().BeNull("an unknown archetype keeps the attack kind's colour");
    }

    [Fact]
    public void Resolve_SameArchetypeTwice_LooksTheNameUpOnce()
    {
        IWorldReadModel world = World();
        world.ResolveMonsterNameKey(106).Returns("monster.orc_shaman.name");
        MonsterEffectTints tints = new();

        _ = tints.Resolve(world, 106);
        tints.Resolve(world, 106).Should().Be(StatusEffectTints.Weakness);

        _ = world.Received(1).ResolveMonsterNameKey(106);
    }

    [Fact]
    public void VignetteResolve_Blind_DarkensMoreAtASmallerViewAndFrostbiteIsColdNotPoison()
    {
        LocalStatusEffect blindOne = new(12, 1, 4f, 5f, 0.5f, 1, 104);
        LocalStatusEffect blindThree = new(12, 1, 4f, 5f, 0.3f, 1, 104, Level: 3);

        (ColorRgba color, float alpha) = DamageVignetteWidget.Resolve(World(blindOne), animTime: 0f);
        float darker = DamageVignetteWidget.Resolve(World(blindThree), animTime: 0f).Alpha;

        color.Should().Be(new ColorRgba(6, 6, 10, 255));
        alpha.Should().BeApproximately(204f, 0.01f, "255 x (1 - 0.5) x 1.6");
        darker.Should().BeGreaterThan(alpha, "blindness III hides more");
        DamageVignetteWidget.Resolve(World(new LocalStatusEffect(14, 1, 5f, 6f, 0.5f, 1, 103)), animTime: 0f).Color
            .Should().Be(new ColorRgba(120, 180, 240, 255), "frostbite chills: the cold edge, not the poison green");
    }

    [Fact]
    public void Prepare_WeaknessAndBlindness_ShowTheirMultiplierInTheDetails()
    {
        ILocalizer previous = GameText.Current;
        GameText.Current = new Localizer(LocaleCatalog.Load(LocaleCatalog.TryFindDirectory()!));
        try
        {
            using BitmapFont font = CreateFont();
            StatusEffectStripWidget strip = new();
            strip.Open(11);
            strip.Prepare(World(new LocalStatusEffect(11, 1, 6f, 8f, 0.65f, 1, 106, Level: 2)), font, mouse: null, viewportWidth: 1280);
            strip.PanelTitle.Should().Be("Weakness II");
            strip.PanelLines.Should().Contain("Damage dealt ×0.65");

            strip.Open(12);
            strip.Prepare(World(new LocalStatusEffect(12, 1, 4f, 5f, 0.5f, 1, 104)), font, mouse: null, viewportWidth: 1280);
            strip.PanelLines.Should().Contain("View range ×0.50");
        }
        finally
        {
            GameText.Current = previous;
        }
    }

    private static IWorldReadModel World(params LocalStatusEffect[] effects)
    {
        IWorldReadModel world = Substitute.For<IWorldReadModel>();
        world.StatusEffectDefinitions.Returns(s_shipped);
        world.LocalStatusEffects.Returns(effects);
        world.LocalDamageVignetteTimer.Returns(0f);
        return world;
    }

    private static BitmapFont CreateFont()
    {
        BitmapFontAtlas atlas = new(Substitute.For<ITexture>());
        for (char c = ' '; c <= '~'; c++)
        {
            atlas.AddGlyph(new FontGlyph(c, new RectangleF(0f, 0f, 8f, 16f), 0f, 0f, 8f));
        }

        return new BitmapFont("test", atlas, new FontMetrics(fontSize: 16f, lineHeight: 20f, baseLine: 16f, spaceWidth: 6f));
    }
}
