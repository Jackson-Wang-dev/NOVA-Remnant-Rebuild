using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace Nova.Tests;

/// <summary>
/// Dev-only hot reload (GameState.ReloadScripts, see porting-guide.md Tier 5 / decision log) re-parses
/// the scenario files from disk via ScriptLoader.OnEnter() and resumes via the same MoveTo replay
/// mechanism LoadGame/Backlog-jump already use. These tests start a real game session against the
/// already-loaded "test_animation" scenario (see resources/scenarios/test_animation.txt) and step a
/// couple of dialogue entries forward before reloading - the same live process the game itself runs
/// in, not a mock.
/// </summary>
public class GameStateReloadTest : TestClass
{
    public GameStateReloadTest(Node testScene) : base(testScene) { }

    [Test]
    public void ReloadScripts_RebuildsGraphAndStaysResolvable()
    {
        var gameState = GameState.Instance;

        gameState.ReloadScripts();

        gameState.GetNode("test_animation").ShouldNotBeNull();
        gameState.GetStartNodeNames(StartNodeType.All).ShouldContain("test_animation");
    }

    [Test]
    public void ReloadScripts_ResumesAtCurrentPosition()
    {
        var gameState = GameState.Instance;

        gameState.StartGame("test_animation");
        gameState.Step();
        gameState.Step();

        var nodeRecordId = gameState.CurrentNodeRecordId;
        var dialogueIndex = gameState.CurrentDialogueIndex;
        var nodeName = gameState.CurrentNode.Name;

        gameState.ReloadScripts();

        gameState.CurrentNode.Name.ShouldBe(nodeName);
        gameState.CurrentNodeRecordId.ShouldBe(nodeRecordId);
        gameState.CurrentDialogueIndex.ShouldBe(dialogueIndex);
        gameState.IsRestoring.ShouldBeFalse();
    }
}
