#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
	public const string INITIAL_SCENE = "__initial_scene__";

    public static SuperSceneManager Instance { get; private set; } = null!;
	private static readonly IReadOnlyDictionary<string, string> AllRegisteredScenes
		= ProjectSettings.GetSetting(SETTING_SCENE_LIST).AsGodotDictionary<string, string>();

	// -----------------------------------------------------------------------------------------------------------------
	// EXPORTS
	// -----------------------------------------------------------------------------------------------------------------

	// [Export] public

	// -----------------------------------------------------------------------------------------------------------------
	// FIELDS
	// -----------------------------------------------------------------------------------------------------------------

    public bool TransitionInProgress { get; private set; } = false;
    private Stack<SceneHistoryItem> _sceneStack = new();
	private TaskCompletionSource? TransitionPauseController;
	private Dictionary<SceneHistoryItem, TaskCompletionSource<Variant>> SceneReturnTasks = new();

    // -----------------------------------------------------------------------------------------------------------------
    // PROPERTIES
    // -----------------------------------------------------------------------------------------------------------------

	public IReadOnlyList<SceneHistoryItem> GetHistory() => this._sceneStack.ToArray();
	public SceneHistoryItem PeekHistory() => this._sceneStack.Peek();

    // -----------------------------------------------------------------------------------------------------------------
    // SIGNALS
    // -----------------------------------------------------------------------------------------------------------------

    [Signal] public delegate void BeforeSceneExitEventHandler(Node scene);
	[Signal] public delegate void AfterSceneExitEventHandler();
	[Signal] public delegate void SceneLoadProgressEventHandler(Variant percentage, string toScene, string fromScene);
	[Signal] public delegate void BeforeSceneEnterEventHandler(string sceneName, Node scene, Godot.Collections.Array @params);
	[Signal] public delegate void AfterSceneEnterEventHandler(string sceneName, Node scene, Godot.Collections.Array @params);

	// -----------------------------------------------------------------------------------------------------------------
	// INTERNAL TYPES
	// -----------------------------------------------------------------------------------------------------------------

	// TODO
	// /// <summary>
	// /// Determines what should be done with the previous scene when a new one is pushed to the stack.
	// /// Scenes should be able to determine their own default behavior, but programmers should also be able to
	// /// override the default behavior on a per-scene-change-call basis.
	// /// </summary>
	// private enum PreviousSceneActionEnum {
	// 	/// <summary>
	// 	/// The old scene is removed from the tree and deleted. This is analogous to the behavior of
	// 	/// SceneTree.change_scene_to_file() and Node.queue_free(); and is the default behavior.
	// 	/// </summary>
	// 	Free,
	// 	/// <summary>
	// 	/// The old scene is kept in the tree but its visibility is set to false.
	// 	/// </summary>
	// 	Hide,
	// 	/// <summary>
	// 	/// The old scene is kept in the tree but its processing is disabled.
	// 	/// </summary>
	// 	Disable,
	// 	/// <summary>
	// 	/// Nothing is done to the existing scene, other than making SceneTree.current_scene point to the new scene. The
	// 	/// old scene will be kept in the tree and processing in parallel to the new scene.
	// 	/// </summary>
	// 	KeepAlive,
	// }
	// /// <summary>
	// /// Determines what should happen when a scene is pushed to the stack while another instance of it is already in the
	// /// stack.
	// /// Scenes should be able to determine their own default behavior, but programmers should also be able to
	// /// override the default behavior on a per-scene-change-call basis.
	// /// </summary>
	// private enum SceneStackTypeEnum {
	// 	/// <summary>
	// 	/// A new instance of the scene will be created and pushed to the stack even if another instance of it already
	// 	/// exists in the stack.
	// 	/// </summary>
	// 	Default,
	// 	/// <summary>
	// 	/// If another instance of the scene existing on top of the stack, it will be replaced by the new instance.
	// 	/// </summary>
	// 	SingleOnTop,
	// 	/// <summary>
	// 	/// If another instance of the scene existing anywhere in the stack, the stack will be popped until the existing
	// 	/// instance is popped out of the stack, and then the new instance will be pushed to replace it.
	// 	/// </summary>
	// 	Single,
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
		this._sceneStack.Push(new() { SceneName = INITIAL_SCENE });
	}
    public override void _ExitTree()
    {
        if (Instance == this) {
			Instance = null!;
		}
    }

    public override void _Ready()
    {
        base._Ready();
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

	public static string GetScenePath(string sceneName)
	{
		if (sceneName == INITIAL_SCENE) {
			return ProjectSettings.GetSetting("application/run/main_scene").AsString();
		}
		if (!AllRegisteredScenes.ContainsKey(sceneName)) {
			throw new System.Exception($"SuperSceneManager: Scene not found in project settings: {sceneName}");
		}
		if (AllRegisteredScenes[sceneName] is not string scenePath) {
			throw new System.Exception($"SuperSceneManager: Invalid path for scene: {sceneName}");
		}
		if (!ResourceLoader.Exists(scenePath)) {
			throw new System.Exception($"SuperSceneManager: Scene not found in file system: [{sceneName}] {scenePath}");
		}
		return scenePath;
	}

	private void ChangeSceneSync(string sceneName)
	{
		string scenePath = SuperSceneManager.GetScenePath(sceneName);
		this.GetTree().ChangeSceneToFile(scenePath);
	}

    private async Task<T> ChangeSceneAsync<T>(string sceneName, Variant[] arguments) where T : Node {
		if (this.TransitionInProgress) {
			throw new Exception("SuperSceneManager: Transition already in progress.");
		}
		try {
			this.TransitionInProgress = true;
			return await this.PerformSceneChangeAsync<T>(sceneName, arguments);
		} finally {
			this.TransitionInProgress = false;
		}
	}

	private async Task<T> PerformSceneChangeAsync<T>(string sceneName, Variant[] arguments) where T : Node {
		// Validate next scene
		string scenePath = SuperSceneManager.GetScenePath(sceneName);

		// Exit current scene
		await this.ExitCurrentScene();

		// Load next scene
		PackedScene scene = await ResourceLoadingUtil.LoadSceneAsync(
			scenePath,
			progress => this.EmitSignal(SignalName.SceneLoadProgress, progress, sceneName, this.PeekHistory().SceneName)
		);
		Node node = scene.Instantiate();
		if (scene.Instantiate() is not T instance) {
			node.Free();
			throw new Exception($"SuperSceneManager: Scene [{sceneName}] is not of type {typeof(T).Name}");
		}

		// Before entering next scene
		await this.EmitSignalAsync(SignalName.BeforeSceneEnter, [sceneName, node, ..arguments]);
		if (instance.HasMethod("_before_enter_tree")) {
			instance.Call("_before_enter_tree", arguments);
		}

		// Enter next scene
		this.GetTree().Root.AddChild(instance);
		this.GetTree().CurrentScene = instance;
		await this.EmitSignalAsync(SignalName.AfterSceneEnter, [sceneName, node, ..arguments]);

		return instance;
	}

	private async Task ExitCurrentScene()
	{
		Node oldScene = this.GetTree().CurrentScene;
		await this.EmitSignalAsync(SignalName.BeforeSceneExit, [oldScene]);
		await this.FreeNodeAsync(oldScene);
		await this.EmitSignalAsync(SignalName.AfterSceneExit);
	}

	private async Task FreeNodeAsync(Node node)
	{
		TaskCompletionSource source = new();
		Callable.From(() => {
			node.Free();
			source.SetResult();
		}).CallDeferred();
		await source.Task;
	}

	private async Task EmitSignalAsync(StringName signalName, params Variant[] arguments)
	{
		this.EmitSignal(signalName, arguments);
		if (this.TransitionPauseController != null) {
			await this.TransitionPauseController.Task;
		}
	}

	public void SuspendTransition() {
		if (this.TransitionInProgress && this.TransitionPauseController == null) {
			this.TransitionPauseController = new();
		}
	}
	public void ResumeTransition() {
		this.TransitionPauseController?.SetResult();
		this.TransitionPauseController = null;
	}

    // -----------------------------------------------------------------------------------------------------------------
    // SCENE NAVIGATION METHODS
    // -----------------------------------------------------------------------------------------------------------------

    public void PushScene(string sceneName, params Variant[] args)
	{
		this._sceneStack.Push(new() { SceneName = sceneName, Args = args });
		this.ChangeSceneSync(sceneName);
	}

	/// <summary>
	/// Returns a Task that is resolved when the pushed scene is popped. The result value of the task is the value
	/// passed to PopScene(). Note that if the current scene is deleted (when PreviousSceneActionEnum is Free), the
	/// task will be resolved immediately with a result of Nil. This is to prevent memory leaks.
	/// </summary>
    public async Task<Variant> PushSceneWithReturn(string sceneName, params Variant[] args)
	{
		this._sceneStack.Push(new() { SceneName = sceneName, Args = args });
		this.SceneReturnTasks[this._sceneStack.Peek()] = new();
		this.ChangeSceneSync(sceneName);
		return new Variant();
	}

	public void ReplaceScene(string sceneName, params Variant[] args)
	{
		SceneHistoryItem item = this._sceneStack.Pop();
		if (this.SceneReturnTasks.TryGetValue(item, out TaskCompletionSource<Variant>? source)) {
			source.SetException(new Exception("Scene replaced."));
			this.SceneReturnTasks.Remove(item);
		}
		this._sceneStack.Push(new() { SceneName = sceneName, Args = args });
		this.ChangeSceneSync(sceneName);
	}

	/// <summary>
	/// When popping a scene without passing a return value, any tasks that are waiting for the scene to be popped with
	/// a return value will be faulted with an exception.
	/// </summary>
	public void PopScene()
	{
		SceneHistoryItem item = this._sceneStack.Pop();
		if (this._sceneStack.Count == 0) {
			this.Quit();
			return;
		}
		if (this.SceneReturnTasks.TryGetValue(item, out TaskCompletionSource<Variant>? source)) {
			source.SetException(new Exception("Scene popped without a return value."));
			this.SceneReturnTasks.Remove(item);
		}
		this.ChangeSceneSync(this._sceneStack.Peek().SceneName);
	}

	/// <summary>
	/// When popping a scene, a value can be passed back to the scene that pushed it into the stack. This return
	/// value should contain any resulting data that have been generated by the scene that is being popped.
	/// </summary>
	public void PopScene(Variant returnValue = new Variant())
	{
		SceneHistoryItem item = this._sceneStack.Pop();
		if (this._sceneStack.Count == 0) {
			this.Quit();
			return;
		}
		this.ChangeSceneSync(this._sceneStack.Peek().SceneName);
		if (this.SceneReturnTasks.TryGetValue(item, out TaskCompletionSource<Variant>? source)) {
			source.SetResult(returnValue);
			this.SceneReturnTasks.Remove(item);
		}
	}

	public void ResetScene() => this.ReplaceScene(this._sceneStack.Peek().SceneName, this._sceneStack.Peek().Args);

	public async void Quit()
	{
		await this.ExitCurrentScene();
		this.GetTree().Quit();
	}
}
