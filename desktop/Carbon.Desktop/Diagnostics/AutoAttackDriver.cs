// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Desktop.Abstractions.World;

namespace Carbon.Desktop.Diagnostics;

/// <summary>
/// Capture-tool driver (<c>CARBON_AUTO_ATTACK=seconds</c>): holds hotbar slot 1 and swings the primary action due east
/// every <c>seconds</c> without moving, so frame stats and screenshots are taken with attack clips and swing trails on
/// screen in a repeatable frame (<c>docs/art/combat-animation.md</c>). It sends the same input a player does; the server
/// stays authoritative over every swing.
/// </summary>
internal sealed class AutoAttackDriver
{
    private float _untilNext;
    private bool _slotSelected;

    /// <summary>Advances the driver and swings when the interval has elapsed.</summary>
    /// <param name="dt">Frame seconds.</param>
    /// <param name="intervalSeconds">Seconds between swings.</param>
    /// <param name="world">World read model.</param>
    /// <param name="controller">Gameplay controller that sends the swing.</param>
    public void Update(float dt, float intervalSeconds, IWorldReadModel world, IGameplayController controller)
    {
        if (!world.IsReady)
        {
            return;
        }

        if (!_slotSelected)
        {
            controller.SelectHotbarSlot(0);
            _slotSelected = true;
        }

        _untilNext -= dt;
        if (_untilNext > 0f)
        {
            return;
        }

        _untilNext = intervalSeconds;
        _ = controller.TriggerPrimaryAction(world.LocalPlayerInterpolatedPosition + new Vector2(24f, 0f), out _);
    }
}
