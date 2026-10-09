using StreamCommand.Services;
using Xunit;

namespace StreamCommand.Tests;

/// <summary>
/// The IRC parser decides whether chat ever reads as "connected": IsConnected waits for a
/// parsed 366/JOIN, so a parsing slip here leaves users on "Not connected" for good.
/// Lines are shaped like real Twitch traffic.
/// </summary>
public class IrcParserTests
{
    [Fact]
    public void Privmsg_WithTags_SplitsEveryPart()
    {
        var irc = IrcParser.ParseLine(
            "@badge-info=;color=#1E90FF;display-name=Viewer_One;id=abc-123;mod=0;subscriber=1 " +
            ":viewer_one!viewer_one@viewer_one.tmi.twitch.tv PRIVMSG #mychannel :hello there");

        Assert.Equal("PRIVMSG", irc.Command);
        Assert.Equal("#mychannel", irc.Parameters);
        Assert.Equal("hello there", irc.Trailing);
        Assert.Equal("viewer_one!viewer_one@viewer_one.tmi.twitch.tv", irc.Prefix);

        var msg = IrcParser.ParsePrivmsg(irc)!;
        Assert.Equal("Viewer_One", msg.Username);
        Assert.Equal("#1E90FF", msg.Color);
        Assert.True(msg.IsSub);
        Assert.False(msg.IsMod);
        Assert.False(msg.IsLocalEcho);
    }

    [Fact]
    public void Trailing_KeepsColonsAndSpacesVerbatim()
    {
        var irc = IrcParser.ParseLine(":u!u@u.tmi.twitch.tv PRIVMSG #c :link: https://x.y/z :) ok");
        Assert.Equal("link: https://x.y/z :) ok", irc.Trailing);
    }

    [Fact]
    public void Ping_WithoutPrefix_Parses()
    {
        var irc = IrcParser.ParseLine("PING :tmi.twitch.tv");
        Assert.Equal("PING", irc.Command);
        Assert.Equal("tmi.twitch.tv", irc.Trailing);
    }

    [Theory]
    [InlineData(":viewer.tmi.twitch.tv 366 viewer #mychannel :End of /NAMES list", "366")]
    [InlineData(":viewer!viewer@viewer.tmi.twitch.tv JOIN #mychannel", "JOIN")]
    [InlineData(":tmi.twitch.tv 001 viewer :Welcome, GLHF!", "001")]
    public void JoinConfirmationLines_YieldTheCommandTheServiceWaitsFor(string line, string command)
        => Assert.Equal(command, IrcParser.ParseLine(line).Command);

    [Fact]
    public void Privmsg_FallsBackToLoginName_WhenNoDisplayName()
    {
        var msg = IrcParser.ParsePrivmsg(IrcParser.ParseLine(":somebody!somebody@somebody.tmi.twitch.tv PRIVMSG #c :hi"))!;
        Assert.Equal("somebody", msg.Username);
        Assert.Equal("#C084FC", msg.Color);   // default when Twitch sends no color
    }

    [Fact]
    public void Privmsg_WithNoPrefix_IsRejected()
        => Assert.Null(IrcParser.ParsePrivmsg(IrcParser.ParseLine("PRIVMSG #c :orphan")));

    [Fact]
    public void LoginFailedNotice_ExposesMsgIdlessText()
    {
        // Twitch's auth failure has no msg-id; the service matches on this trailing text.
        var irc = IrcParser.ParseLine(":tmi.twitch.tv NOTICE * :Login authentication failed");
        Assert.Equal("NOTICE", irc.Command);
        Assert.Equal("Login authentication failed", irc.Trailing);
    }

    [Fact]
    public void Resub_MapsToTypedEvent_WithMonths()
    {
        var msg = IrcParser.ParseUserNotice("display-name=Fan;id=r1;msg-id=resub;msg-param-months=7")!;
        Assert.Equal(TwitchEventType.Resub, msg.EventType);
        Assert.Equal("7", msg.Months);
        Assert.True(msg.IsAlert);
    }

    [Fact]
    public void Raid_CarriesRaiderAndViewerCount()
    {
        var msg = IrcParser.ParseUserNotice("display-name=Raider;msg-id=raid;msg-param-viewerCount=42")!;
        Assert.Equal(TwitchEventType.Raid, msg.EventType);
        Assert.Equal("Raider", msg.RaiderName);
        Assert.Equal("42", msg.ViewerCount);
    }

    [Theory]
    [InlineData("subgift", "gifted a sub to Lucky")]
    [InlineData("submysterygift", "is gifting 5 subs")]
    public void GiftSubs_DistinguishSingleFromMystery(string msgId, string expected)
    {
        var msg = IrcParser.ParseUserNotice(
            $"display-name=Gifter;msg-id={msgId};msg-param-recipient-display-name=Lucky;msg-param-mass-gift-count=5")!;
        Assert.Equal(TwitchEventType.GiftSub, msg.EventType);
        Assert.Contains(expected, msg.Text);
    }

    [Fact]
    public void UnknownUserNotice_IsIgnored()
        => Assert.Null(IrcParser.ParseUserNotice("display-name=X;msg-id=announcement"));

    [Fact]
    public void Tags_EmptyColorKeepsDefault_AndMalformedPairsAreSkipped()
    {
        var t = IrcParser.ParseTags("color=;garbage;id=xyz;vip=1");
        Assert.Equal("#C084FC", t.Color);
        Assert.Equal("xyz", t.Id);
        Assert.True(t.IsVip);
    }
}
