// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Abstractions;

/// <summary>
/// Composite client world session combining authentication (<see cref="IClientAuthService"/>),
/// character roster management (<see cref="IClientCharacterService"/>), read-only rendering state (<see cref="IClientWorldReadModel"/>),
/// and gameplay command dispatching (<see cref="IClientGameplayController"/>). Kept free of OpenGL/window dependencies.
/// </summary>
public interface IClientWorldSession :
    IClientAuthService,
    IClientCharacterService,
    IClientWorldReadModel,
    IClientGameplayController,
    IAsyncDisposable;
