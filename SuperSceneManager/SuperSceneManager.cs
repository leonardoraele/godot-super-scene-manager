#nullable enable
using System.Collections.Generic;
using Godot;

namespace Raele.SuperSceneManager;

public partial class SuperSceneManager : Node
{
	// -----------------------------------------------------------------------------------------------------------------
	// STATICS
	// -----------------------------------------------------------------------------------------------------------------

    public const string SETTING_SCENE_LIST = "addons/super_scene_manager/scene_list";
    public const string SETTING_TRANSITION_SCENE = "addons/super_scene_manager/transition_scene";
    // public const string SETTING_EXITING_SCENE_BEHAVIOR = "addons/super_scene_manager/exiting_scene_behavior";
	// public const string SETTING_ENTERING_SCENE_BEHAVIOR = "addons/super_scene_manager/entering_scene_behavior";
	public const string MAIN_SCENE = ":application/run/main_scene:";

    public static SuperSceneManager? Instance { get; private set; }

	// -----------------------------------------------------------------------------------------------------------------
	// EXPORTS
	// -----------------------------------------------------------------------------------------------------------------

	// [Export] public

	// -----------------------------------------------------------------------------------------------------------------
	// FIELDS
	// -----------------------------------------------------------------------------------------------------------------

    public bool TransitionInProgress { get; private set; } = false;
    private Stack<SceneHistoryItem> SceneStack = new();
	private Godot.Collections.Dictionary<string, string> AllScenes = ProjectSettings.GetSetting(SETTING_SCENE_LIST)
		.AsGodotDictionary<string, string>();
    private AsyncTaskHelper AsyncTaskHelper = new();
    private SignalAwaiter? SignalAwaiter;

    // -----------------------------------------------------------------------------------------------------------------
    // PROPERTIES
    // -----------------------------------------------------------------------------------------------------------------



    // -----------------------------------------------------------------------------------------------------------------
    // SIGNALS
    // -----------------------------------------------------------------------------------------------------------------

    [Signal] public delegate void BeforeSceneExitEventHandler();
	[Signal] public delegate void AfterSceneExitEventHandler();
	[Signal] public delegate void SceneLoadProgressEventHandler(int percentage);
	[Signal] public delegate void BeforeSceneEnterEventHandler();
	[Signal] public delegate void AfterSceneEnterEventHandler();

	// -----------------------------------------------------------------------------------------------------------------
	// INTERNAL TYPES
	// -----------------------------------------------------------------------------------------------------------------

	// private enum PreviousSceneAction {
	// 	Free,
	// 	Pause,
	// 	KeepAlive,
	// }

	// -----------------------------------------------------------------------------------------------------------------
	// EVENTS
	// -----------------------------------------------------------------------------------------------------------------

	public override void _EnterTree()
	{
		if (Instance != null) {
			GD.PrintErr("SuperSceneManager: Instance already exists, ignoring autoload request.");
			this.QueueFree();
			return;
		}
		Instance = this;
		this.SceneStack.Push(new() { sceneName = MAIN_SCENE });
	}
    public override void _ExitTree()
    {
        if (Instance == this) {
			Instance = null;
		}
    }

    public override void _Ready()
    {
        base._Ready();
		this.AddChild(this.AsyncTaskHelper);
		this.SetupTransitionScene();
    }

    // public override void _Process(double delta)
    // {
    // 	base._Process(delta);
    // }

    // public override void _PhysicsProcess(double delta)
    // {
    // 	base._PhysicsProcess(delta);
    // }

    // public override string[] _GetConfigurationWarnings()
    // 	=> base._PhysicsProcess(delta);

	// -----------------------------------------------------------------------------------------------------------------
	// SCENE LOANDING & CHANGING METHODS
	// -----------------------------------------------------------------------------------------------------------------

    private void SetupTransitionScene()
    {
		string? transitionScenePath = ProjectSettings.GetSetting(SETTING_TRANSITION_SCENE).AsString();
		if (string.IsNullOrEmpty(transitionScenePath)) {
			return;
		}
		PackedScene transitionScene = ResourceLoader.Load<PackedScene>(transitionScenePath, nameof(PackedScene));
		this.AddChild(transitionScene.Instantiate());
    }

	private string GetScenePath(string sceneName)
	{
		if (sceneName == MAIN_SCENE) {
			return ProjectSettings.GetSetting("application/run/main_scene").AsString();
		}
		if (!AllScenes.ContainsKey(sceneName)) {
			throw new System.Exception($"SuperSceneManager: Scene not found in project settings: {sceneName}");
		}
		if (AllScenes[sceneName] is not string scenePath) {
			throw new System.Exception($"SuperSceneManager: Invalid path for scene: {sceneName}");
		}
		if (!ResourceLoader.Exists(scenePath)) {
			throw new System.Exception($"SuperSceneManager: Scene not found in file system: [{sceneName}] {scenePath}");
		}
		return scenePath;
	}

	private void ChangeSceneSync(string sceneName)
	{
		string scenePath = this.GetScenePath(sceneName);
		this.GetTree().ChangeSceneToFile(scenePath);
	}

	private async void ChangeSceneAsync(string sceneName)
	{
		if (this.TransitionInProgress) {
			throw new System.Exception("SuperSceneManager: Transition already in progress.");
		}

		try {
			this.TransitionInProgress = true;

			// Validate next scene
			string scenePath = this.GetScenePath(sceneName);

			// Before exiting current scene
			this.SignalAwaiter = null;
			this.EmitSignal(SignalName.BeforeSceneExit);
			if (this.SignalAwaiter != null) {
				await this.SignalAwaiter;
			}

			// Free current scene
			await this.AsyncTaskHelper.FreeAsync(this.GetTree().CurrentScene);

			// After exiting scene
			this.SignalAwaiter = null;
			this.EmitSignal(SignalName.AfterSceneExit);
			if (this.SignalAwaiter != null) {
				await this.SignalAwaiter;
			}

			// Load next scene
			AsyncTaskHelper.LoadProgressEventHandler progressHandler = (string path, int progress) => {
				if (path == scenePath) {
					this.EmitSignal(SignalName.SceneLoadProgress, progress);
				}
			};
			this.AsyncTaskHelper.LoadProgress += progressHandler;
			PackedScene scene = await this.AsyncTaskHelper.LoadAsync<PackedScene>(scenePath);
			this.AsyncTaskHelper.LoadProgress -= progressHandler;

			// Before entering next scene
			this.SignalAwaiter = null;
			this.EmitSignal(SignalName.BeforeSceneEnter);
			if (this.SignalAwaiter != null) {
				await this.SignalAwaiter;
			}

			// Add scene to tree
			this.GetTree().Root.AddChild(this.GetTree().CurrentScene = scene.Instantiate());

			// After entering next scene
			this.SignalAwaiter = null;
			this.EmitSignal(SignalName.AfterSceneEnter);
			if (this.SignalAwaiter != null) {
				await this.SignalAwaiter;
			}
		} finally {
			this.TransitionInProgress = false;
		}
	}

	public void WaitSignal(GodotObject source, StringName signal) => this.SignalAwaiter = ToSignal(source, signal);

    // -----------------------------------------------------------------------------------------------------------------
    // SCENE NAVIGATION METHODS
    // -----------------------------------------------------------------------------------------------------------------

	public Variant[] GetCurrentSceneArguments() => this.SceneStack.Peek().args ?? [];

    public void PushScene(string sceneName, params Variant[] args)
	{
		this.ChangeSceneSync(sceneName);
		this.SceneStack.Push(new() { sceneName = sceneName, args = args });
	}

	public void ReplaceScene(string sceneName, params Variant[] args)
	{
		this.ChangeSceneSync(sceneName);
		this.SceneStack.Pop();
		this.SceneStack.Push(new() { sceneName = sceneName, args = args });
	}

	public void PopScene()
	{
		this.SceneStack.Pop();
		if (this.SceneStack.Count == 0) {
			this.Quit();
			return;
		}
		this.ChangeSceneSync(this.SceneStack.Peek().sceneName);
	}

	public void ResetCurrentScene()
	{
		this.ChangeSceneSync(this.SceneStack.Peek().sceneName);
	}

	public void ResetSceneStack(string replaceScene = MAIN_SCENE)
	{
		this.SceneStack.Clear();
		this.PushScene(replaceScene);
	}

	public void Quit() => this.GetTree().Quit();
}
