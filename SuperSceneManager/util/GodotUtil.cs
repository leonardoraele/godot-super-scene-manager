using Godot;

namespace Raele.SuperSceneManager;

public static class GodotUtil
{
	public static bool TryGetSetting(string settingName, out Variant value)
	{
		if (!ProjectSettings.HasSetting(settingName)) {
			value = new Variant();
			return false;
		} else {
			value = ProjectSettings.GetSetting(settingName);
			return true;
		}
	}

	public static bool TryGetSetting<[MustBeVariant] T>(string settingName, out T value)
	{
		if (!ProjectSettings.HasSetting(settingName)) {
			value = new Variant().As<T>();
			return false;
		} else {
			value = ProjectSettings.GetSetting(settingName).As<T>();
			return true;
		}
	}
}
