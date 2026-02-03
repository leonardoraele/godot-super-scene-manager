using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Raele.GodotUtils.Extensions;

namespace Raele.SuperSceneManager;

public partial class SceneManager : Node
{
	//==================================================================================================================
	// STATICS
	//==================================================================================================================

	public static SceneManager Singleton => Engine.GetSceneTree().Root.GetNode<SceneManager>(nameof(SceneManager));
	private static readonly Godot.Collections.Dictionary<string, string> SceneDict
		= ProjectSettings.GetSetting(Consts.SettingNames.SceneList).AsGodotDictionary<string, string>();

	//==================================================================================================================
	// FIELDS
	//==================================================================================================================

	public bool TransitionInProgress { get; private set; } = false;
	private Stack<HistoryItem> _sceneStack = new();
	private TaskCompletionSource? TransitionPauseController;
	public string MainScenePath { get; private set; } = "";
	public string MainSceneName { get; private set; } = "";

	//==================================================================================================================
	// COMPUTED PROPERTIES
	//==================================================================================================================

	public IReadOnlyList<HistoryItem> GetHistory() => this._sceneStack.ToArray();
	public HistoryItem PeekHistory() => this._sceneStack.Peek();

	//==================================================================================================================
	// SIGNALS
	//==================================================================================================================

	[Signal] public delegate void BeforeSceneExitEventHandler(Node scene);
	[Signal] public delegate void AfterSceneExitEventHandler();
	[Signal] public delegate void SceneLoadProgressEventHandler(Variant percentage, string toScene, string fromScene);
	[Signal] public delegate void BeforeSceneEnterEventHandler(string sceneName, Node scene, Godot.Collections.Array @params);
	[Signal] public delegate void AfterSceneEnterEventHandler(string sceneName, Node scene, Godot.Collections.Array @params);

	//==================================================================================================================
	// INTERNAL TYPES
	//==================================================================================================================

	public record SceneChangeOptions {
		public SceneExitStrategyEnum ExitStrategy = SceneExitStrategyEnum.Delete;
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
	public enum SceneExitStrategyEnum {
		/// <summary>
		/// The old scene is removed from the tree and deleted. This is analogous to the behavior of
		/// SceneTree.change_scene_to_file() and Node.queue_free(); and is the default behavior.
		///
		/// ⚠ Do not use this strategy when pushing a scene with <see cref="PushSceneWithReturn"/>, or the previous
		/// scene might be kept in memory until the new scene ends. Also, the node will still be deleted, and the
		/// in-memory node will be invalidated, which might cause issues.
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
		/// The old scene is kept in the tree but its processing is disabled. It will still be visible, but a canvas
		/// layer is placed in front of it with a grayed out texture to contrast visually with the new scene, indicating
		/// that it is disabled.
		/// </summary>
		DisableAndTint,
		/// <summary>
		/// The old scene is kept in the tree but its processing is disabled. It will still be visible.
		/// </summary>
		Disable,
		/// <summary>
		/// A canvas layer is placed in front of it with a grayed out texture to contrast visually with the new scene.
		/// The old scene is kept visible and processing in parallel to the new scene, albeit with a tint, and it ceases
		/// to be the SceneTree.current_scene.
		/// </summary>
		Tint,
		/// <summary>
		/// Nothing is done to the existing scene, other than making SceneTree.current_scene point to the new scene. The
		/// old scene will be kept in the tree, visible and processing, in parallel to the new scene.
		/// </summary>
		Nothing,
	}

	// public enum PopUpType {
	// 	/// <summary>
	// 	/// A box with the "ℹ" icon, a title, an optional message, and an OK button.
	// 	/// </summary>
	// 	Info,

	// 	/// <summary>
	// 	/// A box with the "⚠" icon, a title, an optional  message, and an OK button.
	// 	/// </summary>
	// 	Warn,

	// 	/// <summary>
	// 	/// A box with the "⛔" icon, a title, an optional message, and an OK button.
	// 	/// </summary>
	// 	Error,

	// 	/// <summary>
	// 	/// A box with a question mark icon, a title, an optional message, and OK and Cancel buttons.
	// 	/// </summary>
	// 	Confirm,

	// 	/// <summary>
	// 	/// A box with the "⚠" icon, a title, an optional message, and OK and Cancel buttons.
	// 	/// </summary>
	// 	WarnConfirm,

	// 	/// <summary>
	// 	/// A box with a question mark icon, a title, an optional message, and Yes and No buttons.
	// 	/// </summary>
	// 	Query,

	// 	// /// <summary>
	// 	// /// A box with a question mark icon, a title, an optional message, and some buttons with custom labels.
	// 	// /// </summary>
	// 	// Options,
	// }

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

	//==================================================================================================================
	// OVERRIDES
	//==================================================================================================================

	public override void _Ready()
	{
		base._Ready();
		this.MainScenePath = ProjectSettings.GetSetting("application/run/main_scene").AsString();
		this.MainSceneName = SceneDict.First(kv => kv.Value == this.MainScenePath).Key;
		this._sceneStack.Push(new() { SceneName = this.MainSceneName });
		this.SetupTransitionScene();
	}

	//==================================================================================================================
	// SETUP METHODS
	//==================================================================================================================

	/// <summary>
	/// Loads the scene defined in the TransitionScene project setting.
	///
	/// This scene will be used when transitioning between scenes. This is useful as a loading screen.
	/// </summary>
	private void SetupTransitionScene()
	{
		if (!ProjectSettings.TryGetSetting(Consts.SettingNames.TransitionScenePath, out string? transitionScenePath) || string.IsNullOrEmpty(transitionScenePath))
			return;
		PackedScene transitionScene = ResourceLoader.Load<PackedScene>(transitionScenePath, nameof(PackedScene));
		this.AddChild(transitionScene.Instantiate());
	}

	//==================================================================================================================
	// SCENE LOADING & CHANGING METHODS
	//==================================================================================================================

	public static string GetScenePath(string sceneName)
	{
		if (!SceneDict.ContainsKey(sceneName)) {
			throw new Exception($"SuperSceneManager: Scene not found in project settings: {sceneName}");
		}
		if (SceneDict[sceneName] is not string scenePath) {
			throw new Exception($"SuperSceneManager: Invalid path for scene: {sceneName}");
		}
		if (!ResourceLoader.Exists(scenePath)) {
			throw new Exception($"SuperSceneManager: Scene not found in file system: [{sceneName}] {scenePath}");
		}
		return scenePath;
	}

	private void ChangeSceneSync(string sceneName)
	{
		string scenePath = GetScenePath(sceneName);
		this.GetTree().ChangeSceneToFile(scenePath);
	}

	private async Task ReturnToPreviousScene(HistoryItem item)
	{
		await this.ExitCurrentScene(SceneExitStrategyEnum.Delete);

		if (item.PreviousSceneInstance != null) {
			this.GetTree().CurrentScene = item.PreviousSceneInstance;
			switch (item.Options.ExitStrategy) {
				case SceneExitStrategyEnum.Detach:
					this.GetTree().Root.AddChild(item.PreviousSceneInstance);
					break;
				case SceneExitStrategyEnum.Hide:
					item.PreviousSceneInstance.Set("visible", true);
					break;
				case SceneExitStrategyEnum.Disable:
					item.PreviousSceneInstance.ProcessMode = item.ProcessMode;
					break;
				case SceneExitStrategyEnum.HideAndDisable:
					item.PreviousSceneInstance.Set("visible", true);
					item.PreviousSceneInstance.ProcessMode = item.ProcessMode;
					break;
			}
		} else {
			await this.ChangeSceneAsync(this.PeekHistory().SceneName);
		}
	}

	private Task<Node> ChangeSceneAsync(string sceneName, SceneChangeOptions? options = null)
		=> this.ChangeSceneAsync<Node>(sceneName, options ?? default);

	private async Task<T> ChangeSceneAsync<T>(string sceneName, SceneChangeOptions? options = null) where T : Node {
		if (this.TransitionInProgress) {
			throw new Exception("SuperSceneManager: Transition already in progress.");
		}
		try {
			this.TransitionInProgress = true;
			return await this.PerformSceneChangeAsync<T>(sceneName, options ?? default);
		} finally {
			this.TransitionInProgress = false;
		}
	}

	private async Task<T> PerformSceneChangeAsync<T>(string sceneName, SceneChangeOptions? options = null) where T : Node {
		options ??= new();

		// Validate next scene
		string scenePath = GetScenePath(sceneName);

		// Exit current scene
		await this.ExitCurrentScene(options.ExitStrategy);

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
		await this.EmitSignalAsync(SignalName.BeforeSceneEnter, [sceneName, instance, ..options.Args]);
		Variant maybeTask = instance.HasMethod("_before_enter_tree") ? instance.Call("_before_enter_tree", options.Args)
			: instance.HasMethod("_BeforeEnterTree") ? instance.Call("_BeforeEnterTree", options.Args)
			: new Variant();

		if (maybeTask.VariantType == Variant.Type.Signal) {
			await maybeTask.AsSignal();
		}

		// Enter next scene
		this.GetTree().Root.AddChild(instance);
		this.GetTree().CurrentScene = instance;
		await this.EmitSignalAsync(SignalName.AfterSceneEnter, [sceneName, instance, ..options.Args]);

		return instance;
	}

	private async Task ExitCurrentScene(SceneExitStrategyEnum exitMode)
	{
		Node oldScene = this.GetTree().CurrentScene;
		await this.EmitSignalAsync(SignalName.BeforeSceneExit, [oldScene]);
		switch (exitMode) {
			case SceneExitStrategyEnum.Delete:
				await this.FreeNodeAsync(oldScene);
				break;
			case SceneExitStrategyEnum.Detach:
				oldScene.GetParent().RemoveChild(oldScene);
				break;
			case SceneExitStrategyEnum.Hide:
				oldScene.Set("visible", false);
				break;
			case SceneExitStrategyEnum.Disable:
				oldScene.ProcessMode = ProcessModeEnum.Disabled;
				break;
			case SceneExitStrategyEnum.HideAndDisable:
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

	//==================================================================================================================
	// SCENE NAVIGATION METHODS
	//==================================================================================================================

	private HistoryItem PushHistoryItem(string sceneName, SceneChangeOptions options, bool hasTask = false)
	{
		HistoryItem item = new() {
			SceneName = sceneName,
			Options = options,
			PreviousSceneInstance = options.ExitStrategy != SceneExitStrategyEnum.Delete
				? this.GetTree().CurrentScene
				: null,
			ProcessMode = options.ExitStrategy == SceneExitStrategyEnum.Disable || options.ExitStrategy == SceneExitStrategyEnum.HideAndDisable
				? this.GetTree().CurrentScene?.ProcessMode ?? ProcessModeEnum.Inherit
				: ProcessModeEnum.Inherit,
			TaskCompletionSource = hasTask ? new() : null,
		};
		this._sceneStack.Push(item);
		return item;
	}

	public void PushScene(string sceneName, params Variant[] args)
		=> this.PushScene(sceneName, new SceneChangeOptions() { Args = args });
	public async void PushScene(string sceneName, SceneChangeOptions options)
	{
		this.PushHistoryItem(sceneName, options);
		try {
			await this.ChangeSceneAsync(sceneName, options);
		} catch {
			this._sceneStack.Pop();
			throw;
		}
	}

	public Task PushSceneWithReturn(string sceneName, params Variant[] args)
		=> this.PushSceneWithReturn(sceneName, new SceneChangeOptions() { Args = args });
	public Task<T> PushSceneWithReturn<[MustBeVariant] T>(string sceneName, params Variant[] args)
		=> this.PushSceneWithReturn<T>(sceneName, new SceneChangeOptions() { Args = args });
	public async Task<T> PushSceneWithReturn<[MustBeVariant] T>(string sceneName, SceneChangeOptions options)
		=> (await this.PushSceneWithReturn(sceneName, options)).As<T>();

	/// <summary>
	/// Returns a Task that is resolved when the pushed scene is popped. The result value of the task is the value
	/// passed to PopScene(). Note that if the current scene is deleted (when PreviousSceneActionEnum is Free), the
	/// task will be resolved immediately with a result of Nil. This is to prevent memory leaks.
	/// </summary>
	public async Task<Variant> PushSceneWithReturn(string sceneName, SceneChangeOptions options)
	{
		options ??= new() {
			ExitStrategy = SceneExitStrategyEnum.HideAndDisable,
		};
		if (options.ExitStrategy == SceneExitStrategyEnum.Delete) {
			GD.PushWarning("Pushing a scene with return value using ExitStrategy.Delete. This is not recommended as it might cause issues since the node will be deleted from the engine but corresponding Godot.GodotObject objects refered by the stack awaiting the Task will be invalidated but not collected collected by GC.");
		}
		HistoryItem item = this.PushHistoryItem(sceneName, options, hasTask: true);
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
		=> this.ReplaceScene(sceneName, new SceneChangeOptions() { Args = args });
	public async void ReplaceScene(string sceneName, SceneChangeOptions options)
	{
		HistoryItem item = this._sceneStack.Pop();
		item.TaskCompletionSource?.SetException(new Exception("Scene was replaced."));
		this.PushHistoryItem(sceneName, options);
		try {
			await this.ChangeSceneAsync(sceneName, options);
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
	public async Task PopScene()
	{
		HistoryItem item = await this._PopScene();
		if (item.TaskCompletionSource != null) {
			item.TaskCompletionSource?.SetException(new Exception("Scene popped without a return value."));
		}
	}

	/// <summary>
	/// Pop scene with an error. If the previous scene is waiting for a return, the Task will be faulted with the
	/// provided exception. Otherwise, the exception will be logged and the scene will be popped.
	/// </summary>
	public async Task PopScene(Exception exception)
	{
		HistoryItem item = await this._PopScene();
		if (item.TaskCompletionSource != null) {
			item.TaskCompletionSource.SetException(exception);
		} else {
			GD.PushError("❌ Scene popped with an exception and previous scene was not waiting for a return value.");
		}
	}

	/// <summary>
	/// When popping a scene, a value can be passed back to the scene that pushed it into the stack. This return
	/// value should contain any resulting data that have been generated by the scene that is being popped.
	/// </summary>
	public async Task PopScene(Variant returnValue = new Variant())
	{
		HistoryItem item = await this._PopScene();
		item.TaskCompletionSource?.SetResult(returnValue);
	}

	public async Task<HistoryItem> _PopScene()
	{
		HistoryItem item = this._sceneStack.Pop();

		if (this._sceneStack.Count == 0) {
			this.Quit();
			return item;
		}

		try {
			await this.ReturnToPreviousScene(item);
		} catch {
			this._sceneStack.Push(item);
			throw;
		}

		{
			// Wait next frame
			TaskCompletionSource source = new();
			Callable.From(source.SetResult).CallDeferred();
			await source.Task;
		}

		return item;
	}

	public void ResetScene() => this.ReplaceScene(this._sceneStack.Peek().SceneName, this._sceneStack.Peek().Options.Args);

	public async void Quit()
	{
		try {
			await this.ExitCurrentScene(SceneExitStrategyEnum.Delete);
		} catch {}
		this.GetTree().Quit();
	}
}
