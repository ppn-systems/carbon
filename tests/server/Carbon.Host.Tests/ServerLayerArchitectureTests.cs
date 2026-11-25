// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Reflection;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Handlers.Identity;
using Carbon.Infrastructure.Security;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class ServerLayerArchitectureTests
{
    [Fact]
    public void ServerAssemblies_MustRespectLayerBoundariesAndNeverReferenceEfOrGraphics()
    {
        Assembly[] serverAssemblies =
        [
            typeof(IAuthoritativeWorld).Assembly,
            typeof(AuthoritativeWorld).Assembly,
            typeof(Pbkdf2PasswordHasher).Assembly,
            typeof(IdentityHandlers).Assembly,
            typeof(Program).Assembly,
        ];

        foreach (Assembly asm in serverAssemblies)
        {
            string? asmName = asm.GetName().Name;
            asmName.Should().NotBe("Carbon.Server", "server assemblies follow Clean Architecture layer names");

            AssemblyName[] refs = asm.GetReferencedAssemblies();
            foreach (AssemblyName reference in refs)
            {
                string refName = reference.Name ?? string.Empty;
                refName.Should().NotContain("EntityFramework", $"{asmName} must remain Native AOT pure without EF Core");
                refName.Should().NotContain("Carbon.Graphics", $"{asmName} must never reference client graphics");
                refName.Should().NotContain("Carbon.Desktop", $"{asmName} must never reference desktop runner");
                refName.Should().NotContain("Silk.NET", $"{asmName} must never pull windowing/OpenGL dependencies");
            }
        }
    }

    [Fact]
    public void HandlersAssembly_MustNotDependOnInfrastructure()
    {
        Assembly handlersAssembly = typeof(IdentityHandlers).Assembly;
        AssemblyName[] refs = handlersAssembly.GetReferencedAssemblies();

        foreach (AssemblyName reference in refs)
        {
            string refName = reference.Name ?? string.Empty;
            refName.Should().NotBe("Carbon.Infrastructure", "Carbon.Handlers dispatches through Carbon.Abstractions only");
        }
    }
}
