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

	public record SceneChangeOptions {
		public ExitModeEnum ExitMode = ExitModeEnum.Delete;
		// TODO
		// public NodePath DeploymentParent = new NodePath("/root");
		public Variant[] Args = [];
		// TODO
		// public LoadingModeEnum LoadingMode = LoadingModeEnum.LoadingScene;
	}

	/// <summary>
	/// Determines what should be done with the previous scene when a new one is pushed to the stack.
	/// Scenes should be able to determine their own default behavior, but programmers should also be able to
	/// override the default behavior on a per-scene-change-call basis.
	/// </summary>
	public enum ExitModeEnum {
		/// <summary>
		/// The old scene is removed from the tree and deleted. This is analogous to the behavior of
		/// SceneTree.change_scene_to_file() and Node.queue_free(); and is the default behavior.
		/// </summary>
		Delete,
		/// <summary>
		/// The old scene is removed from the tree, but is not deleted. When this scene returns to the top of the stack, it
		/// will be added immediately to the tree again, no loading needed, and it will be in the same state as it was when
		/// it was popped.
		/// </summary>
		Detach,
		/// <summary>
		/// Combination of Hide and Disable options. The scene will remain in the tree, but will be hidden and its
		/// processing will be disabled.
		/// </summary>
		HideAndDisable,
		/// <summary>
		/// The old scene is kept in the tree but its visibility is set to false. Only works if the top node of the scene is
		/// a Node2D or Control.
		/// </summary>
		Hide,
		/// <summary>
		/// The old scene is kept in the tree but its processing is disabled. It will still be visible.
		/// </summary>
		Disable,
		/// <summary>
		/// Nothing is done to the existing scene, other than making SceneTree.current_scene point to the new scene. The
		/// old scene will be kept in the tree, visible and processing, in parallel to the new scene.
		/// </summary>
		Nothing,
	}

	// TODO
	// /// <summary>
	// /// Determines how the next scene will be loaded, when changing scenes.
	// /// </summary>
	// public enum LoadingModeEnum {
	// 	/// <summary>
	// 	/// The next scene will be loaded synchronously. The user will see a black screen (unless other visible nodes
	// 	/// are in the tree) until the next scene is fully loaded. This is the same behavior as
	// 	/// SceneTree.change_scene_to_file() and SceneTree.change_scene(). This is the default setting unless a loading
	// 	/// scene is configured in the addon settings; then, the LoadingScene option becomes the default.
	// 	/// </summary>
	// 	Sync,
	// 	/// <summary>
	// 	/// After exiting the current scene, a loading scene will be shown while the next scene is being loaded. This is
	// 	/// the default if a loading scene is configured in the addon settings.
	// 	/// </summary>
	// 	LoadingScene,
	// }

	// TODO
	// /// <summary>
	// /// Determines what should happen when a scene is pushed to the stack while another instance of its type is already
	// /// in the stack.
	// /// Scenes should be able to determine their own default behavior, but programmers should also be able to
	// /// override the default behavior on a per-scene-change-call basis.
	// /// </summary>
	// private enum SceneStackTypeEnum {
	// 	/// <summary>
	// 	/// A new instance of the scene will be created and pushed to the stack even if another instance of it already
	// 	/// exists in the stack. This is the default behavior.
	// 	/// </summary>
	// 	Default,
	// 	/// <summary>
	// 	/// If another instance of the scene existing on top of the stack, it will be replaced by the new instance.
	// 	/// </summary>
	// 	SingleOnTop,
	// 	/// <summary>
	// 	/// If another instance of the scene existing anywhere in the stack, the stack will be popped until the existing
	// 	/// instance, then it will be replaced by the new instance.
	// 	/// </summary>
	// 	SingleInStack,
	// }

	// -----------------------------------------------------------------------------------------------------------------
	// GODOT EVENTS
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
	// SETUP METHODS
	// -----------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// Loads the scene defined by the developer in the settings. This scene will be used when transitioning between
	/// scenes. This is useful as a loading screen.
	/// </summary>
    private void SetupTransitionScene()
    {
		if (!GodotUtil.TryGetSetting(SETTING_TRANSITION_SCENE, out string transitionScenePath) || string.IsNullOrEmpty(transitionScenePath)) {
			return;
		}
		PackedScene transitionScene = ResourceLoader.Load<PackedScene>(transitionScenePath, nameof(PackedScene));
		this.AddChild(transitionScene.Instantiate());
    }

	// -----------------------------------------------------------------------------------------------------------------
	// SCENE LOANDING & CHANGING METHODS
	// -----------------------------------------------------------------------------------------------------------------

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

	private async Task ReturnToPreviousScene(SceneHistoryItem item)
	{
		await this.ExitCurrentScene(ExitModeEnum.Delete);

		if (item.PreviousSceneInstance != null) {
			this.GetTree().CurrentScene = item.PreviousSceneInstance;
			switch (item.Options.ExitMode) {
				case ExitModeEnum.Detach:
					this.GetTree().Root.AddChild(item.PreviousSceneInstance);
					break;
				case ExitModeEnum.Hide:
					item.PreviousSceneInstance.Set("visible", true);
					break;
				case ExitModeEnum.Disable:
					item.PreviousSceneInstance.ProcessMode = item.ProcessMode;
					break;
				case ExitModeEnum.HideAndDisable:
					item.PreviousSceneInstance.Set("visible", true);
					item.PreviousSceneInstance.ProcessMode = item.ProcessMode;
					break;
			}
		} else {
			this.ChangeSceneSync(this.PeekHistory().SceneName);
		}
	}

	private Task<Node> ChangeSceneAsync(string sceneName, SceneChangeOptions options)
		=> this.ChangeSceneAsync<Node>(sceneName, options);

    private async Task<T> ChangeSceneAsync<T>(string sceneName, SceneChangeOptions options) where T : Node {
		if (this.TransitionInProgress) {
			throw new Exception("SuperSceneManager: Transition already in progress.");
		}
		try {
			this.TransitionInProgress = true;
			return await this.PerformSceneChangeAsync<T>(sceneName, options);
		} finally {
			this.TransitionInProgress = false;
		}
	}

	private async Task<T> PerformSceneChangeAsync<T>(string sceneName, SceneChangeOptions options) where T : Node {
		// Validate next scene
		string scenePath = SuperSceneManager.GetScenePath(sceneName);

		// Exit current scene
		await this.ExitCurrentScene(options.ExitMode);

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
		await this.EmitSignalAsync(SignalName.BeforeSceneEnter, [sceneName, node, ..options.Args]);
		if (instance.HasMethod("_before_enter_tree")) {
			instance.Call("_before_enter_tree", options.Args);
		}

		// Enter next scene
		this.GetTree().Root.AddChild(instance);
		this.GetTree().CurrentScene = instance;
		await this.EmitSignalAsync(SignalName.AfterSceneEnter, [sceneName, node, ..options.Args]);

		return instance;
	}

	private async Task ExitCurrentScene(ExitModeEnum exitMode)
	{
		Node oldScene = this.GetTree().CurrentScene;
		await this.EmitSignalAsync(SignalName.BeforeSceneExit, [oldScene]);
		switch (exitMode) {
			case ExitModeEnum.Delete:
				await this.FreeNodeAsync(oldScene);
				break;
			case ExitModeEnum.Detach:
				oldScene.GetParent().RemoveChild(oldScene);
				break;
			case ExitModeEnum.Hide:
				oldScene.Set("visible", false);
				break;
			case ExitModeEnum.Disable:
				oldScene.ProcessMode = ProcessModeEnum.Disabled;
				break;
			case ExitModeEnum.HideAndDisable:
				oldScene.Set("visible", false);
				oldScene.ProcessMode = ProcessModeEnum.Disabled;
				break;
		}
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

	private SceneHistoryItem PushHistoryItem(string sceneName, SceneChangeOptions options, bool hasTask = false)
	{
		SceneHistoryItem item = new() {
			SceneName = sceneName,
			Options = options,
			PreviousSceneInstance = options.ExitMode != ExitModeEnum.Delete
				? this.GetTree().CurrentScene
				: null,
			ProcessMode = options.ExitMode == ExitModeEnum.Disable || options.ExitMode == ExitModeEnum.HideAndDisable
				? this.GetTree().CurrentScene?.ProcessMode ?? ProcessModeEnum.Inherit
				: ProcessModeEnum.Inherit,
			TaskCompletionSource = hasTask ? new() : null,
		};
		this._sceneStack.Push(item);
		return item;
	}

    public void PushScene(string sceneName, params Variant[] args)
	{
		this.PushHistoryItem(sceneName, new() { Args = args });
		try {
			this.ChangeSceneSync(sceneName);
		} catch {
			this._sceneStack.Pop();
			throw;
		}
	}

	public async Task<T> PushSceneWithReturn<[MustBeVariant] T>(string sceneName, SceneChangeOptions? options = null)
		=> (await this.PushSceneWithReturn(sceneName, options)).As<T>();

	/// <summary>
	/// Returns a Task that is resolved when the pushed scene is popped. The result value of the task is the value
	/// passed to PopScene(). Note that if the current scene is deleted (when PreviousSceneActionEnum is Free), the
	/// task will be resolved immediately with a result of Nil. This is to prevent memory leaks.
	/// </summary>
    public async Task<Variant> PushSceneWithReturn(string sceneName, SceneChangeOptions? options = null)
	{
		options ??= new();
        SceneHistoryItem item = this.PushHistoryItem(sceneName, options, hasTask: true);
		try {
			await this.ChangeSceneAsync(sceneName, options);
		} catch {
			this._sceneStack.Pop();
			item.TaskCompletionSource!.SetException(new Exception("Scene change failed."));
			throw;
		}
		return await item.TaskCompletionSource!.Task;
	}

	public void ReplaceScene(string sceneName, params Variant[] args)
	{
		SceneHistoryItem item = this._sceneStack.Pop();
		item.TaskCompletionSource?.SetException(new Exception("Scene was replaced."));
		this.PushHistoryItem(sceneName, new() { Args = args });
		try {
			this.ChangeSceneSync(sceneName);
		} catch {
			this._sceneStack.Pop();
			this._sceneStack.Push(item);
			throw;
		}
	}

	/// <summary>
	/// When popping a scene without passing a return value, any tasks that are waiting for the scene to be popped with
	/// a return value will be faulted with an exception.
	/// </summary>
	public async void PopScene()
	{
		SceneHistoryItem item = this._sceneStack.Pop();
		if (this._sceneStack.Count == 0) {
			this.Quit();
			return;
		}
		try {
			await this.ReturnToPreviousScene(item);
		} catch {
			this._sceneStack.Push(item);
			throw;
		}
		Callable.From(() => {
				item.TaskCompletionSource?.SetException(new Exception("Scene popped without a return value."));
			})
			.CallDeferred();
	}

    /// <summary>
    /// When popping a scene, a value can be passed back to the scene that pushed it into the stack. This return
    /// value should contain any resulting data that have been generated by the scene that is being popped.
    /// </summary>
    public async Task PopScene(Variant returnValue = new Variant())
	{
		SceneHistoryItem item = this._sceneStack.Pop();
		if (this._sceneStack.Count == 0) {
			this.Quit();
			return;
		}
		try {
			await this.ReturnToPreviousScene(item);
		} catch {
			this._sceneStack.Push(item);
			throw;
		}
		Callable.From(() => item.TaskCompletionSource?.SetResult(returnValue)).CallDeferred();
	}

	public void ResetScene() => this.ReplaceScene(this._sceneStack.Peek().SceneName, this._sceneStack.Peek().Options.Args);

	public async void Quit()
	{
		try {
			await this.ExitCurrentScene(ExitModeEnum.Delete);
		} catch {}
		this.GetTree().Quit();
	}
}
