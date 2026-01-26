#if TOOLS
using Godot;
using Raele.GodotUtils.Extensions;

namespace Raele.SuperSceneManager;

[Tool]
public partial class SuperSceneManagerPlugin : EditorPlugin
{
	public override void _EnterTree()
	{
		ProjectSettings.DefineSetting(new()
		{
			Name = Consts.SettingNames.SceneList,
			Type = Variant.Type.Dictionary,
			Hint = PropertyHint.DictionaryType,
			HintString = $"String,String/{PropertyHint.File:D}:*.tscn",
			DefaultValue = new Godot.Collections.Dictionary()
			{
				{ Consts.InitialSceneName, ProjectSettings.GetSetting("application/run/main_scene").AsString() }
			},
		});

		ProjectSettings.DefineSetting(new()
		{
			Name = Consts.SettingNames.TransitionScenePath,
			Type = Variant.Type.String,
			Hint = PropertyHint.File,
			HintString = "*.tscn",
			DefaultValue = "",
		});

		this.AddAutoloadSingleton(nameof(SceneManager), $"res://addons/{nameof(SceneManager)}/{nameof(SceneManager)}/{nameof(SceneManager)}.cs");
	}
}
#endif
