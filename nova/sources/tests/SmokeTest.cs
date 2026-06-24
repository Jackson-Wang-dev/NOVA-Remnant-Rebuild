using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace Nova.Tests;

/// <summary>
/// Tier 0's bar for "test framework works at all" - see porting-guide.md. Runs inside the real game
/// process (NovaController._Ready's #if DEBUG branch), so GameState.Instance is already a live,
/// initialized singleton by the time this runs, not a mock.
/// </summary>
public class SmokeTest : TestClass
{
    public SmokeTest(Node testScene) : base(testScene) { }

    [Test]
    public void TrueIsTrue() => true.ShouldBeTrue();

    [Test]
    public void GameStateSingletonIsLive() => GameState.Instance.ShouldNotBeNull();
}
