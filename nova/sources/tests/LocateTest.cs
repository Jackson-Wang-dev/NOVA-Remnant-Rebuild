using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace Nova.Tests;

public class LocateTest : TestClass
{
    public LocateTest(Node testScene) : base(testScene) { }

    [Test]
    public void Locate_ReturnsReachedRecordForCurrentPath()
    {
        var gameState = GameState.Instance;
        gameState.StartGame("test_animation");
        gameState.Step();

        var result = gameState.Locate("test_animation.txt", 15);

        result.Ok.ShouldBeTrue();
        result.NodeName.ShouldBe("test_animation");
        result.DialogueIndex.ShouldBe(1);
        result.NodeRecordId.ShouldBe(gameState.CurrentNodeRecordId);
        result.Reached.ShouldBeTrue();
    }

    [Test]
    public void Locate_ReturnsUnreachedForFutureDialogueInCurrentNode()
    {
        var gameState = GameState.Instance;
        gameState.StartGame("test_animation");

        var result = gameState.Locate("test_animation.txt", 38);

        result.Ok.ShouldBeTrue();
        result.NodeName.ShouldBe("test_animation");
        result.DialogueIndex.ShouldBe(4);
        result.NodeRecordId.ShouldBe(NodeRecord.NoId);
        result.Reached.ShouldBeFalse();
    }

    [Test]
    public void Locate_ReturnsErrorForMissingFileLine()
    {
        var gameState = GameState.Instance;
        var result = gameState.Locate("missing.txt", 999);

        result.Ok.ShouldBeFalse();
        result.Error.ShouldContain("No dialogue entry found");
    }
}