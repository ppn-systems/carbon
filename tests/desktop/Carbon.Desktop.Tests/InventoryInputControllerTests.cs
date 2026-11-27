// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Gameplay.Input;
using Carbon.Desktop.Input;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Tests.Fakes;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Behaviour of the inventory tab's mouse/keyboard handling (ADR-020, extracted in #109): which session commands a
/// gesture sends, and which gestures send nothing. Driven frame by frame through a recording session and a
/// screen-free HUD whose dialog is the real <see cref="InventoryDialogState"/>.
/// </summary>
public sealed class InventoryInputControllerTests : IAsyncDisposable
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int BackpackSlot = PlayerInventoryLayout.BackpackStart + 3;
    private const int OtherBackpackSlot = PlayerInventoryLayout.BackpackStart + 7;

    private readonly RecordingWorldSession _session = new();
    private readonly FakeInventoryHud _hud = new();
    private readonly InventoryInputController _controller;
    private readonly uint _commonStackable;
    private readonly uint _rareStackable;

    public InventoryInputControllerTests()
    {
        _controller = new InventoryInputController(_session);
        _controller.AttachHud(_hud);
        ItemCatalogEntry[] all = _session.Items.Rules.ItemCatalog.All.ToArray();
        _commonStackable = all.First(static i => i.Rarity == ItemRarity.Common && i.MaxStack > 1).Id;
        _rareStackable = all.First(static i => i.Rarity >= ItemRarity.Rare && i.MaxStack > 1).Id;
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();

    // ---- drag and drop between slots ----

    [Fact]
    public void HandleTabInput_HoldDragReleasedOnAnotherSlot_SendsExactlyOneMove()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);

        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        _controller.CarriedSlot.Should().Be(BackpackSlot, "pressing on a filled slot picks it up");
        this.Hold(FakeInventoryHud.SlotAt(OtherBackpackSlot));
        this.Release(FakeInventoryHud.SlotAt(OtherBackpackSlot));
        this.Idle(FakeInventoryHud.SlotAt(OtherBackpackSlot));

        _session.Commands.Should().Equal($"Move({BackpackSlot},{OtherBackpackSlot})");
        _controller.CarriedSlot.Should().Be(-1);
        _hud.CarriedInventorySlot.Should().Be(-1, "the HUD highlight is cleared with the drag");
    }

    [Fact]
    public void HandleTabInput_ClickPickUpThenClickTarget_MovesOnSecondClick()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);

        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.SlotAt(BackpackSlot));
        _session.Commands.Should().BeEmpty("releasing on the source slot switches to click-carry instead of moving");
        _controller.CarriedSlot.Should().Be(BackpackSlot);

        this.Press(FakeInventoryHud.SlotAt(OtherBackpackSlot));

        _session.Commands.Should().Equal($"Move({BackpackSlot},{OtherBackpackSlot})");
        _controller.CarriedSlot.Should().Be(-1);
    }

    [Fact]
    public void HandleTabInput_CarriedItemClickedBackOntoItsOwnSlot_PutsItDownWithoutCommand()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.SlotAt(BackpackSlot));

        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));

        _session.Commands.Should().BeEmpty();
        _controller.CarriedSlot.Should().Be(-1);
    }

    [Fact]
    public void HandleTabInput_CarriedItemClickedOnEmptyModalArea_CancelsWithoutCommand()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.SlotAt(BackpackSlot));

        this.Press(FakeInventoryHud.EmptyModalArea);

        _session.Commands.Should().BeEmpty("clicking inside the window but off any slot cancels the carry");
        _controller.CarriedSlot.Should().Be(-1);
    }

    [Fact]
    public void HandleTabInput_HoldDragReleasedOffSlotBeyondDeadZone_CancelsWithoutCommand()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);

        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.EmptyModalArea);

        _session.Commands.Should().BeEmpty();
        _controller.CarriedSlot.Should().Be(-1, "a real drag that ends off every slot is a cancel, not a click-carry");
    }

    [Fact]
    public void HandleTabInput_PressOnEmptySlot_PicksNothingUp()
    {
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Press(FakeInventoryHud.SlotAt(OtherBackpackSlot));

        _session.Commands.Should().BeEmpty("an empty slot cannot be carried, so the second click has nothing to place");
        _controller.CarriedSlot.Should().Be(-1);
    }

    [Fact]
    public void HandleTabInput_MoveRejectedBySession_StillClearsCarry()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);
        _session.CommandResult = false;
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.SlotAt(BackpackSlot));

        this.Press(FakeInventoryHud.SlotAt(OtherBackpackSlot));

        _session.Commands.Should().Equal($"Move({BackpackSlot},{OtherBackpackSlot})");
        _controller.CarriedSlot.Should().Be(-1, "a rejected move must not leave the cursor stuck holding the item");
    }

    // ---- quick move ----

    [Fact]
    public void HandleTabInput_ShiftClickOnSlot_QuickMovesWithoutPickingUp()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);

        this.Press(FakeInventoryHud.SlotAt(BackpackSlot), shift: true);

        _session.Commands.Should().Equal($"QuickMove({BackpackSlot})");
        _controller.CarriedSlot.Should().Be(-1);
    }

    [Fact]
    public void HandleTabInput_RightClickOnSlot_QuickMoves()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);

        this.Frame(FakeInventoryHud.SlotAt(BackpackSlot), rightPressed: true);

        _session.Commands.Should().Equal($"QuickMove({BackpackSlot})");
    }

    // ---- closing / sorting ----

    [Fact]
    public void HandleTabInput_ClickOutsideModalWithNothingCarried_ClosesWindowWithoutCommand()
    {
        this.Press(FakeInventoryHud.OutsideModal);

        _hud.ShowPlayerModal.Should().BeFalse();
        _session.Commands.Should().BeEmpty();
    }

    [Fact]
    public void HandleTabInput_SortButtonAccepted_SortsAndShowsToast()
    {
        this.Press(FakeInventoryHud.SortButton);

        _session.Commands.Should().Equal("Sort()");
        _hud.Notifications.Should().Equal(GameText.Get("inventory.sorted"));
    }

    [Fact]
    public void HandleTabInput_SortButtonRejected_ShowsNoToast()
    {
        _session.CommandResult = false;

        this.Press(FakeInventoryHud.SortButton);

        _session.Commands.Should().Equal("Sort()");
        _hud.Notifications.Should().BeEmpty("a sort the session refused did not happen");
    }

    // ---- drop (outside the window) ----

    [Fact]
    public void HandleTabInput_CommonStackDraggedOutsideModal_DropsWholeStackImmediately()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);

        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.OutsideModal);

        _session.Commands.Should().ContainSingle().Which.Should().Be($"Drop({BackpackSlot},-1)", "-1 means the whole stack");
        _hud.InventoryDialog.IsOpen.Should().BeFalse();
        _controller.CarriedSlot.Should().Be(-1);
    }

    [Fact]
    public void HandleTabInput_RareItemDraggedOutsideModal_AsksBeforeDropping()
    {
        _session[BackpackSlot] = new ItemSlot(_rareStackable, 3);

        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.OutsideModal);

        _session.Commands.Should().BeEmpty("rare items need a confirmation before they hit the ground");
        _hud.InventoryDialog.Kind.Should().Be(InventoryDialogKind.ConfirmDrop);
        _hud.InventoryDialog.SlotIndex.Should().Be(BackpackSlot);

        this.Press(FakeInventoryHud.DialogButtonAt(InventoryDialogButton.Confirm));

        _session.Commands.Should().Equal($"Drop({BackpackSlot},-1)");
        _hud.InventoryDialog.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void HandleTabInput_RareDropConfirmationCancelled_DropsNothing()
    {
        _session[BackpackSlot] = new ItemSlot(_rareStackable, 3);
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.OutsideModal);

        this.Press(FakeInventoryHud.DialogButtonAt(InventoryDialogButton.Cancel));

        _session.Commands.Should().BeEmpty();
        _hud.InventoryDialog.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void HandleTabInput_RightClickOutsideWhileCarryingStack_DropsOneAndKeepsCarrying()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.SlotAt(BackpackSlot));

        this.Frame(FakeInventoryHud.OutsideModal, rightPressed: true);

        _session.Commands.Should().Equal($"Drop({BackpackSlot},1)");
        _controller.CarriedSlot.Should().Be(BackpackSlot, "the rest of the stack stays on the cursor");
    }

    [Fact]
    public void HandleTabInput_RightClickOutsideWhileCarryingLastItem_DropsItAndClearsCarry()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 1);
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.SlotAt(BackpackSlot));

        this.Frame(FakeInventoryHud.OutsideModal, rightPressed: true);

        _session.Commands.Should().Equal($"Drop({BackpackSlot},1)");
        _controller.CarriedSlot.Should().Be(-1);
    }

    [Fact]
    public void HandleTabInput_RightClickOutsideWhileCarryingRareItem_AsksToDropOne()
    {
        _session[BackpackSlot] = new ItemSlot(_rareStackable, 3);
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.SlotAt(BackpackSlot));

        this.Frame(FakeInventoryHud.OutsideModal, rightPressed: true);

        _session.Commands.Should().BeEmpty();
        _hud.InventoryDialog.Kind.Should().Be(InventoryDialogKind.ConfirmDrop);
        _hud.InventoryDialog.Quantity.Should().Be(1);
        _controller.CarriedSlot.Should().Be(-1, "the cursor lets go while the dialog asks");
    }

    [Fact]
    public void RequestDrop_EquippedSlot_AlwaysAsksFirst()
    {
        _session[PlayerInventoryLayout.HeadSlot] = new ItemSlot(_commonStackable, 1);

        bool asked = _controller.RequestDrop(PlayerInventoryLayout.HeadSlot, -1);

        asked.Should().BeTrue();
        _session.Commands.Should().BeEmpty();
        _hud.InventoryDialog.Kind.Should().Be(InventoryDialogKind.ConfirmDrop);
    }

    [Fact]
    public void RequestDrop_EnhancedCommonItem_AsksFirst()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 1, InstanceId: 77, UpgradeLevel: 2);

        _controller.RequestDrop(BackpackSlot, -1).Should().BeTrue();
        _session.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(PlayerInventoryLayout.TotalCapacity)]
    public void RequestDrop_SlotOutOfRange_SendsNothing(int slot)
    {
        _controller.RequestDrop(slot, 1).Should().BeFalse();

        _session.Commands.Should().BeEmpty();
        _hud.InventoryDialog.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void RequestDrop_NoHudAttached_SendsNothing()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);
        _controller.AttachHud(null);

        _controller.RequestDrop(BackpackSlot, 1).Should().BeFalse();

        _session.Commands.Should().BeEmpty();
    }

    // ---- split ----

    [Fact]
    public void HandleTabInput_CtrlClickStackThenAdjustAndConfirm_SplitsChosenQuantity()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 20);

        this.Press(FakeInventoryHud.SlotAt(BackpackSlot), ctrl: true);
        _hud.InventoryDialog.Kind.Should().Be(InventoryDialogKind.Split);
        _hud.InventoryDialog.Quantity.Should().Be(10, "the split dialog starts at half the stack");
        _controller.CarriedSlot.Should().Be(-1, "ctrl-click opens the dialog instead of picking up");

        this.Press(FakeInventoryHud.DialogButtonAt(InventoryDialogButton.Plus));
        this.Press(FakeInventoryHud.DialogButtonAt(InventoryDialogButton.Minus), shift: true);
        this.Press(FakeInventoryHud.DialogButtonAt(InventoryDialogButton.Confirm));

        _session.Commands.Should().ContainSingle().Which.Should().Be($"Split({BackpackSlot},1)", "10 + 1 - 10 = 1");
        _hud.InventoryDialog.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void HandleTabInput_SplitConfirmedWithEnterKey_SplitsWithoutClicking()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 8);
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot), ctrl: true);

        this.Frame(FakeInventoryHud.EmptyModalArea, confirm: true);

        _session.Commands.Should().Equal($"Split({BackpackSlot},4)");
    }

    [Fact]
    public void HandleTabInput_CtrlClickSingleItem_ExplainsInsteadOfOpeningSplit()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 1);

        this.Press(FakeInventoryHud.SlotAt(BackpackSlot), ctrl: true);

        _hud.InventoryDialog.IsOpen.Should().BeFalse();
        _hud.Notifications.Should().Equal(GameText.Get("inventory.split.needs_stack"));
        _session.Commands.Should().BeEmpty();
    }

    [Fact]
    public void HandleTabInput_ClickOutsideOpenDialog_ClosesItWithoutCommand()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 8);
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot), ctrl: true);

        this.Press(FakeInventoryHud.EmptyModalArea);

        _hud.InventoryDialog.IsOpen.Should().BeFalse();
        _session.Commands.Should().BeEmpty();
    }

    [Fact]
    public void HandleTabInput_DialogOpen_SwallowsSlotClicksAndShortcuts()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 8);
        _session[OtherBackpackSlot] = new ItemSlot(_commonStackable, 3);
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot), ctrl: true);

        // A frame with no click: the dialog owns it, so shift/right-click/delete on a slot do nothing.
        this.Frame(FakeInventoryHud.SlotAt(OtherBackpackSlot), rightPressed: true);
        this.Frame(FakeInventoryHud.SlotAt(OtherBackpackSlot), delete: true);

        _session.Commands.Should().BeEmpty();
        _hud.InventoryDialog.Kind.Should().Be(InventoryDialogKind.Split);
        _hud.InventoryDialog.SlotIndex.Should().Be(BackpackSlot);
        _controller.TryBindHoveredSlotToHotbar(0, FakeInventoryHud.SlotAt(OtherBackpackSlot), Width, Height).Should().BeFalse();
        _session.Commands.Should().BeEmpty("hotbar binding is disabled while a dialog is open");
    }

    [Fact]
    public void HandleTabInput_ItemChangedUnderOpenDialog_ClosesDialogAndSendsNothing()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 8);
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot), ctrl: true);

        _session[BackpackSlot] = new ItemSlot(_rareStackable, 8); // a server update replaced the stack
        this.Frame(FakeInventoryHud.EmptyModalArea, confirm: true);

        _session.Commands.Should().BeEmpty("confirming a stale dialog would split an item the player never chose");
        _hud.InventoryDialog.IsOpen.Should().BeFalse();
    }

    // ---- destroy ----

    [Fact]
    public void HandleTabInput_DeleteOnHoveredSlotThenConfirm_DestroysThatSlot()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);

        this.Frame(FakeInventoryHud.SlotAt(BackpackSlot), delete: true);
        _session.Commands.Should().BeEmpty("destroy always asks first");
        _hud.InventoryDialog.Kind.Should().Be(InventoryDialogKind.ConfirmDestroy);

        this.Press(FakeInventoryHud.DialogButtonAt(InventoryDialogButton.Confirm));

        _session.Commands.Should().ContainSingle().Which.Should().Be($"Destroy({BackpackSlot},0)", "0 means the whole stack");
    }

    [Fact]
    public void HandleTabInput_DeleteWhileCarrying_TargetsCarriedSlotNotHoveredOne()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);
        _session[OtherBackpackSlot] = new ItemSlot(_commonStackable, 2);
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.SlotAt(BackpackSlot));

        this.Frame(FakeInventoryHud.SlotAt(OtherBackpackSlot), delete: true);

        _hud.InventoryDialog.Kind.Should().Be(InventoryDialogKind.ConfirmDestroy);
        _hud.InventoryDialog.SlotIndex.Should().Be(BackpackSlot);
        _controller.CarriedSlot.Should().Be(-1);
    }

    [Fact]
    public void HandleTabInput_DeleteOnEmptySlot_OpensNothing()
    {
        this.Frame(FakeInventoryHud.SlotAt(BackpackSlot), delete: true);

        _hud.InventoryDialog.IsOpen.Should().BeFalse();
        _session.Commands.Should().BeEmpty();
    }

    // ---- hotbar binding ----

    [Fact]
    public void TryBindHoveredSlotToHotbar_HoveringFilledBackpackSlot_MovesItToThatHotbarKey()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);

        bool consumed = _controller.TryBindHoveredSlotToHotbar(2, FakeInventoryHud.SlotAt(BackpackSlot), Width, Height);

        consumed.Should().BeTrue();
        _session.Commands.Should().Equal($"Move({BackpackSlot},2)");
    }

    [Fact]
    public void TryBindHoveredSlotToHotbar_WhileCarrying_ClearsCarry()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);
        this.Press(FakeInventoryHud.SlotAt(BackpackSlot));
        this.Release(FakeInventoryHud.SlotAt(BackpackSlot));

        _controller.TryBindHoveredSlotToHotbar(0, FakeInventoryHud.SlotAt(BackpackSlot), Width, Height).Should().BeTrue();

        _controller.CarriedSlot.Should().Be(-1);
        _hud.CarriedInventorySlot.Should().Be(-1);
    }

    [Fact]
    public void TryBindHoveredSlotToHotbar_HoveringTheSameHotbarSlot_ConsumesKeyWithoutMove()
    {
        _session[2] = new ItemSlot(_commonStackable, 5);

        bool consumed = _controller.TryBindHoveredSlotToHotbar(2, FakeInventoryHud.SlotAt(2), Width, Height);

        consumed.Should().BeTrue("the key must not fall through to hotbar selection while hovering a filled slot");
        _session.Commands.Should().BeEmpty();
    }

    [Fact]
    public void TryBindHoveredSlotToHotbar_HoveringEmptySlot_LetsKeyFallThrough()
    {
        _controller.TryBindHoveredSlotToHotbar(2, FakeInventoryHud.SlotAt(BackpackSlot), Width, Height).Should().BeFalse();

        _session.Commands.Should().BeEmpty();
    }

    [Fact]
    public void TryBindHoveredSlotToHotbar_NotHoveringAnySlot_LetsKeyFallThrough()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);

        _controller.TryBindHoveredSlotToHotbar(2, FakeInventoryHud.EmptyModalArea, Width, Height).Should().BeFalse();

        _session.Commands.Should().BeEmpty();
    }

    [Fact]
    public void TryBindHoveredSlotToHotbar_HotbarIndexOutOfRange_SendsNothing()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);

        _ = _controller.TryBindHoveredSlotToHotbar(PlayerInventoryLayout.HotbarCount, FakeInventoryHud.SlotAt(BackpackSlot), Width, Height);

        _session.Commands.Should().BeEmpty();
    }

    // ---- unattached ----

    [Fact]
    public void HandleTabInput_NoHudAttached_IgnoresInputWithoutTouchingSession()
    {
        _session[BackpackSlot] = new ItemSlot(_commonStackable, 5);
        _controller.AttachHud(null);

        this.Press(FakeInventoryHud.SlotAt(BackpackSlot), shift: true);
        this.Frame(FakeInventoryHud.SlotAt(BackpackSlot), delete: true);

        _session.Commands.Should().BeEmpty();
        _controller.CarriedSlot.Should().Be(-1);
    }

    // ---- frame helpers ----

    private void Press(Vector2 mouse, bool shift = false, bool ctrl = false) =>
        this.Frame(mouse, leftPressed: true, leftDown: true, shift: shift, ctrl: ctrl);

    private void Hold(Vector2 mouse) => this.Frame(mouse, leftDown: true);

    private void Release(Vector2 mouse) => this.Frame(mouse);

    private void Idle(Vector2 mouse) => this.Frame(mouse);

    private void Frame(
        Vector2 mouse,
        bool leftPressed = false,
        bool leftDown = false,
        bool rightPressed = false,
        bool shift = false,
        bool ctrl = false,
        bool delete = false,
        bool confirm = false)
    {
        DesktopInputFrame frame = new(
            MoveDirection: Vector2.Zero,
            Sprint: shift,
            ZoomMultiplier: null,
            SpaceActionPressed: false,
            LeftClickPressed: leftPressed,
            LeftMouseDown: leftDown,
            RightClickPressed: rightPressed,
            RightMouseDown: rightPressed,
            ShiftHeld: shift,
            ScrollSlotDelta: 0,
            MouseScreenPosition: mouse,
            NumberSlotPressed: null,
            SkillPressed: null,
            ToggleInventoryPressed: false,
            ToggleCharacterPressed: false,
            CycleTargetPressed: false,
            InteractOrPickupPressed: false,
            DropItemPressed: false,
            ToggleCraftPressed: false,
            ToggleForgePressed: false,
            ToggleSkillsPressed: false,
            ToggleMinimapPressed: false,
            ToggleRestPressed: false,
            ToggleHudPressed: false,
            HarvestPressed: false,
            ToggleDoorPressed: false,
            PlaceStructurePressed: false,
            DemolishStructurePressed: false,
            ToggleDebugPressed: false,
            ScreenshotPressed: false,
            EscapePressed: false,
            CtrlHeld: ctrl,
            DeletePressed: delete,
            ConfirmPressed: confirm);
        _controller.HandleTabInput(in frame, Width, Height);
    }
}
