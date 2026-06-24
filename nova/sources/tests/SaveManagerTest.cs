using Chickensoft.GoDotTest;
using Godot;
using Shouldly;

namespace Nova.Tests;

/// <summary>
/// SaveManager is pure logic with no UI dependency (porting-guide.md Tier 0/M1 flagged it as a good
/// unit-test target that nobody had gotten to yet). Runs against the real, live SaveManager.Instance
/// (same process the game itself uses, see SmokeTest) rather than a mock, per this project's "don't
/// mock the live singletons" testing convention - which means every test here that mutates persistent
/// state (anything that flows through Flush(), e.g. SaveBookmark) must clean up after itself, since
/// SaveManager writes to the same user://save/ directory the user's real playthroughs use.
/// </summary>
public class SaveManagerTest : TestClass
{
    public SaveManagerTest(Node testScene) : base(testScene) { }

    [Test]
    public void NodeRecordTree_DedupsSameNameAndHash()
    {
        var manager = SaveManager.Instance;
        const string name = "__test_node_record_dedup__";

        var first = manager.GetNextNodeRecord(null, name, 1, 0);
        var second = manager.GetNextNodeRecord(null, name, 1, 0);

        second.Id.ShouldBe(first.Id, "revisiting the same (parent, name, variablesHash) should reuse the record, not create a sibling");

        var differentHash = manager.GetNextNodeRecord(null, name, 2, 0);
        differentHash.Id.ShouldNotBe(first.Id, "a different variablesHash under the same parent should create a new sibling record");
    }

    [Test]
    public void NodeRecordTree_GetPathToIsOrderedRootFirst()
    {
        var manager = SaveManager.Instance;

        var root = manager.GetNextNodeRecord(null, "__test_node_record_path_root__", 100, 0);
        var child = manager.GetNextNodeRecord(root, "__test_node_record_path_child__", 100, 5);
        var grandchild = manager.GetNextNodeRecord(child, "__test_node_record_path_grandchild__", 100, 9);

        var path = manager.GetPathTo(grandchild.Id);

        path.Count.ShouldBe(3);
        path[0].Id.ShouldBe(root.Id);
        path[1].Id.ShouldBe(child.Id);
        path[2].Id.ShouldBe(grandchild.Id);
    }

    [Test]
    public void ReachedDialogue_RoundTrips()
    {
        var manager = SaveManager.Instance;
        const string nodeName = "__test_reached_dialogue_node__";

        // No "not reached yet" precondition check here: SetReachedDialogue is a no-op once a key is
        // already marked reached (by design - see SaveManager.cs), and this same __test_xxx__ key is
        // never cleaned up between runs (no API to un-reach it, see porting-guide.md decision log), so
        // a rerun would find it already reached. The round-trip below still holds either way: either
        // this run is the one that actually writes TextHash=12345, or a previous run already did and
        // this Set is a harmless no-op - the read-back assertion is true in both cases.
        manager.SetReachedDialogue(new ReachedDialogueData
        {
            NodeName = nodeName,
            DialogueIndex = 0,
            NeedInterpolate = false,
            TextHash = 12345UL
        });

        manager.IsReachedAnyHistory(nodeName, 0).ShouldBeTrue();
        manager.GetReachedDialogue(nodeName, 0).TextHash.ShouldBe(12345UL);
    }

    [Test]
    public void ReachedEnd_RoundTrips()
    {
        var manager = SaveManager.Instance;
        const string endName = "__test_reached_end__";

        // No "not reached yet" precondition: same reasoning as ReachedDialogue_RoundTrips above - this
        // key is never un-reached between runs, so SetReachedEnd may be a no-op on a rerun, but the
        // postcondition (IsReachedEnd true) holds regardless of whether this call or an earlier run's
        // call is what actually set it.
        manager.SetReachedEnd(endName);
        manager.IsReachedEnd(endName).ShouldBeTrue();
    }

    [Test]
    public void GlobalVariable_RoundTripsAndRemoves()
    {
        var manager = SaveManager.Instance;
        const string varName = "__test_global_variable__";

        manager.GetGlobalVariable(varName).ShouldBeNull();

        manager.SetGlobalVariable(varName, 42.0);
        manager.GetGlobalVariable(varName).ShouldBe(42.0);

        manager.SetGlobalVariable(varName, null);
        manager.GetGlobalVariable(varName).ShouldBeNull();
    }

    [Test]
    public void Bookmark_RoundTripsThenDeletes()
    {
        var manager = SaveManager.Instance;
        // Deliberately far outside any range the Save/Load UI ever pages through (NormalSave starts at
        // 301, 9 slots per page), so this can never show up as a real-looking slot or steal the
        // "latest save" pick while the test runs.
        const int testSaveId = 1_000_001;
        var nodeRecord = manager.GetNextNodeRecord(null, "__test_bookmark_node__", 999, 0);

        try
        {
            manager.SaveBookmark(testSaveId, nodeRecord, 3, default);

            var loaded = manager.LoadBookmark(testSaveId);
            loaded.ShouldNotBeNull();
            loaded.NodeRecordId.ShouldBe(nodeRecord.Id);
            loaded.DialogueIndex.ShouldBe(3);

            manager.BookmarksMetadata.ShouldContainKey(testSaveId);
        }
        finally
        {
            manager.DeleteBookmark(testSaveId);
        }

        manager.BookmarksMetadata.ShouldNotContainKey(testSaveId);
        manager.LoadBookmark(testSaveId).ShouldBeNull();
    }
}
