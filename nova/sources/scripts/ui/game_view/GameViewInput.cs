using Godot;

namespace Nova;

public class GameViewInput
{
    private readonly ButtonRingTrigger _buttonRingTrigger;
    private readonly GameViewController _gameView;

    private bool _needShowUI => !_gameView.UIActive;

    public ButtonRingTrigger ButtonRingTrigger { init => _buttonRingTrigger = value; }

    public GameViewInput(GameViewController gameView)
    {
        _gameView = gameView;
    }

    private void ClickForward()
    {
        _gameView.Step();
    }

    // Scrolling up to peek at the backlog is a standard VN convention (Nova1 had no equivalent since
    // it only exposed history through the menu, but nova2's mouse-driven UI makes it cheap to add).
    // Gated the same way as the ring trigger and Escape handling elsewhere: skip while UI is hidden
    // (the scroll should just reveal it, like any other input), the ring is open, or a dialog is up.
    private void OnMouseDown(InputEventMouseButton @event)
    {
        if (@event.ButtonIndex != MouseButton.WheelUp)
        {
            return;
        }

        if (_needShowUI || _buttonRingTrigger.ButtonShowing || ConfirmDialog.Instance.Active)
        {
            return;
        }

        _gameView.OpenBacklog();
    }

    private void OnMouseUp(InputEventMouseButton @event)
    {
        if (_needShowUI)
        {
            _gameView.ShowUI();
            return;
        }
        // Any click cancels Auto/Skip outright rather than also performing the click's usual action
        // (advance/open ring) - matches Nova1's "any click stops auto/fast-forward" and is the least
        // surprising choice (no risk of also eating an extra dialogue step in the same click).
        if (_gameView.AutoSkipActive)
        {
            _gameView.CancelAutoSkip();
            return;
        }
        if (_buttonRingTrigger.ButtonShowing)
        {
            // Right-click cancels the ring without acting on whatever sector is currently hovered;
            // only a left-click confirms the selection. Any other release (e.g. the synthetic wheel
            // release) is swallowed here too rather than falling through to ClickForward/ShowRing below.
            if (@event.ButtonIndex == MouseButton.Left)
            {
                var action = _buttonRingTrigger.ConfirmSelection();
                DispatchRingAction(action);
            }

            _buttonRingTrigger.HideRing();
            return;
        }

        // Swallowed rather than stepping or falling through to the ring - a click landing mid-wait
        // would otherwise race TickForceAdvance's own Step() once the wait condition clears (see
        // AutoSkipController.ForceAdvance), and a forced step is meant to play out untouched (that's
        // the entire point of force-advancing a video/cutscene instead of leaving it click-driven).
        if (@event.ButtonIndex == MouseButton.Left && !_gameView.IsForceAdvancePending)
        {
            ClickForward();
        }

        if (@event.ButtonIndex == MouseButton.Right)
        {
            _buttonRingTrigger.ShowRing(@event.Position);
        }
    }

    // Sector numbers match the icons baked into the button_ring_N.png wedge assets (save/load and
    // their "quick" variants, the gear that was already drawn for settings, the document icon for
    // history, the play icon for auto, and the fast-forward icon for skip).
    private void DispatchRingAction(string action)
    {
        switch (action)
        {
            case "1":
                _gameView.OpenBacklog();
                break;
            case "2":
                _gameView.ToggleAuto();
                break;
            case "3":
                _gameView.QuickSave();
                break;
            case "4":
                _gameView.OpenSaveLoad(SaveLoadMode.Save);
                break;
            case "5":
                _gameView.OpenSettings();
                break;
            case "6":
                _gameView.OpenSaveLoad(SaveLoadMode.Load);
                break;
            case "7":
                _gameView.QuickLoad();
                break;
            case "8":
                _gameView.ToggleSkip();
                break;
        }
    }

    public void HandleMouseButton(InputEventMouseButton @event)
    {
        if (@event.Pressed)
        {
            OnMouseDown(@event);
        }
        else
        {
            OnMouseUp(@event);
        }
    }
}
