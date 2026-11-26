// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Carbon.Architecture.Tests;

/// <summary>
/// Audit TS-4: Nalix pools <c>IPacketContext</c> and may recycle it while an async handler is suspended, so a handler
/// must copy what it needs (<c>Connection</c>, <c>Sender</c>, <c>Packet</c> fields, <c>CancellationToken</c>) into locals
/// before its first <c>await</c> and never touch <c>context</c> after it. The arguments of the first awaited call are
/// evaluated before it suspends, so <c>context</c> may appear there, but only when that statement is at the top level
/// of the method (inside a loop or block it may run again after a suspension).
/// </summary>
public sealed partial class HandlerContextTests
{
    [Fact]
    public void AsyncHandlers_ShouldNotUseThePacketContextAfterTheFirstAwait()
    {
        List<string> violations = [];
        foreach (string file in RepositoryLayout.TrackedFiles("server/Carbon.Handlers").Where(f => f.EndsWith(".cs", StringComparison.Ordinal)))
        {
            string source = StripComments(File.ReadAllText(Path.Combine(RepositoryLayout.Root, file)));
            foreach (Match handler in AsyncContextHandlerRegex().Matches(source))
            {
                string body = MethodBody(source, handler.Index + handler.Length);
                int checkFrom = FirstUnsafeIndex(body);
                if (checkFrom < 0)
                {
                    continue;
                }

                int bodyLine = LineOf(source, source.IndexOf(body, handler.Index, StringComparison.Ordinal));
                foreach (Match use in ContextUseRegex().Matches(body[checkFrom..]))
                {
                    int line = bodyLine + LineOf(body, checkFrom + use.Index) - 1;
                    violations.Add($"{file}:{line} {handler.Groups["name"].Value} uses 'context' after await");
                }
            }
        }

        Violations.ShouldBeEmpty(violations, "copy context members into locals before the first await (audit TS-4)");
    }

    [Theory]
    [InlineData("ulong id = R(context.Connection); await Submit(id, context.Packet.X).ConfigureAwait(false); await Reply(sender).ConfigureAwait(false);", 0)]
    [InlineData("await Something().ConfigureAwait(false); await context.Sender.ReplyAsync(x).ConfigureAwait(false);", 1)]
    [InlineData("for (int i = 0; i < 3; i++) { await context.Sender.ReplyAsync(x).ConfigureAwait(false); }", 1)]
    [InlineData("IPacketSender sender = context.Sender; Work(); return;", 0)]
    public void Guard_FlagsOnlyUsesThatCanRunAfterASuspension(string statements, int expected)
    {
        string sample = "public static async ValueTask HandleAsync(IPacketContext<Foo> context)\n{\n    " + statements + "\n}\n";

        Match handler = AsyncContextHandlerRegex().Match(sample);
        string body = MethodBody(sample, handler.Index + handler.Length);
        int checkFrom = FirstUnsafeIndex(body);

        handler.Success.Should().BeTrue();
        (checkFrom < 0 ? 0 : ContextUseRegex().Count(body[checkFrom..])).Should().Be(expected);
    }

    /// <summary>
    /// Index from which a use of <c>context</c> may run after a suspension: the end of the first awaited statement when
    /// it is at the method's top level, else the first <c>await</c> itself; -1 when the method never awaits.
    /// </summary>
    private static int FirstUnsafeIndex(string body)
    {
        Match firstAwait = AwaitRegex().Match(body);
        if (!firstAwait.Success)
        {
            return -1;
        }

        int depth = 0;
        for (int i = 0; i < firstAwait.Index; i++)
        {
            depth += body[i] == '{' ? 1 : body[i] == '}' ? -1 : 0;
        }

        if (depth != 1)
        {
            return firstAwait.Index;
        }

        int end = body.IndexOf(';', firstAwait.Index);
        return end < 0 ? firstAwait.Index : end + 1;
    }

    private static string MethodBody(string source, int afterSignature)
    {
        int open = source.IndexOf('{', afterSignature);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        return source[open..];
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    private static string StripComments(string source) => LineCommentRegex().Replace(source, string.Empty);

    [GeneratedRegex(@"\basync\s+(?:ValueTask|Task)\s+(?<name>\w+)\s*\(\s*IPacketContext<[^>]+>\s+context\s*\)")]
    private static partial Regex AsyncContextHandlerRegex();

    [GeneratedRegex(@"\bawait\b")]
    private static partial Regex AwaitRegex();

    [GeneratedRegex(@"\bcontext\b")]
    private static partial Regex ContextUseRegex();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineCommentRegex();
}
