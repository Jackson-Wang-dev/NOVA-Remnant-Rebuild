using System;
using Godot;

namespace Nova;

public partial class PanelController : Control, IPanelController
{
    // Plain CanvasItem.modulate alpha tween, not the NovaScript AnimationEntry/PropertyState
    // machinery from Tier 4: that machinery exists to drive script-controlled, checkpointed
    // diegetic animation (it holds/bakes values for restore replay), which is the wrong tool for
    // transient C#-driven UI chrome like a panel open/close that isn't part of the script timeline.
    private const double TransitionDuration = 0.2;

    public bool Active => Visible;

    protected virtual void OnTransitionBegin() { }

    protected virtual void OnShowFinish() { }

    // this function calls before myPanel inactive
    protected virtual void OnHideComplete() { }

    // this function calls after myPanel inactive but before onFinish
    protected virtual void OnHideFinish() { }

    public virtual void ShowPanel(bool doTransition, Action onFinish)
    {
        if (Active)
        {
            onFinish?.Invoke();
            return;
        }

        void OnFinishAll()
        {
            OnShowFinish();
            onFinish?.Invoke();
        }

        Visible = true;
        if (doTransition)
        {
            OnTransitionBegin();
            Modulate = new Color(Modulate, 0f);
            var tween = CreateTween();
            tween.TweenProperty(this, "modulate:a", 1.0, TransitionDuration);
            tween.Finished += OnFinishAll;
        }
        else
        {
            Modulate = new Color(Modulate, 1f);
            OnFinishAll();
        }
    }

    // GDScript-callable, no-arg counterparts to the ShowPanel/HidePanel overloads above and to
    // ViewHelper's ShowPanelImmediate/HidePanelImmediate extension methods (same "false, null"
    // semantics) - GDScript's dynamic dispatch into C# can only see real instance methods, not C#
    // extension-method sugar, and can't marshal a bare Action parameter either (same class of gap as
    // ConfirmDialog.ShowNotice's "params object[]" - see that method's doc comment). Needed by
    // dialogue_box.gd's box_hide_show, the first GDScript caller of either Show/HidePanel.
    public void ShowPanelImmediate()
    {
        ShowPanel(false, null);
    }

    public void HidePanelImmediate()
    {
        HidePanel(false, null);
    }

    public virtual void HidePanel(bool doTransition, Action onFinish)
    {
        if (!Active)
        {
            onFinish?.Invoke();
            return;
        }

        void OnFinishAll()
        {
            OnHideFinish();
            onFinish?.Invoke();
        }

        if (doTransition)
        {
            OnTransitionBegin();
            var tween = CreateTween();
            tween.TweenProperty(this, "modulate:a", 0.0, TransitionDuration);
            tween.Finished += () =>
            {
                OnHideComplete();
                Visible = false;
                OnFinishAll();
            };
        }
        else
        {
            OnHideComplete();
            Visible = false;
            OnFinishAll();
        }
    }
}
