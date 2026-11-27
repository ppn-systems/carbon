// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Items.Catalog;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>Localized HUD warnings for tool misuse on harvest strikes (ADR-023; replaces engine-side English prose).</summary>
public static class ToolWarningText
{
    /// <summary>Returns the localized warning for <paramref name="outcome"/>, or an empty string for an optimal strike.</summary>
    /// <param name="outcome">The strike evaluation outcome.</param>
    /// <param name="requiredTier">Harvest tier the target needs.</param>
    /// <returns>The warning text.</returns>
    public static string Describe(ToolStrikeOutcome outcome, byte requiredTier) => outcome switch
    {
        ToolStrikeOutcome.SwordMisuseOnWood => GameText.Get("tool.sword_on_wood"),
        ToolStrikeOutcome.BladeBluntOnStone => GameText.Get("tool.blade_on_stone"),
        ToolStrikeOutcome.WornOutTool => GameText.Get("tool.worn_out"),
        ToolStrikeOutcome.InsufficientTier => GameText.Format("tool.need_tier", new TextArg("tier", requiredTier)),
        ToolStrikeOutcome.Optimal or _ => string.Empty,
    };
}
