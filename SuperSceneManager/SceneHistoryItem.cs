using Godot;

namespace Raele.SuperSceneManager;

public record SceneHistoryItem
{
	public string SceneName = "";
	public Variant[] Args = [];
}
