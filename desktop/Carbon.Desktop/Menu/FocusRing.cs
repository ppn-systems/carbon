// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Carbon.Desktop.Menu;

/// <summary>Tab / Shift+Tab focus cycling over a fixed order.</summary>
internal static class FocusRing
{
    public static T Next<T>(IReadOnlyList<T> order, T current, bool reverse)
        where T : struct, Enum
    {
        int index = -1;
        for (int i = 0; i < order.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(order[i], current))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return order[0];
        }

        return order[(index + (reverse ? order.Count - 1 : 1)) % order.Count];
    }
}
