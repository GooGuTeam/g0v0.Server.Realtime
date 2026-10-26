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

    [Test]
    public void SplitMessage_KeepsShortContentInOnePart()
    {
        Assert.That(string.Join('|', ChatConventions.SplitMessage("!mp help", 450)), Is.EqualTo("!mp help"));
    }

    [Test]
    public void SplitMessage_BreaksOnLineBoundaries()
    {
        string content = "Available !mp commands:\nRoom: !mp make <name>\nMatch: !mp start";

        IReadOnlyList<string> parts = ChatConventions.SplitMessage(content, 30);

        Assert.Multiple(() =>
        {
            Assert.That(parts, Has.Count.EqualTo(3));
            Assert.That(parts[0], Is.EqualTo("Available !mp commands:"));
            Assert.That(parts[1], Is.EqualTo("Room: !mp make <name>"));
            Assert.That(parts[2], Is.EqualTo("Match: !mp start"));
        });
    }

    [Test]
    public void SplitMessage_PacksShortLinesIntoFewerParts()
    {
        IReadOnlyList<string> parts = ChatConventions.SplitMessage("one\ntwo\nthree\nfour", 10);

        Assert.That(string.Join('|', parts), Is.EqualTo("one two|three four"));
    }

    [Test]
    public void SplitMessage_CutsOverlongLinesAtSpaces()
    {
        string content = string.Join(' ', Enumerable.Repeat("word", 100));

        IReadOnlyList<string> parts = ChatConventions.SplitMessage(content, 40);

        Assert.Multiple(() =>
        {
            Assert.That(parts, Has.Count.GreaterThan(1));
            Assert.That(parts.Select(part => part.Length), Is.All.LessThanOrEqualTo(40));
            Assert.That(string.Join(' ', parts), Is.EqualTo(content));
        });
    }

    [Test]
    public void SplitMessage_HardCutsContentWithoutSpaces()
    {
        string content = new('a', 100);

        IReadOnlyList<string> parts = ChatConventions.SplitMessage(content, 40);

        Assert.Multiple(() =>
        {
            Assert.That(parts.Select(part => part.Length), Is.All.LessThanOrEqualTo(40));
            Assert.That(string.Concat(parts), Is.EqualTo(content));
        });
    }

    [Test]
    public void SplitMessage_DoesNotSeparateSurrogatePairs()
    {
        string content = new string('a', 39) + "😀" + new string('b', 10);

        IReadOnlyList<string> parts = ChatConventions.SplitMessage(content, 40);

        Assert.Multiple(() =>
        {
            Assert.That(parts.Select(part => part.Length), Is.All.LessThanOrEqualTo(40));
            Assert.That(string.Concat(parts), Is.EqualTo(content));
        });
    }
}