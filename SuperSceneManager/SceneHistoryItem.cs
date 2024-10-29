using System;
using System.Threading.Tasks;
using Godot;
using static Godot.Node;
using static Raele.SuperSceneManager.SuperSceneManager;

namespace Raele.SuperSceneManager;

public record SceneHistoryItem
{
	/// <summary>
	/// A unique identifier for this scene history item.
	/// </summary>
	public Guid id = Guid.NewGuid();
	/// <summary>
	/// The name of the scene to be loaded. This must be a key in the `scene_list` dictionary, configured in the addon's
	/// settings.
	/// </summary>
	public required string SceneName;
	/// <summary>
	/// The options to be used when changing scenes. This object is created by the user and passed to SuperSceneManager
	/// when changing scenes.
	/// </summary>
	public SceneChangeOptions Options = new();
	/// <summary>
	/// The instance of the previous scene. This is only used if the user selected an ExitMode other than Delete in the
	/// Options field.
	/// </summary>
	public Node? PreviousSceneInstance = null;
	/// <summary>
	/// If Options.ExitMode is set to `Disable` or `HideAndDisable`, this field keeps the node's process mode so that we
	/// set back to this value when returning to this scene. Otherwise, this value is not used.
	/// </summary>
	public ProcessModeEnum ProcessMode = ProcessModeEnum.Inherit;
	/// <summary>
	/// This field controls the asynchronous task that represents the lifetime of the scene represented by this history
	/// item. This is only used when a scene is pushed with an expected return using
	/// `SuperSceneManager.PushSceneWithReturn`.
	/// </summary>
	public TaskCompletionSource<Variant>? TaskCompletionSource = null;
}
