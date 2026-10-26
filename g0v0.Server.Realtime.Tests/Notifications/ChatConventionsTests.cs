// Copyright (c) GooGuTeam. License under MIT License. See LICENSE in the project root for license information.

using g0v0.Server.Realtime.Objects.Chat;
using NUnit.Framework;

namespace g0v0.Server.Realtime.Tests.Notifications;

/// <summary>
/// Tests for the chat conventions shared by the endpoints and the notification server.
/// </summary>
[TestFixture]
public class ChatConventionsTests
{
    [TestCase("!mp", ExpectedResult = true)]
    [TestCase("!mp help", ExpectedResult = true)]
    [TestCase("!MP name Grand Finals", ExpectedResult = true)]
    [TestCase("!mp start 10", ExpectedResult = true)]
    [TestCase("!mpx", ExpectedResult = false)]
    [TestCase("!roll 10", ExpectedResult = false)]
    [TestCase("! help", ExpectedResult = false)]
    [TestCase("!mp not a command prefix", ExpectedResult = true)]
    [TestCase("hello", ExpectedResult = false)]
    public bool IsMultiplayerCommand_DetectsMpCommands(string content)
    {
        return ChatConventions.IsMultiplayerCommand(content);
    }

    [Test]
    public void IsMultiplayerCommand_RequiresUserCommandSyntax()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ChatConventions.IsMultiplayerCommand(string.Empty), Is.False);
            Assert.That(ChatConventions.IsMultiplayerCommand("!"), Is.False);
            Assert.That(ChatConventions.IsMultiplayerCommand("!!mp"), Is.False);
        });
    }
}