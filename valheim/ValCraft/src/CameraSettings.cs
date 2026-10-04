using BepInEx.Configuration;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // [Camera] FOV is Minecraft's FOV option, kept in step both ways: changed here it's set in
    // Minecraft (ValState.fovSetting / fovSeq); changed in Minecraft's options (O) this follows
    // (McState.optionsFov, once fovAck shows Minecraft has our latest). The first-person hands are
    // drawn in Valheim's scene, so they'd widen and shrink with the FOV: they're scaled to look as
    // they would at ViewmodelFOV instead, which follows FOV while LockViewmodelFOV is on.
    public static class CameraSettings
    {
        public static ConfigEntry<int> Fov, ViewmodelFov;
        public static ConfigEntry<bool> LockViewmodel;
        static uint _seq;              // 0: on linking, the config takes Minecraft's FOV; bumped on each change here
        static bool _adopting;

        public static void Init(ConfigFile config)
        {
            Fov = config.Bind("Camera", "FOV", 90,
                new ConfigDescription("Field of view, the same setting as Minecraft's FOV option (changing either changes both).", new AcceptableValueRange<int>(30, 110)));
            ViewmodelFov = config.Bind("Camera", "ViewmodelFOV", 90,
                new ConfigDescription("Field of view the first-person hands and held item are drawn at (when LockViewmodelFOV is off). Lower: bigger and closer.", new AcceptableValueRange<int>(30, 110)));
            LockViewmodel = config.Bind("Camera", "LockViewmodelFOV", true,
                "The hands follow FOV: turning FOV up turns the viewmodel FOV up with it. Off: ViewmodelFOV on its own.");
            Fov.SettingChanged += (s, e) => { if (!_adopting) _seq++; };
        }

        public static void Write(ref ValState st)
        {
            st.fovSetting = Fov != null ? Fov.Value : 0f;
            st.fovSeq = _seq;
        }

        // The player changed the FOV in Minecraft's own options: the config follows.
        public static void Read(in McState mc)
        {
            if (Fov == null || mc.fovAck != _seq || mc.optionsFov < 1f) return;
            int fov = Mathf.RoundToInt(mc.optionsFov);
            if (fov == Fov.Value) return;
            _adopting = true;
            try { Fov.Value = fov; } finally { _adopting = false; }
            Plugin.Log($"camera: FOV {fov} from Minecraft's options");
        }

        // How much to widen the hands (view-space x and y) so they look as at the viewmodel FOV
        // while the scene is drawn at sceneFov.
        public static float ViewmodelScale(float sceneFov)
        {
            if (Fov == null) return 1f;
            float vm = LockViewmodel.Value ? Fov.Value : ViewmodelFov.Value;
            return Mathf.Tan(sceneFov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(vm * 0.5f * Mathf.Deg2Rad);
        }
    }
}
