// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Combat.Statuses;
using Carbon.Combat.Statuses.Config;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.StatusEffects;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Top-right status strip (ADR-029 D10): one vector icon per active effect with a duration ring, the stack count and a
/// countdown; a hover tooltip; and a details panel (name, description, remaining time, source, magnitude) opened by a
/// click on an icon and closed by a click elsewhere or Esc. Text comes from the <c>[status_effects]</c> section.
/// </summary>
internal sealed class StatusEffectStripWidget
{
    private const int MaxIcons = 12;
    private const float PanelPadding = 12f;
    private const float LineHeight = 18f;

    private static readonly ColorRgba s_debuffFrame = new(220, 80, 70, 255);
    private static readonly ColorRgba s_buffFrame = new(110, 200, 120, 255);
    private static readonly ColorRgba s_ring = new(250, 230, 160, 235);
    private static readonly ColorRgba s_dimText = new(190, 180, 160, 255);

    private readonly DerivedTextCache _names = new("status_effects.", ".name");
    private readonly DerivedTextCache _descriptions = new("status_effects.", ".desc");
    private readonly CachedText[] _countdowns = CreateTexts(MaxIcons);
    private readonly CachedText _tooltip = new();
    private readonly CachedText _tooltipTime = new();
    private readonly CachedText _remaining = new();
    private readonly CachedText _remainingTime = new();
    private readonly CachedText _stacks = new();
    private readonly CachedText _source = new();
    private readonly CachedText _magnitude = new();
    private readonly CachedText _interval = new();
    private readonly List<string> _panelLines = new(6);

    private int _count;
    private int _hovered = -1;
    private RectangleF _panel;
    private float _descriptionHeight;

    /// <summary>Gets the effect id whose details panel is open, or 0.</summary>
    public ushort SelectedEffectId { get; private set; }

    /// <summary>Gets a value indicating whether the details panel is open.</summary>
    public bool IsPanelOpen => this.SelectedEffectId != 0;

    /// <summary>
    /// Handles a left click: a click on an icon opens (or toggles) its details; while the panel is open any other click
    /// closes it. Returns whether the click was consumed.
    /// </summary>
    public bool HandleClick(Vector2 mouse, IReadOnlyList<LocalStatusEffect> effects, int viewportWidth, bool showMinimap)
    {
        ArgumentNullException.ThrowIfNull(effects);
        int hit = StatusEffectStripLayout.HitTest(mouse, Math.Min(effects.Count, MaxIcons), viewportWidth, showMinimap);
        if (hit >= 0)
        {
            ushort id = effects[hit].EffectId;
            this.SelectedEffectId = this.SelectedEffectId == id ? (ushort)0 : id;
            return true;
        }

        if (!this.IsPanelOpen)
        {
            return false;
        }

        bool insidePanel = _panel.Contains(mouse);
        if (!insidePanel)
        {
            this.SelectedEffectId = 0;
        }

        return true;
    }

    /// <summary>Gets whether <paramref name="mouse"/> is over an icon or the open panel (no state change).</summary>
    public bool IsOver(Vector2 mouse, IReadOnlyList<LocalStatusEffect> effects, int viewportWidth, bool showMinimap)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return StatusEffectStripLayout.HitTest(mouse, Math.Min(effects.Count, MaxIcons), viewportWidth, showMinimap) >= 0 ||
            (this.IsPanelOpen && _panel.Contains(mouse));
    }

    /// <summary>Closes the details panel; returns whether it was open (Esc priority chain).</summary>
    public bool Close()
    {
        bool wasOpen = this.IsPanelOpen;
        this.SelectedEffectId = 0;
        return wasOpen;
    }

    /// <summary>Opens the details of <paramref name="effectId"/> (screenshot previews).</summary>
    public void Open(ushort effectId) => this.SelectedEffectId = effectId;

    /// <summary>Computes hover, panel text and panel size for this frame. Call before the three draw passes.</summary>
    public void Prepare(IWorldReadModel world, BitmapFont font, Vector2? mouse, int viewportWidth, bool showMinimap)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(font);
        IReadOnlyList<LocalStatusEffect> effects = world.LocalStatusEffects;
        _count = Math.Min(effects.Count, MaxIcons);
        _hovered = mouse is Vector2 m ? StatusEffectStripLayout.HitTest(m, _count, viewportWidth, showMinimap) : -1;
        _panelLines.Clear();
        if (!this.IsPanelOpen)
        {
            return;
        }

        int selected = this.IndexOf(effects, this.SelectedEffectId);
        if (selected < 0 || !world.StatusEffectDefinitions.TryGet(this.SelectedEffectId, out StatusEffectDefinition def))
        {
            this.SelectedEffectId = 0; // The effect ended (or is unknown): the panel closes with it.
            return;
        }

        LocalStatusEffect effect = effects[selected];
        _panelLines.Add(effect.IsTimed
            ? _remaining.Format("status_effects.remaining", new TextArg("time", FormatTime(_remainingTime, effect.RemainingSeconds)))
            : GameText.Get("status_effects.while_active"));
        if (def.MaxStacks > 1)
        {
            _panelLines.Add(_stacks.Format("status_effects.stacks", new TextArg("count", effect.Stacks)));
        }

        _panelLines.Add(_source.Format("status_effects.source", new TextArg("source", ResolveSource(world, effect))));
        if (this.FormatMagnitude(def, effect) is { } magnitude)
        {
            _panelLines.Add(magnitude);
        }

        string description = _descriptions.Text(def.Key, def.Key);
        float textWidth = StatusEffectStripLayout.PanelWidth - (PanelPadding * 2f);
        _descriptionHeight = font.MeasureString(description, new TextLayoutOptions(textWidth, wordWrap: true), 1f).Height;
        float height = (PanelPadding * 2f) + 24f + _descriptionHeight + 8f + ((_panelLines.Count + 1) * LineHeight) + 6f;
        _panel = StatusEffectStripLayout.Panel(_count, height, viewportWidth, showMinimap);
    }

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin, IWorldReadModel world, int viewportWidth, bool showMinimap)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ArgumentNullException.ThrowIfNull(world);
        IReadOnlyList<LocalStatusEffect> effects = world.LocalStatusEffects;
        StatusEffectCatalog catalog = world.StatusEffectDefinitions;
        int count = Math.Min(_count, effects.Count);
        for (int i = 0; i < count; i++)
        {
            RectangleF rect = StatusEffectStripLayout.Icon(i, viewportWidth, showMinimap);
            bool buff = catalog.TryGet(effects[i].EffectId, out StatusEffectDefinition def) && def.Category == StatusCategory.Buff;
            skin.DrawInset(spriteBatch, rect, CarbonUiPalette.WindowFill, buff ? s_buffFrame : s_debuffFrame);
        }

        if (this.IsPanelOpen)
        {
            skin.DrawCard(spriteBatch, _panel, CarbonUiPalette.BorderOuter, bodyAlpha: 240);
        }
    }

    public void DrawGeometry(GeometryBatch batch, IWorldReadModel world, IVectorArtRepository? art, int viewportWidth, bool showMinimap)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(world);
        IReadOnlyList<LocalStatusEffect> effects = world.LocalStatusEffects;
        StatusEffectCatalog catalog = world.StatusEffectDefinitions;
        int count = Math.Min(_count, effects.Count);
        for (int i = 0; i < count; i++)
        {
            RectangleF rect = StatusEffectStripLayout.Icon(i, viewportWidth, showMinimap);
            Vector2 center = new(rect.X + (rect.Width * 0.5f), rect.Y + (rect.Height * 0.5f));
            if (art is not null && catalog.TryGet(effects[i].EffectId, out StatusEffectDefinition def) &&
                art.TryGetUiIcon(def.Icon, out CompiledItemVectorArt? icon))
            {
                VectorRigRenderer.DrawItemIcon(batch, icon, center, rect.Width - 10f);
            }

            if (effects[i].IsTimed)
            {
                DrawRing(batch, center, (rect.Width * 0.5f) - 1.5f, effects[i].RemainingFraction);
            }

            if (i == _hovered || effects[i].EffectId == this.SelectedEffectId)
            {
                batch.DrawRectangle(rect, s_ring, 1f);
            }
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, IWorldReadModel world, int viewportWidth, bool showMinimap)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(world);
        IReadOnlyList<LocalStatusEffect> effects = world.LocalStatusEffects;
        StatusEffectCatalog catalog = world.StatusEffectDefinitions;
        ColorRgba outline = CarbonUiPalette.TextOutline;
        int count = Math.Min(_count, effects.Count);
        Span<char> stack = stackalloc char[4];
        for (int i = 0; i < count; i++)
        {
            RectangleF rect = StatusEffectStripLayout.Icon(i, viewportWidth, showMinimap);
            LocalStatusEffect effect = effects[i];
            if (effect.Stacks > 1)
            {
                _ = effect.Stacks.TryFormat(stack, out int written, provider: System.Globalization.CultureInfo.InvariantCulture);
                spriteBatch.DrawOutlinedString(font, stack[..written], new Vector2(rect.X + rect.Width - 9f, rect.Y + rect.Height - 15f), CarbonUiPalette.TextPrimary, outline, 1, 0.85f);
            }

            if (effect.IsTimed)
            {
                string countdown = FormatShortTime(_countdowns[i], effect.RemainingSeconds);
                float width = font.MeasureString(countdown, TextLayoutOptions.Default, 0.8f).Width;
                spriteBatch.DrawOutlinedString(font, countdown, new Vector2(rect.X + ((rect.Width - width) * 0.5f), rect.Y + rect.Height + 1f), s_dimText, outline, 1, 0.8f);
            }
        }

        if (_hovered >= 0 && _hovered < count && !this.IsPanelOpen &&
            catalog.TryGet(effects[_hovered].EffectId, out StatusEffectDefinition hovered))
        {
            this.DrawTooltip(spriteBatch, font, effects[_hovered], hovered, viewportWidth, showMinimap);
        }

        if (this.IsPanelOpen && catalog.TryGet(this.SelectedEffectId, out StatusEffectDefinition def))
        {
            this.DrawPanelText(spriteBatch, font, def);
        }
    }

    /// <summary>Formats seconds with the shared <c>[time]</c> keys into <paramref name="cache"/>.</summary>
    internal static string FormatTime(CachedText cache, float seconds)
    {
        int total = (int)MathF.Ceiling(MathF.Max(0f, seconds));
        return total >= 60
            ? cache.Format("time.minutes", new TextArg("minutes", (int)MathF.Ceiling(total / 60f)))
            : cache.Format("time.seconds", new TextArg("seconds", total));
    }

    /// <summary>Formats seconds compactly for the label under an icon (<c>[status_effects] short.*</c>).</summary>
    internal static string FormatShortTime(CachedText cache, float seconds)
    {
        int total = (int)MathF.Ceiling(MathF.Max(0f, seconds));
        return total >= 60
            ? cache.Format("status_effects.short.minutes", new TextArg("minutes", (int)MathF.Ceiling(total / 60f)))
            : cache.Format("status_effects.short.seconds", new TextArg("seconds", total));
    }

    private static string ResolveSource(IWorldReadModel world, in LocalStatusEffect effect) =>
        (StatusSource)effect.SourceKind switch
        {
            StatusSource.Monster or StatusSource.HeavyHit when world.ResolveMonsterNameKey(effect.SourceId) is { } key => GameText.Get(key),
            StatusSource.Monster => GameText.Get("status_effects.source.monster"),
            StatusSource.HeavyHit => GameText.Get("status_effects.source.heavy_hit"),
            StatusSource.Food => GameText.Get("status_effects.source.food"),
            StatusSource.Needs => GameText.Get("status_effects.source.needs"),
            StatusSource.Sleep => GameText.Get("status_effects.source.sleep"),
            StatusSource.None => GameText.Get("status_effects.source.none"),
            _ => GameText.Get("status_effects.source.none"),
        };

    private static void DrawRing(GeometryBatch batch, Vector2 center, float radius, float fraction)
    {
        const int Segments = 28;
        int lit = (int)MathF.Ceiling(Segments * Math.Clamp(fraction, 0f, 1f));
        for (int s = 0; s < lit; s++)
        {
            // Clockwise from 12 o'clock; the lit arc shrinks as the effect runs out.
            float a0 = (-MathF.PI * 0.5f) + (s * MathF.Tau / Segments);
            float a1 = (-MathF.PI * 0.5f) + ((s + 1) * MathF.Tau / Segments);
            batch.DrawLine(
                center + (new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius),
                center + (new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius),
                s_ring,
                2f);
        }
    }

    private static CachedText[] CreateTexts(int count)
    {
        CachedText[] texts = new CachedText[count];
        for (int i = 0; i < count; i++)
        {
            texts[i] = new CachedText();
        }

        return texts;
    }

    private string? FormatMagnitude(StatusEffectDefinition def, in LocalStatusEffect effect)
    {
        if (def.Stun)
        {
            return GameText.Get("status_effects.magnitude.stun");
        }

        if (def.Magnitude > 0f && def.TickIntervalSeconds > 0f)
        {
            return _magnitude.Format(
                "status_effects.magnitude.damage",
                new TextArg("amount", effect.Magnitude, MathF.Round(effect.Magnitude) == effect.Magnitude ? 0 : 2),
                new TextArg("interval", FormatTime(_interval, def.TickIntervalSeconds)));
        }

        if (def.HealthRegenMultiplier != 1f)
        {
            return _magnitude.Format("status_effects.magnitude.health_regen", new TextArg("value", effect.Magnitude, 2));
        }

        if (def.StaminaRegenMultiplier != 1f)
        {
            return _magnitude.Format("status_effects.magnitude.stamina_regen", new TextArg("value", effect.Magnitude, 2));
        }

        return effect.Magnitude is > 0f and < 1f
            ? _magnitude.Format("status_effects.magnitude.speed", new TextArg("value", effect.Magnitude, 2))
            : null;
    }

    private int IndexOf(IReadOnlyList<LocalStatusEffect> effects, ushort effectId)
    {
        for (int i = 0; i < _count && i < effects.Count; i++)
        {
            if (effects[i].EffectId == effectId)
            {
                return i;
            }
        }

        return -1;
    }

    private void DrawTooltip(SpriteBatch spriteBatch, BitmapFont font, in LocalStatusEffect effect, StatusEffectDefinition def, int viewportWidth, bool showMinimap)
    {
        string name = _names.Text(def.Key, def.Key);
        string text = effect.IsTimed
            ? _tooltip.Format("status_effects.tooltip", new TextArg("name", name), new TextArg("time", FormatTime(_tooltipTime, effect.RemainingSeconds)))
            : _tooltip.Format("status_effects.tooltip_untimed", new TextArg("name", name));
        RectangleF icon = StatusEffectStripLayout.Icon(_hovered, viewportWidth, showMinimap);
        float width = font.MeasureString(text, TextLayoutOptions.Default, 1f).Width;
        float x = MathF.Max(8f, MathF.Min(icon.X, viewportWidth - width - 12f));
        spriteBatch.DrawOutlinedString(font, text, new Vector2(x, icon.Y + StatusEffectStripLayout.RowHeight + 2f), CarbonUiPalette.TextPrimary, CarbonUiPalette.TextOutline, 2, 1f);
    }

    private void DrawPanelText(SpriteBatch spriteBatch, BitmapFont font, StatusEffectDefinition def)
    {
        ColorRgba outline = CarbonUiPalette.TextOutline;
        float x = _panel.X + PanelPadding;
        float y = _panel.Y + PanelPadding;
        ColorRgba titleColor = def.Category == StatusCategory.Buff ? s_buffFrame : s_debuffFrame;
        spriteBatch.DrawOutlinedString(font, _names.Text(def.Key, def.Key), new Vector2(x, y), titleColor, outline, 1, 1.15f);
        y += 24f;
        float textWidth = _panel.Width - (PanelPadding * 2f);
        spriteBatch.DrawOutlinedString(font, _descriptions.Text(def.Key, def.Key), new Vector2(x, y), new TextLayoutOptions(textWidth, wordWrap: true), CarbonUiPalette.TextPrimary, outline, 1, 1f);
        y += _descriptionHeight + 8f;
        for (int i = 0; i < _panelLines.Count; i++)
        {
            spriteBatch.DrawOutlinedString(font, _panelLines[i], new Vector2(x, y), CarbonUiPalette.TextPrimary, outline, 1, 1f);
            y += LineHeight;
        }

        spriteBatch.DrawOutlinedString(font, GameText.Get("status_effects.close_hint"), new Vector2(x, y), s_dimText, outline, 1, 0.9f);
    }
}
