// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Read-only visual loadout passed to <see cref="VectorRigRenderer"/> to composite equipped armor pieces
/// and enhanced weapons directly onto an articulated character rig's skeleton bones.
/// </summary>
public readonly record struct VisualEquipmentLoadout(
    ushort HeadId = 0,
    ushort ChestId = 0,
    ushort LegsId = 0,
    ushort FeetId = 0,
    ushort WeaponId = 0,
    byte WeaponUpgradeLevel = 0);
