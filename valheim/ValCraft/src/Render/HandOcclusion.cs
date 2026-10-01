using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValCraft.Render
{
    // Valheim's ambient occlusion (Amplify, "deferred injection") darkens the G-buffer's occlusion and
    // ambient light after the opaque objects are drawn. A few centimetres from the eye the first-person
    // hands come out nearly black. Right after it runs, the hands' G-buffer pass is drawn again over
    // their own pixels (same depth), which puts back their unoccluded values; everything else about
    // them (sun, shadows, lightning, fog) stays the world's.
    public class HandOcclusion : MonoBehaviour
    {
        static readonly RenderTargetIdentifier[] GBuffer =
        {
            BuiltinRenderTextureType.GBuffer0, BuiltinRenderTextureType.GBuffer1, BuiltinRenderTextureType.GBuffer2, BuiltinRenderTextureType.CameraTarget,
        };

        // False when the occlusion isn't applied in the G-buffer (its post-effect mode): the hands are
        // then drawn after the opaque pass instead (SceneRender).
        public static bool Usable = true;

        Camera _camera;
        CommandBuffer _cmd;
        CameraEvent _event;
        bool _added;
        MonoBehaviour _ao;
        FieldInfo _applyMethod;
        static string _logged;

        void OnEnable()
        {
            _camera = GetComponent<Camera>();
            _cmd = new CommandBuffer { name = "ValCraft hands without ambient occlusion" };
            foreach (var mb in GetComponents<MonoBehaviour>())
                if (mb && mb.GetType().Name == "AmplifyOcclusionEffect") { _ao = mb; _applyMethod = mb.GetType().GetField("ApplyMethod"); }
        }

        void OnDisable() => Remove();

        void Remove()
        {
            if (_added && _camera) _camera.RemoveCommandBuffer(_event, _cmd);
            _added = false;
        }

        // After Amplify's OnPreRender (this component is added later), which may re-add its buffers:
        // re-adding ours each frame keeps it after them on the same camera event.
        void OnPreRender()
        {
            Remove();
            _cmd.Clear();
            var r = SceneRender.ViewModelRenderer;
            if (!_camera || !_ao || !_ao.enabled || r == null) return;
            // Valheim applies it as a post effect (over the finished picture, hands and all); injected
            // in the G-buffer instead, it can be taken back off the hands.
            if (_applyMethod != null && _applyMethod.GetValue(_ao).ToString() == "PostEffect" && _camera.actualRenderingPath == RenderingPath.DeferredShading)
                _applyMethod.SetValue(_ao, System.Enum.Parse(_applyMethod.FieldType, "Deferred"));
            string method = _applyMethod != null ? _applyMethod.GetValue(_ao).ToString() : "?";
            bool usable = method == "Deferred" && _camera.actualRenderingPath == RenderingPath.DeferredShading;
            if (_logged != method) { _logged = method; Plugin.Log($"hands: ambient occlusion method {method}, {(usable ? "taken off the hands in the G-buffer" : "hands drawn after the opaque pass")}"); }
            if (usable != Usable) { Usable = usable; SceneRender.RefreshViewModel(); }
            if (!usable) return;
            _event = GraphicsSettings.GetShaderMode(BuiltinShaderType.DeferredReflections) != BuiltinShaderMode.Disabled ? CameraEvent.BeforeReflections : CameraEvent.BeforeLighting;

            _cmd.SetRenderTarget(GBuffer, BuiltinRenderTextureType.CameraTarget);
            // UNITY_HDR_ON is Unity's own keyword during the deferred passes: left as it is (switching it
            // off afterwards broke the lighting pass for the whole frame).
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!m || m.renderQueue > 2500) continue;  // translucent parts aren't in the G-buffer
                int pass = m.FindPass("DEFERRED");
                if (pass >= 0) _cmd.DrawRenderer(r, m, i, pass);
            }
            _camera.AddCommandBuffer(_event, _cmd);
            _added = true;
        }
    }
}
