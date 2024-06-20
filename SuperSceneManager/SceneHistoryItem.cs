using Godot;

namespace Raele.SuperSceneManager;

public record SceneHistoryItem
{
	public string sceneName = "";
	public Variant[]? args;
}
