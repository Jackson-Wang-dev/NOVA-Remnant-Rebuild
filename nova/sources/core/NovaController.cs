using System;
using System.Collections.Generic;
using Godot;
using Nova.Exceptions;
#if DEBUG
using System.Reflection;
using Chickensoft.GoDotTest;
#endif

namespace Nova;

public partial class NovaController : Node
{
	[Export]
	private string _scriptPath = "scenarios";

	private enum ObjectState
	{
		Uninitialized,
		Initializing,
		Initialized
	}

	private readonly Dictionary<Type, ISingleton> _objects = [];
	private readonly Dictionary<Type, ObjectState> _states = [];
	// Dictionary<TKey,TValue> enumeration order is not guaranteed to match insertion order - it's
	// an implementation detail that can and did change between otherwise-identical runs. Several
	// singletons (e.g. GameState/ViewManager) have a real mutual dependency that's only safe when
	// initialized in AddObjs' declared order, so that order must be tracked explicitly rather than
	// relying on _objects' enumeration.
	private readonly List<Type> _initOrder = [];

	public override void _EnterTree()
	{
		Instance = this;
		try
		{
			AddObjs();
			foreach (var type in _initOrder)
			{
				TryInit(type, _objects[type]);
			}
#if DEBUG
			// Cache PreviewBridge reference for _Process ticking - safe to do here since
			// AddObjs/initialization above already created and initialized it.
			if (_objects.TryGetValue(typeof(PreviewBridge), out var pb))
			{
				_previewBridge = (PreviewBridge)pb;
			}
#endif
		}
		catch (Exception e)
		{
			GD.PrintErr(e);
			Utils.Quit();
		}
	}

	public override void _Ready()
	{
		foreach (var type in _initOrder)
		{
			_objects[type].OnReady();
		}

#if DEBUG
		// Running inside the actual game (not a separate test project) so tests reach live
		// singletons (GameState/SaveManager/etc, already initialized above) for free - see
		// porting-guide.md's Tier 0 test framework decision log for why this replaced gdUnit4Net.
		// Invoked via `godot --headless --run-tests --quit-on-finish`; stripped from release builds
		// (see Nova.csproj's ExportRelease exclusion) so this branch and GoDotTest never ship.
		_testEnvironment = TestEnvironment.From(OS.GetCmdlineArgs());
		if (_testEnvironment.ShouldRunTests)
		{
			CallDeferred(nameof(RunTests));
		}
#endif
	}

#if DEBUG
	private TestEnvironment _testEnvironment;

	private void RunTests() => _ = GoTest.RunTests(Assembly.GetExecutingAssembly(), this, _testEnvironment);

	// PreviewBridge is DEBUG-only and must be ticked on the main thread to drain its message queue.
	// This is the designated handoff point: background thread accepts connections and enqueues raw
	// messages; _Process (main thread) dequeues and dispatches to GameState/etc.
	private PreviewBridge _previewBridge;

	public override void _Process(double delta)
	{
		if (_previewBridge != null)
		{
			_previewBridge.Tick();
		}
	}
#endif

	public override void _ExitTree()
	{
		foreach (var type in _initOrder)
		{
			_objects[type].OnExit();
		}
		_objects.Clear();
		_states.Clear();
		_initOrder.Clear();
	}

	private void AddObj<T>() where T : ISingleton, new()
	{
		AddObj(new T());
	}

	private void AddObj<T>(T obj) where T : ISingleton
	{
		_objects.Add(typeof(T), obj);
		_states.Add(typeof(T), ObjectState.Uninitialized);
		_initOrder.Add(typeof(T));
	}

	private void TryInit(Type type, ISingleton obj)
	{
		if (!_states.TryGetValue(type, out var state))
		{
			throw new InvalidAccessException($"Missing singleton {type}");
		}
		else if (state == ObjectState.Initializing)
		{
			throw new InvalidAccessException("Circular dependency");
		}
		else if (state == ObjectState.Initialized)
		{
			return;
		}
		_states[type] = ObjectState.Initializing;
		GD.Print($"Start init: {type}");
		obj.OnEnter();
		_states[type] = ObjectState.Initialized;
	}

	public bool CheckInit<T>() where T : ISingleton
	{
		return _states.TryGetValue(typeof(T), out var state) && state == ObjectState.Initialized;
	}

	public T GetObj<T>() where T : ISingleton
	{
		var type = typeof(T);
		var obj = (T)_objects[type];
		TryInit(type, obj);
		GD.Print($"Get obj: {type}");
		return obj;
	}

	private void AddObjs()
	{
		AddObj<Assets>();
		AddObj<SettingsManager>();
		AddObj<I18n>();
		AddObj<ObjectManager>();
		AddObj<SaveManager>();
		AddObj<Variables>();
		AddObj(new ScriptLoader(_scriptPath));
		AddObj<GameState>();
		AddObj<ViewManager>();
		AddObj<StateManager>();
		AddObj<VfxManager>();
#if DEBUG
		AddObj<PreviewBridge>();
#endif
	}

	public static NovaController Instance { get; private set; }

	// allow gdscript to access
	public ObjectManager ObjectManager => GetObj<ObjectManager>();

	public GameViewController GameViewController =>
		GetObj<ViewManager>().GetController<GameViewController>();

	public ConfirmDialog ConfirmDialog => ConfirmDialog.Instance;

	public NotifyToast NotifyToast => NotifyToast.Instance;

	// GameState itself isn't exposed (it's a plain ISingleton, not RefCounted/Node-derived like
	// ScriptLoader/ObjectManager/VfxManager - returning it as a property here would hit the same
	// "Invalid access to property" GDScript-can't-see-it gap as a params object[]/Action parameter,
	// just for a return type instead - see the decision log). Exposing just this one bool both avoids
	// changing GameState's class hierarchy and is all dialogue_box.gd's box_hide_show actually needs.
	public bool IsRestoring => GameState.Instance.IsRestoring;

	public ScriptLoader ScriptLoader => _objects[typeof(ScriptLoader)] as ScriptLoader;
}
