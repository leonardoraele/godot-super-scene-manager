#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using static Godot.ResourceLoader;

namespace Raele.SuperSceneManager;

public partial class AsyncTaskHelper : Node
{
	// -----------------------------------------------------------------------------------------------------------------
	// STATICS
	// -----------------------------------------------------------------------------------------------------------------

	// public static readonly string MyConstant = "";

	// -----------------------------------------------------------------------------------------------------------------
	// EXPORTS
	// -----------------------------------------------------------------------------------------------------------------

	// [Export] public

	// -----------------------------------------------------------------------------------------------------------------
	// FIELDS
	// -----------------------------------------------------------------------------------------------------------------

	private Dictionary<string, TaskCompletionSource<Resource>> OngoingResourceLoads = new();
    private Dictionary<Node, TaskCompletionSource> OngoingNodeFreeings = new();

    // -----------------------------------------------------------------------------------------------------------------
    // PROPERTIES
    // -----------------------------------------------------------------------------------------------------------------



    // -----------------------------------------------------------------------------------------------------------------
    // SIGNALS
    // -----------------------------------------------------------------------------------------------------------------

    [Signal] public delegate void LoadProgressEventHandler(string path, int progress);
	[Signal] public delegate void LoadCompleteEventHandler(string path, Resource resource);
	[Signal] public delegate void LoadFailedEventHandler(string path, string errorMessage);

	// -----------------------------------------------------------------------------------------------------------------
	// INTERNAL TYPES
	// -----------------------------------------------------------------------------------------------------------------

	// private enum Type {
	// 	Value1,
	// }

	// -----------------------------------------------------------------------------------------------------------------
	// EVENTS
	// -----------------------------------------------------------------------------------------------------------------

	// public override void _EnterTree()
	// {
	// 	base._EnterTree();
	// }

	// public override void _Ready()
	// {
	// 	base._Ready();
	// }

	public override void _Process(double delta)
	{
		Godot.Collections.Array progressArray = new();
		foreach (string key in this.OngoingResourceLoads.Keys) {
            ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(key, progressArray);
			switch (status) {
				case ResourceLoader.ThreadLoadStatus.InProgress:
					this.EmitSignal(SignalName.LoadProgress, key, progressArray[0].AsInt32());
					break;
				case ResourceLoader.ThreadLoadStatus.InvalidResource:
				case ResourceLoader.ThreadLoadStatus.Failed:
					string errorMessage = "Failed to load resource: " + status.ToString();
					this.EmitSignal(SignalName.LoadFailed, key, errorMessage);
					this.OngoingResourceLoads[key].SetException(new System.Exception(errorMessage));
					this.OngoingResourceLoads.Remove(key);
					break;
				case ResourceLoader.ThreadLoadStatus.Loaded:
                    Resource resource = ResourceLoader.LoadThreadedGet(key);
					this.EmitSignal(SignalName.LoadComplete, key, resource);
					this.OngoingResourceLoads[key].SetResult(resource);
					this.OngoingResourceLoads.Remove(key);
					break;
			}
		}
		foreach (Node node in this.OngoingNodeFreeings.Keys) {
			if (!GodotObject.IsInstanceValid(node)) {
				this.OngoingNodeFreeings[node].SetResult();
				this.OngoingNodeFreeings.Remove(node);
			}
		}
	}

	// public override void _PhysicsProcess(double delta)
	// {
	// 	base._PhysicsProcess(delta);
	// }

	// public override string[] _GetConfigurationWarnings()
	// 	=> base._PhysicsProcess(delta);

	// -----------------------------------------------------------------------------------------------------------------
	// METHODS
	// -----------------------------------------------------------------------------------------------------------------

	public Task<R> LoadAsync<R>(string path, bool useSubThreads = false, CacheMode cacheMode = CacheMode.Reuse) where R : Resource
	{
        string typeHint = typeof(R).ToString();
		ResourceLoader.LoadThreadedRequest(path, typeHint, useSubThreads, cacheMode);
		TaskCompletionSource<R> source = new();
		this.OngoingResourceLoads[path] = source as TaskCompletionSource<Resource>; // TODO // FIXME
		return source.Task;
	}

	public Task FreeAsync(Node node)
	{
		node.QueueFree();
		TaskCompletionSource source = new();
		this.OngoingNodeFreeings[node] = source;
		return source.Task;
	}
}
