// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Abstractions.Identity;
using Carbon.Abstractions.World;
using Carbon.Handlers.Entities;
using Carbon.Handlers.Identity;
using Carbon.Handlers.Items;
using Carbon.Handlers.Maps;
using Carbon.Handlers.Session;
using Carbon.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Nalix.Abstractions.Networking;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.Hosting.Protocols;
using Nalix.Network.Options;

namespace Carbon.Host;

/// <summary>
/// Configures dependency registrations, packet handler bindings, security, and network listeners for <c>Carbon.Host</c>.
/// </summary>
internal static class Startup
{
    /// <summary>Builds the configured <see cref="NetworkApplication"/> without starting listeners.</summary>
    internal static NetworkApplication Build(
        HostRuntimeOptions options,
        IAuthService authService,
        IAuthoritativeWorld world,
        NalixWorldOutboundSink outboundSink,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authService);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(outboundSink);
        ArgumentNullException.ThrowIfNull(logger);

        InstanceManager.Instance.Register<IAuthService>(authService);
        InstanceManager.Instance.Register<IAuthoritativeWorld>(world);

        NetworkApplication app = NetworkApplication.CreateBuilder()
            .UseLogger(logger)
            .UseSessions()
            .UseTimeSync()
            .UseSystemControl()
            .UseSecureConnections()
            .MapHandlers(typeof(IdentityHandlers))
            .MapHandlers(typeof(MapHandlers))
            .MapHandlers(typeof(EntityHandlers))
            .MapHandlers(typeof(ItemHandlers))
            .Configure<ConnectionHubOptions>(o => o.IsEnableLatency = false)
            .Configure<NetworkSocketOptions>(o =>
            {
                o.NoDelay = true;
                o.KeepAlive = true;
                o.MaxParallel = 4;
            })
            .Configure<NetworkWebSocketOptions>(o =>
            {
                o.Host = "+";
                o.Path = options.WebSocketPath;
                o.Port = options.WebSocketPort;
                o.MaxMessageSize = 128 * 1024;
            })
            .Configure<ConnectionGuardOptions>(o =>
            {
                o.MaxConnections = 2000;
                o.MaxPacketPerSecond = 240;
                o.MaxErrorThreshold = 50;
                o.EnableProgressiveBanning = true;
            })
            .ConfigureDispatchOptions(o =>
            {
                _ = o.WithErrorHandling((ex, opcode) =>
                    logger.LogError(ex, "Dispatch error on opcode 0x{Opcode:X4}", opcode));
            })
            .MapWebSocket<DefaultProtocol>()
                .OnPort(options.WebSocketPort)
                .WithPath(options.WebSocketPath)
                .Bind()
            .MapTcp<DefaultProtocol>()
                .OnPort(options.TcpPort)
                .Bind()
            .Build();

        IConnectionHub? hub = InstanceManager.Instance.GetExistingInstance<IConnectionHub>();
        if (hub is not null)
        {
            outboundSink.AttachHub(hub);
            hub.ConnectionUnregistered += conn =>
            {
                ulong id = conn.ConnectionId;
                _ = world.LeaveWorld(id);
                _ = ConnectionAuthRegistry.Clear(id);
            };
        }

        return app;
    }
}
