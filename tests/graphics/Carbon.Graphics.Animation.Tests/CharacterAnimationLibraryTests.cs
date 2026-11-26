// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using System.Text;
using Carbon.Graphics.Animation.Characters;
using Carbon.Graphics.Animation.Clips;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.OpenGL.Abstractions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Carbon.Graphics.Animation.Tests;

public sealed class CharacterAnimationLibraryTests
{
    private const string MiniManifest = """
        {
          // comments are allowed
          "version": 1,
          "sheetRoot": "../thirdparty",
          "archetypes": [
            {
              "key": "hero", "frameWidth": 96, "frameHeight": 96, "pivotX": 46, "pivotY": 58, "bodyHeight": 20, "heightTiles": 1.25,
              "clips": {
                "idle": { "sheet": "hero_idle.png", "fps": 10 },
                "walk": { "sheet": "hero_walk.png", "frames": 4, "fps": 8 },
                "attack_melee": { "sheet": "hero_attack.png", "fps": 10 },
                "unknown_clip": { "sheet": "ignored.png" }
              }
            },
            {
              "key": "ghost", "frameWidth": 96, "frameHeight": 96,
              "clips": { "idle": { "sheet": "missing.png" } }
            }
          ]
        }
        """;

    [Fact]
    public void Parse_MiniManifest_ReadsArchetypesAndClips()
    {
        CharacterSpriteManifest manifest = CharacterSpriteManifest.Parse(Encoding.UTF8.GetBytes(MiniManifest));

        manifest.SheetRoot.Should().Be("../thirdparty");
        manifest.Archetypes.Should().HaveCount(2);
        manifest.Archetypes[0].Key.Should().Be("hero");
        manifest.Archetypes[0].PivotY.Should().Be(58f);
        manifest.Archetypes[0].Clips["walk"].Frames.Should().Be(4);
    }

    [Fact]
    public void Build_MissingSheet_SkipsArchetypeAndReportsSheet()
    {
        using CharacterAnimationLibrary library = BuildMini();

        library.TryGet("ghost", out _).Should().BeFalse();
        library.MissingSheets.Should().Contain("missing.png");
        library.TryGet("hero", out _).Should().BeTrue();
    }

    [Fact]
    public void Build_FrameCount_DefaultsToSheetCellsAndHonoursExplicitCount()
    {
        using CharacterAnimationLibrary library = BuildMini();
        library.TryGet("hero", out CharacterAnimationSet hero).Should().BeTrue();

        hero.GetClip(CharacterClip.Idle).FrameCount.Should().Be(6);
        hero.GetClip(CharacterClip.Walk).FrameCount.Should().Be(4);
        hero.GetClip(CharacterClip.Idle).PlaybackMode.Should().Be(AnimationPlaybackMode.Loop);
        hero.GetClip(CharacterClip.AttackMelee).PlaybackMode.Should().Be(AnimationPlaybackMode.Once);
    }

    [Fact]
    public void Build_FrameRegions_UsePivotAsOriginAndStepAcrossTheStrip()
    {
        using CharacterAnimationLibrary library = BuildMini();
        library.TryGet("hero", out CharacterAnimationSet hero).Should().BeTrue();

        AnimationFrame third = hero.GetClip(CharacterClip.Idle)[2];
        third.Region.Bounds.Should().Be(new RectangleF(192f, 0f, 96f, 96f));
        third.Origin.Should().Be(new Vector2(46f, 58f));
        hero.GetScale(32f).Should().BeApproximately(2f, 0.0001f);
    }

    [Fact]
    public void GetClip_MissingClips_FollowFallbackChain()
    {
        using CharacterAnimationLibrary library = BuildMini();
        library.TryGet("hero", out CharacterAnimationSet hero).Should().BeTrue();

        hero.Has(CharacterClip.Cast).Should().BeFalse();
        hero.GetClip(CharacterClip.Cast).Name.Should().Be("attack_melee");
        hero.GetClip(CharacterClip.AttackRanged).Name.Should().Be("attack_melee");
        hero.GetClip(CharacterClip.Hurt).Name.Should().Be("idle");
        hero.GetClip(CharacterClip.Death).Name.Should().Be("idle");
    }

    [Fact]
    public void GetFrame_LoopingClipWrapsAndOneShotHoldsLastFrame()
    {
        using CharacterAnimationLibrary library = BuildMini();
        library.TryGet("hero", out CharacterAnimationSet hero).Should().BeTrue();

        CharacterAnimationState state = default;
        state.Play(CharacterClip.Idle);
        state.Advance(0.65f); // 6 frames at 10 fps = 0.6 s, so 0.05 s into the second loop
        hero.GetFrame(in state).Region.Bounds.X.Should().Be(0f);

        state.Play(CharacterClip.AttackMelee);
        state.Advance(5f);
        hero.GetFrame(in state).Region.Bounds.X.Should().Be(7 * 96f);
    }

    [Fact]
    public void GetFrameAtProgress_MapsPhaseProgressOntoFrames()
    {
        using CharacterAnimationLibrary library = BuildMini();
        library.TryGet("hero", out CharacterAnimationSet hero).Should().BeTrue();

        hero.GetFrameAtProgress(CharacterClip.AttackMelee, 0f).Region.Bounds.X.Should().Be(0f);
        hero.GetFrameAtProgress(CharacterClip.AttackMelee, 0.5f).Region.Bounds.X.Should().Be(4 * 96f);
        hero.GetFrameAtProgress(CharacterClip.AttackMelee, 1f).Region.Bounds.X.Should().Be(7 * 96f);
    }

    [Fact]
    public void UpdateLocomotion_VelocityPicksWalkOrIdleAndFacing()
    {
        using CharacterAnimationLibrary library = BuildMini();
        library.TryGet("hero", out CharacterAnimationSet hero).Should().BeTrue();
        CharacterAnimationState state = default;

        state.UpdateLocomotion(hero, new Vector2(-120f, 0f), 0.1f);
        state.Clip.Should().Be(CharacterClip.Walk);
        state.FacingLeft.Should().BeTrue();
        hero.GetEffects(in state).Should().Be(SpriteEffects.FlipHorizontally);

        state.UpdateLocomotion(hero, new Vector2(0f, 120f), 0.1f);
        state.FacingLeft.Should().BeTrue("moving straight down keeps the previous facing");

        state.UpdateLocomotion(hero, Vector2.Zero, 0.1f);
        state.Clip.Should().Be(CharacterClip.Idle);
        state.Time.Should().BeApproximately(0.1f, 0.0001f);
    }

    [Fact]
    public void UpdateLocomotion_OneShotAttack_FinishesBeforeReturningToWalk()
    {
        using CharacterAnimationLibrary library = BuildMini();
        library.TryGet("hero", out CharacterAnimationSet hero).Should().BeTrue();
        CharacterAnimationState state = default;

        state.Play(CharacterClip.AttackMelee);
        state.UpdateLocomotion(hero, new Vector2(200f, 0f), 0.4f);
        state.Clip.Should().Be(CharacterClip.AttackMelee);

        state.UpdateLocomotion(hero, new Vector2(200f, 0f), 0.5f); // 0.9 s > 0.8 s duration
        state.UpdateLocomotion(hero, new Vector2(200f, 0f), 0.1f);
        state.Clip.Should().Be(CharacterClip.Walk);
    }

    [Fact]
    public void RepositoryManifest_EveryArchetypeDefinesCoreClips()
    {
        string path = FindRepositoryFile(Path.Combine("resources", "characters", "character_sprites.json"));
        CharacterSpriteManifest manifest = CharacterSpriteManifest.Parse(File.ReadAllBytes(path));

        manifest.Archetypes.Should().Contain(a => a.Key == "human_soldier");
        foreach (CharacterArchetypeDefinition archetype in manifest.Archetypes)
        {
            archetype.FrameWidth.Should().BePositive(archetype.Key);
            archetype.BodyHeight.Should().BePositive(archetype.Key);
            foreach (string clip in new[] { "idle", "walk", "attack_melee", "hurt", "death" })
            {
                archetype.Clips.Should().ContainKey(clip, $"{archetype.Key} needs a {clip} clip");
            }

            foreach (string clipName in archetype.Clips.Keys)
            {
                CharacterClips.TryParse(clipName, out _).Should().BeTrue($"{archetype.Key}.{clipName} must be a known clip");
            }
        }
    }

    private static CharacterAnimationLibrary BuildMini()
    {
        CharacterSpriteManifest manifest = CharacterSpriteManifest.Parse(Encoding.UTF8.GetBytes(MiniManifest));
        return CharacterAnimationLibrary.Build(manifest, sheet => sheet switch
        {
            "hero_idle.png" => FakeTexture(576),
            "hero_walk.png" => FakeTexture(768),
            "hero_attack.png" => FakeTexture(768),
            _ => null,
        });
    }

    private static ITexture FakeTexture(uint width)
    {
        ITexture texture = Substitute.For<ITexture>();
        texture.Width.Returns(width);
        texture.Height.Returns(96u);
        return texture;
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(relativePath);
    }
}
