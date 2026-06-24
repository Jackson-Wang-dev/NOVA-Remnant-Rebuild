using System;
using Godot;

namespace Nova;

public partial class TitleController : ViewController
{
	// Every path back to the title screen (ending reached, in-game "返回标题", from Save/Settings)
	// converges here via SwitchView<TitleController> - stopping bgm/bgs once, centrally, on arrival
	// covers all of them without duplicating a stop call at each of those 5 call sites. bgm/bgs are
	// plain sibling nodes under NovaController's "Audio", not children of any View's Control, so
	// nothing about hiding GameView/SettingsView/etc ever touches them on its own.
	public override void ShowPanel(bool doTransition, Action onFinish)
	{
		StopChannel("bgm");
		StopChannel("bgs");
		base.ShowPanel(doTransition, onFinish);
	}

	private static void StopChannel(string name)
	{
		if (NovaController.Instance.ObjectManager.Objects.TryGetValue(name, out var obj) &&
			obj is PropertyState { Binding: AudioPlayerController player })
		{
			player.Stop();
		}
	}

	public void OnStartGame()
	{
		this.SwitchView<ChapterSelectController>();
	}

	public void OnLoad()
	{
		var saveLoad = ViewManager.Instance.GetController<SaveLoadController>();
		saveLoad.Mode = SaveLoadMode.Load;
		saveLoad.FromTitle = true;
		this.SwitchView<SaveLoadController>();
	}

	public void OnSettings()
	{
		var settings = ViewManager.Instance.GetController<SettingsController>();
		settings.FromTitle = true;
		this.SwitchView<SettingsController>();
	}

	public static void OnQuit()
	{
		Utils.Quit();
	}
}
