#nullable enable
#if TOOLS
using Godot;

namespace Raele.SuperSceneManager;

[Tool]
public partial class SuperSceneManagerPlugin : EditorPlugin
{
    public override void _EnterTree()
    {
        this.AddAutoloadSingleton(nameof(SuperSceneManager), $"res://addons/{nameof(SuperSceneManager)}/{nameof(SuperSceneManager)}/{nameof(SuperSceneManager)}.cs");

        ProjectSettings.AddPropertyInfo(new() {
            { "name", SuperSceneManager.SETTING_TRANSITION_SCENE },
            { "type", (long) Variant.Type.String },
            { "hint", (long) PropertyHint.File },
            { "hint_string", "*.tscn" },
        });

        if (!ProjectSettings.HasSetting(SuperSceneManager.SETTING_TRANSITION_SCENE)) {
            ProjectSettings.SetSetting(SuperSceneManager.SETTING_TRANSITION_SCENE, "");
        }

        // ProjectSettings.AddPropertyInfo(new() {
        //     { "name", SuperSceneManager.SETTING_EXITING_SCENE_BEHAVIOR },
        //     { "type", (long) Variant.Type.Int },
        //     { "hint", (long) PropertyHint.Enum },
        //     { "hint_string", "Free,DisableThenFree,DisableImmediately,DisableLater,Keep" },
        // });

        ProjectSettings.AddPropertyInfo(new() {
            { "name", SuperSceneManager.SETTING_SCENE_LIST },
            { "type", (long) Variant.Type.Dictionary },
            // { "hint", (long) PropertyHint.None },
            // { "hint_string", "string,string" },
        });

        if (!ProjectSettings.HasSetting(SuperSceneManager.SETTING_SCENE_LIST)) {
            Godot.Collections.Dictionary sceneList = new();
            if (ProjectSettings.HasSetting("application/run/main_scene")) {
                sceneList["main"] = ProjectSettings.GetSetting("application/run/main_scene").AsString();
            }
            ProjectSettings.SetSetting(SuperSceneManager.SETTING_SCENE_LIST, sceneList);
        }

        ProjectSettings.Save();
    }
}
#endif
