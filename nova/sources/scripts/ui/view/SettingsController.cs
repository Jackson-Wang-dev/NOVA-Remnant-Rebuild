using System;
using Godot;

namespace Nova;

/// <summary>
/// Settings panel. Scope this round is just the five SettingsManager bus volumes - Nova1's other Config
/// tabs (resolution/fullscreen/language/text speed/font size/fast-forward/per-character voice volume/
/// shortcut rebinding/button ring size) have no backend in nova2 yet, see porting-guide.md decision log.
/// </summary>
public partial class SettingsController : ViewController
{
    [Export]
    private HSlider _masterSlider;
    [Export]
    private HSlider _bgmSlider;
    [Export]
    private HSlider _bgsSlider;
    [Export]
    private HSlider _voiceSlider;
    [Export]
    private HSlider _sfxSlider;
    [Export]
    private Button _backButton;
    [Export]
    private Button _returnTitleButton;
    [Export]
    private Button _quitButton;

    private (string Bus, HSlider Slider)[] _busSliders;

    public bool FromTitle { get; set; }

    public override void _EnterTree()
    {
        base._EnterTree();

        _busSliders =
        [
            ("Master", _masterSlider),
            ("Bgm", _bgmSlider),
            ("Bgs", _bgsSlider),
            ("Voice", _voiceSlider),
            ("Sfx", _sfxSlider)
        ];

        foreach (var (bus, slider) in _busSliders)
        {
            slider.ValueChanged += value => SettingsManager.Instance.SetVolume(bus, (float)value);
        }

        // Not driven by I18nText: that script attaches to the node and replaces its C# wrapper type,
        // which breaks the [Export] Button NodePath binding above - see ConfirmDialog for the same note.
        _backButton.Text = I18n.__("help.close");
        _returnTitleButton.Text = I18n.__("ingame.title.button");
        _quitButton.Text = I18n.__("config.quitgame");

        _backButton.Pressed += CloseToOrigin;
        _returnTitleButton.Pressed += OnReturnTitle;
        _quitButton.Pressed += OnQuit;
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        _backButton.Pressed -= CloseToOrigin;
        _returnTitleButton.Pressed -= OnReturnTitle;
        _quitButton.Pressed -= OnQuit;
    }

    public override void ShowPanel(bool doTransition, Action onFinish)
    {
        foreach (var (bus, slider) in _busSliders)
        {
            slider.SetValueNoSignal(SettingsManager.Instance.GetVolume(bus));
        }

        _returnTitleButton.Visible = !FromTitle;

        base.ShowPanel(doTransition, onFinish);
    }

    private void CloseToOrigin()
    {
        if (FromTitle)
        {
            this.SwitchView<TitleController>();
        }
        else
        {
            this.SwitchView<GameViewController>();
        }
    }

    private void OnReturnTitle()
    {
        ConfirmDialog.Instance.Show("ingame.title.confirm", () => this.SwitchView<TitleController>());
    }

    private void OnQuit()
    {
        ConfirmDialog.Instance.Show("ingame.quit.confirm", Utils.Quit);
    }
}
