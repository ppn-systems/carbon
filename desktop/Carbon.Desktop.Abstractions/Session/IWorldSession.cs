// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Abstractions.Identity;
using Carbon.Desktop.Abstractions.World;

namespace Carbon.Desktop.Abstractions.Session;

/// <summary>
/// Composite world session combining authentication (<see cref="IAuthenticationGateway"/>),
/// character roster management (<see cref="ICharacterRosterService"/>), read-only rendering state (<see cref="IWorldReadModel"/>),
/// and gameplay command dispatching (<see cref="IGameplayController"/>). Kept free of OpenGL/window dependencies.
/// </summary>
public interface IWorldSession :
    IAuthenticationGateway,
    ICharacterRosterService,
    IWorldReadModel,
    IGameplayController,
    IAsyncDisposable;
