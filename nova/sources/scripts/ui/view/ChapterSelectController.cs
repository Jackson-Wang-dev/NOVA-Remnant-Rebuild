using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Nova;

public partial class ChapterSelectController : ViewController
{
    [Export]
    private bool _unlockAllNodes;
    [Export]
    private bool _unlockDebugNodes;
    [Export]
    private Node _chapterList;

    private GameState _gameState;

    private List<string> _nodes;
    private HashSet<string> _activeNodes;
    private HashSet<string> _unlockedNodes;
    private List<Button> _buttons;

    public override void _EnterTree()
    {
        base._EnterTree();

        _gameState = GameState.Instance;

        // TODO: sort
        _nodes = _gameState.GetStartNodeNames(StartNodeType.All).ToList();
        _buttons = _nodes.Select(InitButton).ToList();
    }

    private void UpdateNodes()
    {
        _activeNodes = new(_gameState.GetStartNodeNames(
            _unlockDebugNodes ? StartNodeType.All : StartNodeType.Normal));
        var unlockedAtFirst = new HashSet<string>(_gameState.GetStartNodeNames(
            _unlockAllNodes ? StartNodeType.All : StartNodeType.Unlocked));

        // A node not unlocked at first (is_start(), not is_unlocked_start()/is_default_start()) is
        // still selectable once the player has actually reached it at least once - e.g. ch2.txt's
        // is_start() so ch1's title-screen chapter list doesn't show it as a fake "new game" entry,
        // but it must still show up (and stay selectable) after the player has played through ch1.
        _unlockedNodes = new(_activeNodes.Where(node =>
            unlockedAtFirst.Contains(node) || SaveManager.Instance.IsReachedAnyHistory(node, 0)));
    }

    private Button InitButton(string nodeName)
    {
        var button = new Button
        {
            Visible = false,
            Theme = Assets.Instance.DefaultTheme
        };
        button.Pressed += () => StartGame(nodeName);
        _chapterList.AddChild(button);
        return button;
    }

    private void UpdateButtons()
    {
        if (_activeNodes == null)
        {
            return;
        }

        for (var i = 0; i < _nodes.Count; i++)
        {
            var node = _nodes[i];
            var button = _buttons[i];
            if (_activeNodes.Contains(node))
            {
                button.Visible = true;
                if (_unlockedNodes.Contains(node))
                {
                    button.Text = I18n.__(_gameState.GetNode(node, false).DisplayNames);
                    button.Disabled = false;
                }
                else
                {
                    button.Text = I18n.__("title.selectchapter.locked");
                    button.Disabled = true;
                }
            }
            else
            {
                button.Visible = false;
            }
        }
    }

    public void StartGame(string nodeName)
    {
        // Reset/populate game state before the view transition starts (same ordering as
        // SaveLoadController.LoadSlot), not in ShowPanel's onFinish callback: that only fires after
        // GameView's fade-in tween completes, so for that whole 0.2s GameView would already be visible
        // and rendering leftover state from whatever was last on screen before returning to the title.
        _gameState.StartGame(nodeName);
        this.SwitchView<GameViewController>();
    }

    public override void ShowPanel(bool doTransition, Action onFinish)
    {
        UpdateNodes();
        if (_unlockedNodes.Count == 0)
        {
            Utils.Warn("Nova: No node is unlocked so the game cannot start. " +
                "Please use is_unlocked_start() rather than is_start() in your first node.");
        }
        else if (_unlockedNodes.Count == 1)
        {
            StartGame(_unlockedNodes.First());
            return;
        }
        UpdateButtons();

        base.ShowPanel(doTransition, onFinish);
    }
}
