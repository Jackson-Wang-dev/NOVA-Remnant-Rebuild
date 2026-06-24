using System;
#if DEBUG
using System.Linq;
#endif
using Godot;

namespace Nova;

public partial class GameViewController : ViewController
{
	[Export]
	private PanelController _gameUI;
	[Export]
	private ButtonRingTrigger _buttonRingTrigger;
	[Export]
	private SubViewport _mainViewport;

	private GameState _gameState;
	private AnimationState _animation;
	private AutoVoiceController _autoVoice;
	private StateManager _stateManager;
	private GameViewInput _gameInput;
	private AutoSkipController _autoSkip;

	public DialogueBoxController CurrentDialogueBox { get; set; }

	public bool UIActive => _gameUI.Active;

	public override void _EnterTree()
	{
		base._EnterTree();

		// Assigned in code rather than via a local_to_scene ViewportTexture resource in the .tscn:
		// that resource's NodePath resolves relative to *this* nested scene's own root (game_view.tscn),
		// which has no "World" node - only the outer game.tscn does - so it can never resolve and Godot
		// logs "_setup_local_to_scene: Path to node is invalid". Getting the live texture via code sidesteps
		// that NodePath resolution entirely.
		GetNode<TextureRect>("Game").Texture = _mainViewport.GetTexture();

		_gameState = GameState.Instance;

		_gameState.DialogueChanged.Subscribe(OnDialogueChanged);
		_gameState.RouteEnded.Subscribe(OnRouteEnded);

		_stateManager = StateManager.Instance;
		_animation = _stateManager.Animation;
		_autoVoice = _stateManager.AutoVoice;

		_gameInput = new(this)
		{
			ButtonRingTrigger = _buttonRingTrigger
		};
		_autoSkip = new(this, _animation, _autoVoice);
	}

	public override void _ExitTree()
	{
		base._ExitTree();

		_gameState.DialogueChanged.Unsubscribe(OnDialogueChanged);
		_gameState.RouteEnded.Unsubscribe(OnRouteEnded);
		_autoSkip.Dispose();
	}

	// Active guards against Auto/Skip silently continuing to step through the story while a
	// Save/Load/Settings/Backlog panel has GameView switched out - it stays in the tree, just hidden,
	// so _Process still reaches it (same gotcha as the Esc handler and Backlog's right-click-to-close).
	public override void _Process(double delta)
	{
		if (!Active)
		{
			return;
		}
		_autoSkip.Tick(delta);
	}

	public void ToggleAuto() => _autoSkip.ToggleAuto();
	public void ToggleSkip() => _autoSkip.ToggleSkip();
	public void CancelAutoSkip() => _autoSkip.Cancel();
	public bool AutoSkipActive => _autoSkip.CurrentMode != AutoSkipController.Mode.Normal;

	// Script-armed one-shot auto-advance (see AutoSkipController.ForceAdvance/animation.gd's
	// auto_step()) - independent of whether the player has Auto/Skip toggled on, see that class's
	// doc comment. IsForceAdvancePending also gates GameViewInput's click-forward path: a click
	// landing in the same frame the wait condition clears would otherwise race the auto-tick's own
	// Step() and risk double-advancing past two dialogue entries on one click.
	public void ForceAdvance() => _autoSkip.ForceAdvance();
	public bool IsForceAdvancePending => _autoSkip.IsForceAdvancePending;

	public bool AllowSkipUnread
	{
		get => _autoSkip.AllowSkipUnread;
		set => _autoSkip.AllowSkipUnread = value;
	}

	public void Step()
	{
		var textRevealing = CurrentDialogueBox != null && CurrentDialogueBox.IsTextRevealing;
		var animationRunning = _animation.IsRunning;

		if (!textRevealing && !animationRunning)
		{
			_gameState.Step();
			return;
		}

		// A click while text is still revealing and/or a per-dialogue animation is still
		// running should finish/stop both at once (Nova1 GameViewInput.ClickForward stops
		// AnimationType.Text and AnimationType.PerDialogue together on the same click), not
		// advance the dialogue - the same click that completes the last entry shouldn't also
		// skip the next one.
		if (textRevealing)
		{
			CurrentDialogueBox.CompleteTextReveal();
		}
		if (animationRunning)
		{
			_animation.Stop();
		}
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			_gameInput.HandleMouseButton(mouseButton);
		}
	}

	// Active guards against firing while a Save/Load/Settings panel has already switched GameView out
	// (it stays in the tree, just hidden, so _Input still reaches it); ButtonShowing/ConfirmDialog.Active
	// guard against popping a second confirm on top of an already-open ring/dialog.
	public override void _Input(InputEvent @event)
	{
		if (!Active || _buttonRingTrigger.ButtonShowing || ConfirmDialog.Instance.Active)
		{
			return;
		}

		if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
		{
			ConfirmDialog.Instance.Show("ingame.title.confirm", () => this.SwitchView<TitleController>());
			GetViewport().SetInputAsHandled();
		}

#if DEBUG
		// Dev-only hot reload (R, mirrors Nova1's ReloadScriptsHelper) + seek between chapter start
		// nodes (N/P, mirrors Nova1's DebugJumpHelper.JumpChapter) - stripped from ExportRelease
		// builds via the same #if DEBUG convention as NovaController's test runner.
		if (@event is InputEventKey { Pressed: true, Echo: false } key)
		{
			switch (key.Keycode)
			{
				case Key.R:
					_gameState.ReloadScripts();
					GetViewport().SetInputAsHandled();
					break;
				case Key.N:
					JumpChapter(1);
					GetViewport().SetInputAsHandled();
					break;
				case Key.P:
					JumpChapter(-1);
					GetViewport().SetInputAsHandled();
					break;
			}
		}
#endif
	}

#if DEBUG
	/// <summary>
	/// "Seek to a specific node", scoped to chapter start nodes - the only nodes with stable,
	/// externally-meaningful names today. Restarts the target chapter from its beginning (not a
	/// resume-in-place like ReloadScripts), same as Nova1's JumpChapter.
	/// </summary>
	private void JumpChapter(int offset)
	{
		var chapters = _gameState.GetStartNodeNames(StartNodeType.All).ToList();
		var index = chapters.IndexOf(_gameState.CurrentNode?.Name) + offset;
		if (index < 0 || index >= chapters.Count)
		{
			Utils.Warn($"Chapter index {index} out of range.");
			return;
		}
		_gameState.StartGame(chapters[index]);
	}
#endif

	/// <summary>
	/// One frame of the live game view, used as a save slot thumbnail. Captured before the Save/Load
	/// panel is shown (so it reflects the game, not the panel itself) - see SaveLoadController.SaveSlot
	/// and QuickSave.
	/// </summary>
	public Image CaptureThumbnail()
	{
		return _mainViewport.GetTexture().GetImage();
	}

	public void OpenSaveLoad(SaveLoadMode mode)
	{
		var saveLoad = ViewManager.Instance.GetController<SaveLoadController>();
		saveLoad.Mode = mode;
		saveLoad.FromTitle = false;
		this.SwitchView<SaveLoadController>();
	}

	public void OpenSettings()
	{
		var settings = ViewManager.Instance.GetController<SettingsController>();
		settings.FromTitle = false;
		this.SwitchView<SettingsController>();
	}

	public void OpenBacklog()
	{
		this.SwitchView<BacklogViewController>();
	}

	/// <summary>
	/// Always overwrites the single fixed QuickSave slot (saveId == (int)BookmarkType.QuickSave) rather
	/// than rotating through a pool like Nova1's AutoSaveBookmark - this is a lightweight convenience
	/// hotkey, not a managed pool (see porting-guide.md decision log). Still gets a thumbnail since the
	/// slot is visible in the Load panel's QuickSave page.
	/// </summary>
	public void QuickSave()
	{
		ConfirmDialog.Instance.Show("bookmark.quicksave.confirm", () =>
		{
			var image = CaptureThumbnail();
			_gameState.SaveBookmark((int)BookmarkType.QuickSave);
			SaveManager.Instance.SaveScreenshot((int)BookmarkType.QuickSave, image);
			ConfirmDialog.Instance.ShowNotice("bookmark.quicksave.complete");
		});
	}

	public void QuickLoad()
	{
		if (!SaveManager.Instance.BookmarksMetadata.ContainsKey((int)BookmarkType.QuickSave))
		{
			ConfirmDialog.Instance.ShowNotice("bookmark.quickload.nosave");
			return;
		}

		ConfirmDialog.Instance.Show("bookmark.quickload.confirm",
			() => _gameState.LoadGame((int)BookmarkType.QuickSave));
	}

	private void OnDialogueChanged(DialogueChangedData dialogueData)
	{
		CurrentDialogueBox?.DisplayDialogue(dialogueData.DisplayData);
	}

	private void OnRouteEnded(ReachedEndData endData)
	{
		this.SwitchView<TitleController>();
	}

	public void ShowUI(Action onFinish)
	{
		_gameUI.ShowPanel(onFinish);
	}

	public void HideUI(Action onFinish)
	{
		_gameUI.HidePanel(onFinish);
	}

	// for gdscript
	public void ShowUI()
	{
		ShowUI(null);
	}

	public void HideUI()
	{
		_gameUI.HidePanel(null);
	}

	public void Switch(DialogueBoxController box, bool cleanText = true)
	{
		if (CurrentDialogueBox == box)
		{
			box?.ShowPanelImmediate();
			// Do not clean text
			return;
		}

		CurrentDialogueBox?.HidePanelImmediate();
		if (box != null)
		{
			box.ShowPanelImmediate();
			if (cleanText)
			{
				box.NewPage();
			}
		}

		CurrentDialogueBox = box;
	}

	public void SwitchDialogueBox(PropertyState box, bool cleanText = true)
	{
		// box is null for presets with no box (e.g. set_box("hide")), meaning "hide the current
		// dialogue box and switch to none" - Switch already handles a null target correctly.
		Switch(box?.Binding as DialogueBoxController, cleanText);
	}
}
