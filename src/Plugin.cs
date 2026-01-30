#if TOOLS
using Godot;
using Raele.GodotUtils.Extensions;

namespace Raele.SuperSceneManager;

[Tool]
public partial class Plugin : EditorPlugin
{
	public override void _EnterTree()
	{
		base._EnterTree();
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

		this.AddAutoloadSingleton(nameof(SceneManager), $"res://addons/{nameof(SuperSceneManager)}/src/{nameof(SceneManager)}.cs");
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		this.RemoveAutoloadSingleton(nameof(SceneManager));
	}
}
#endif
