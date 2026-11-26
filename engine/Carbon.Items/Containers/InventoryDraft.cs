// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Items.Containers;

/// <summary>
/// Zero-allocation scratch copy of an <see cref="InventoryContainer"/> used to stage a multi-step mutation
/// (remove ingredients, add outputs, buy, sell) and commit it only when every step succeeded.
/// This is what makes crafting and vendor operations all-or-nothing.
/// </summary>
public ref struct InventoryDraft
{
    /// <summary>
    /// Largest capacity callers should place on the stack (<c>stackalloc</c>); larger containers should use a heap buffer.
    /// </summary>
    public const int StackBufferLimit = 128;

    private readonly Span<ItemSlot> _slots;

    private InventoryDraft(Span<ItemSlot> slots) => _slots = slots;

    /// <summary>
    /// Gets the staged slots.
    /// </summary>
    public readonly ReadOnlySpan<ItemSlot> Slots => _slots;

    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="buffer"/> and returns a draft over it.
    /// </summary>
    /// <param name="source">Container to stage changes for.</param>
    /// <param name="buffer">Scratch buffer with length of at least <see cref="InventoryContainer.Capacity"/>.</param>
    /// <returns>A draft mirroring the container's current slots.</returns>
    public static InventoryDraft Begin(InventoryContainer source, Span<ItemSlot> buffer)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (buffer.Length < source.Capacity)
        {
            throw new ArgumentException("Draft buffer is smaller than the container capacity.", nameof(buffer));
        }

        Span<ItemSlot> slots = buffer[..source.Capacity];
        source.Slots.CopyTo(slots);
        return new InventoryDraft(slots);
    }

    /// <summary>
    /// Counts the staged quantity of <paramref name="itemId"/>.
    /// </summary>
    /// <param name="itemId">Item definition identifier.</param>
    /// <returns>Total staged quantity.</returns>
    public readonly int Count(uint itemId)
    {
        if (itemId == 0)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].ItemId == itemId && _slots[i].Quantity > 0)
            {
                total += _slots[i].Quantity;
            }
        }

        return total;
    }

    /// <summary>
    /// Removes up to <paramref name="quantity"/> units of <paramref name="itemId"/> in slot order.
    /// </summary>
    /// <param name="itemId">Item definition identifier.</param>
    /// <param name="quantity">Quantity to remove.</param>
    /// <returns>The quantity actually removed.</returns>
    public readonly int Remove(uint itemId, int quantity)
    {
        if (itemId == 0 || quantity <= 0)
        {
            return 0;
        }

        int needed = quantity;
        for (int i = 0; i < _slots.Length && needed > 0; i++)
        {
            ItemSlot slot = _slots[i];
            if (slot.ItemId != itemId || slot.Quantity <= 0)
            {
                continue;
            }

            int take = Math.Min(needed, slot.Quantity);
            int left = slot.Quantity - take;
            _slots[i] = left > 0 ? (slot with { Quantity = left }) : ItemSlot.Empty;
            needed -= take;
        }

        return quantity - needed;
    }

    /// <summary>
    /// Adds <paramref name="incoming"/>, merging into matching stacks first and then filling empty slots.
    /// Items that cannot stack (unique instance, upgraded, or <paramref name="maxStack"/> of 1) take one slot per unit.
    /// </summary>
    /// <param name="incoming">Item entry to add.</param>
    /// <param name="maxStack">Maximum quantity per slot for this item.</param>
    /// <returns>The quantity that did not fit (<c>0</c> when everything fit).</returns>
    public readonly int Add(in ItemSlot incoming, int maxStack)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStack);
        if (incoming.IsEmpty)
        {
            return 0;
        }

        int remaining = incoming.Quantity;
        if (maxStack > 1 && incoming.IsStackable)
        {
            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                ItemSlot slot = _slots[i];
                if (slot.ItemId == incoming.ItemId && slot.IsStackable && slot.Quantity > 0 && slot.Quantity < maxStack)
                {
                    int add = Math.Min(remaining, maxStack - slot.Quantity);
                    _slots[i] = slot with { Quantity = slot.Quantity + add };
                    remaining -= add;
                }
            }

            for (int i = 0; i < _slots.Length && remaining > 0; i++)
            {
                if (_slots[i].IsEmpty)
                {
                    int add = Math.Min(remaining, maxStack);
                    _slots[i] = incoming with { Quantity = add };
                    remaining -= add;
                }
            }

            return remaining;
        }

        for (int i = 0; i < _slots.Length && remaining > 0; i++)
        {
            if (_slots[i].IsEmpty)
            {
                _slots[i] = incoming with { Quantity = 1 };
                remaining--;
            }
        }

        return remaining;
    }

    /// <summary>
    /// Writes every staged slot that differs back into <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The container this draft was started from.</param>
    public readonly void CommitTo(InventoryContainer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Capacity != _slots.Length)
        {
            throw new ArgumentException("Draft was not started from this container.", nameof(target));
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            if (target.GetSlot(i) != _slots[i])
            {
                target.SetSlot(i, _slots[i]);
            }
        }
    }
}
