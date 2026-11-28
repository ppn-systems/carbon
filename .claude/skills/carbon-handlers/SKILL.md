---
name: carbon-handlers
description: Use when writing or debugging server-side packet handlers in Carbon — [PacketHandler], [PacketOpcode], IPacketContext<T>, World Tick queue integration, zero-allocation pooling, response replying, and spatial broadcast.
---

# Carbon Packet Handlers & Dispatching

Applies to packet handlers and message dispatchers in `server/`.

## Non-negotiables

- **`[PacketHandler("Domain.Name")]` on the handler class is required** for discovery and source-generated dispatch table registration.
- **`[PacketOpcode(OpCode)]` is mandatory on every handler method.**
- **Thread Safety & World Tick Integration**:
  - Network I/O callbacks run on background thread-pool threads.
  - **NEVER mutate world entity state directly from an asynchronous network I/O callback.**
  - The handler must enqueue the player command (or input struct) to the `WorldSimulation` command queue for sequential execution during the next World Tick.
- **Context Lifetime**:
  - `IPacketContext<T>` is pooled. Never capture `context` across `await` points or store it in static fields or closures. Copy necessary values (`context.Packet`, `context.Connection`) immediately.
- **Pooled Responses**:
  - Responses sent via `context.Sender.ReplyAsync(packet)` are leased from `PacketFactory<T>.Acquire()`. That allocates one packet per reply (ADR-043), which is accepted because replies are not per tick. Tick-driven server sends go through `WorldOutboundSink` and `PacketPool.Rent<T>()`.
  - Always wrap the lease in `try/catch` and dispose the lease on send completion:
    ```csharp
    PacketScope<MyReply> lease = PacketFactory<MyReply>.Acquire();
    try
    {
        MyReply reply = lease.Value;
        reply.Initialize(...);
        return context.Sender.ReplyAsync(reply).DisposeOnCompletionAsync(lease);
    }
    catch
    {
        lease.Dispose();
        throw;
    }
    ```

## Canonical Handler Blueprint

```csharp
using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Codec.Pooling;
using Carbon.Contracts.Packets;
using Carbon.Engine.Simulation;

namespace Carbon.Server.Handlers;

[PacketHandler("Carbon.Gameplay")]
public static partial class MovementHandlers
{
    // Injected world simulation instance
    public static WorldSimulation Simulation { get; set; } = null!;

    [PacketOpcode(CarbonOpCode.PlayerMoveRequest)]
    public static ValueTask HandleMoveAsync(IPacketContext<PlayerMoveRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PlayerMoveRequest request = context.Packet;
        IConnection connection = context.Connection;

        // Enqueue command for the authoritative tick loop
        Simulation.EnqueuePlayerInput(new PlayerInputCommand
        {
            SessionId = connection.Id,
            EntityId = request.EntityId,
            TargetPosition = new System.Numerics.Vector3(request.PositionX, request.PositionY, request.PositionZ),
            InputTick = request.ClientTick
        });

        return ValueTask.CompletedTask;
    }
}
```

## Spatial Broadcast & Area of Interest (AoI)

When the world simulation generates state updates:
- **Single Target (Unicast)**: Use `context.Sender.ReplyAsync(packet)` or `connection.SendAsync(packet)`.
- **Area Broadcast (Multicast)**: Query the spatial hash grid for players within the entity's viewing radius, serialize the snapshot packet once, and dispatch to all observer connections.
