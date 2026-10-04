using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ValCraft
{
    // For drawing ValCraft's Minecraft versions of Valheim weapons from their 3D models (the icons are
    // small and at odd angles): renders each item's model on its own, laid on the diagonal like a
    // Minecraft sword (grip bottom left, tip top right, flat side to the camera), and saves it as a
    // PNG with a transparent background in BepInEx/ValCraft renders. [Debug] ExportRenders, which
    // switches itself back off.
    public static class ModelRender
    {
        const int Size = 512;

        static ConfigEntry<bool> _export;
        static ConfigEntry<string> _list;

        static readonly HashSet<ItemDrop.ItemData.ItemType> WeaponTypes = new HashSet<ItemDrop.ItemData.ItemType>
        {
            ItemDrop.ItemData.ItemType.OneHandedWeapon, ItemDrop.ItemData.ItemType.TwoHandedWeapon,
            ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft, ItemDrop.ItemData.ItemType.Bow, ItemDrop.ItemData.ItemType.Tool,
            ItemDrop.ItemData.ItemType.Shield, ItemDrop.ItemData.ItemType.Torch,
        };

        public static void Init(ConfigFile config)
        {
            _export = config.Bind("Debug", "ExportRenders", false,
                "Render the 3D models of the items in ExportRendersList as PNGs in BepInEx/ValCraft renders, laid on the diagonal like a Minecraft sword (for drawing ValCraft's Minecraft versions of them). Switches itself off when done.");
            _list = config.Bind("Debug", "ExportRendersList", "weapons", "Item prefab names to render, comma-separated; weapons for every weapon, tool and shield.");
        }

        public static void Frame()
        {
            if (_export == null || !_export.Value || !ObjectDB.instance || ObjectDB.instance.m_items.Count == 0 || !ZNetScene.instance) return;
            _export.Value = false;
            try { Export(); } catch (Exception e) { Plugin.Warn($"render export: {e}"); }
        }

        static int FreeLayer()
        {
            for (int l = 31; l > 8; l--) if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) return l;
            return 31;
        }

        static void Export()
        {
            string dir = Path.Combine(Paths.BepInExRootPath, "ValCraft renders");
            Directory.CreateDirectory(dir);
            bool weapons = _list.Value.Trim().Equals("weapons", StringComparison.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in _list.Value.Split(',')) if (n.Trim().Length > 0) wanted.Add(n.Trim());

            int layer = FreeLayer();
            var stage = new Vector3(0f, -3000f, 0f);
            var camGo = new GameObject("ValCraft render camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.cullingMask = 1 << layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.renderingPath = RenderingPath.Forward;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            var lightGo = new GameObject("ValCraft render light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << layer;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var fill = new GameObject("ValCraft render fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.cullingMask = 1 << layer;
            fill.intensity = 0.5f;
            fill.transform.rotation = Quaternion.Euler(-20f, 150f, 0f);
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            var black = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var white = new Texture2D(Size, Size, TextureFormat.RGBA32, false);

            int saved = 0;
            foreach (var prefab in ObjectDB.instance.m_items)
            {
                var drop = prefab ? prefab.GetComponent<ItemDrop>() : null;
                if (!drop) continue;
                if (weapons ? !WeaponTypes.Contains(drop.m_itemData.m_shared.m_itemType) : !wanted.Contains(prefab.name)) continue;
                GameObject go = null;
                try
                {
                    ZNetView.m_forceDisableInit = true;
                    go = UnityEngine.Object.Instantiate(prefab, stage, Quaternion.identity);
                    ZNetView.m_forceDisableInit = false;
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                    var renderers = new List<Renderer>();
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                        if ((r is MeshRenderer || r is SkinnedMeshRenderer) && r.enabled && r.gameObject.activeInHierarchy) renderers.Add(r);
                    if (renderers.Count == 0) continue;
                    foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

                    Orient(go, renderers, stage);
                    var b = Bounds(renderers);
                    float half = Mathf.Max(b.extents.x, b.extents.y) * 1.04f;
                    cam.orthographicSize = half;
                    cam.transform.position = b.center - Vector3.forward * 20f;
                    cam.transform.rotation = Quaternion.identity;

                    // Rendered on black and on white: the difference is the alpha, whatever the shaders write there.
                    Grab(cam, rt, Color.black, black);
                    Grab(cam, rt, Color.white, white);
                    File.WriteAllBytes(Path.Combine(dir, prefab.name + ".png"), Combine(black, white).EncodeToPNG());
                    saved++;
                }
                catch (Exception e) { Plugin.Warn($"render export: {prefab.name}: {e.Message}"); }
                finally
                {
                    ZNetView.m_forceDisableInit = false;
                    if (go) UnityEngine.Object.DestroyImmediate(go);
                }
            }
            UnityEngine.Object.Destroy(camGo);
            UnityEngine.Object.Destroy(lightGo);
            UnityEngine.Object.Destroy(fill.gameObject);
            rt.Release();
            Plugin.Message($"ValCraft: rendered {saved} item models to {dir}");
        }

        static Bounds Bounds(List<Renderer> renderers)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // Turns the model so its longest side runs from bottom left to top right (the end farther
        // from the item's origin, where Valheim holds it, at the top) and its thinnest side faces the camera.
        static void Orient(GameObject go, List<Renderer> renderers, Vector3 stage)
        {
            var b = Bounds(renderers);
            var ext = b.extents;
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var size = new[] { ext.x, ext.y, ext.z };
            int[] order = { 0, 1, 2 };
            Array.Sort(order, (i, j) => size[j].CompareTo(size[i]));
            Vector3 along = axes[order[0]], across = axes[order[1]];
            // the tip: the end of the long side farther from the origin
            if (Vector3.Dot(b.center - go.transform.position, along) < 0f) along = -along;
            var thin = Vector3.Cross(along, across);
            var toAlong = new Vector3(1f, 1f, 0f).normalized;
            var toAcross = new Vector3(-1f, 1f, 0f).normalized;
            var toThin = Vector3.Cross(toAlong, toAcross);
            var rot = Quaternion.LookRotation(toThin, toAcross) * Quaternion.Inverse(Quaternion.LookRotation(thin, across));
            go.transform.rotation = rot;
            var after = Bounds(renderers);
            go.transform.position += stage - after.center;
        }

        static void Grab(Camera cam, RenderTexture rt, Color background, Texture2D into)
        {
            cam.backgroundColor = background;
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            into.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            into.Apply();
            RenderTexture.active = previous;
        }

        static Texture2D Combine(Texture2D black, Texture2D white)
        {
            var b = black.GetPixels32();
            var w = white.GetPixels32();
            var o = new Color32[b.Length];
            for (int i = 0; i < b.Length; i++)
            {
                int diff = ((w[i].r - b[i].r) + (w[i].g - b[i].g) + (w[i].b - b[i].b)) / 3;
                int a = Mathf.Clamp(255 - diff, 0, 255);
                if (a < 8) { o[i] = new Color32(0, 0, 0, 0); continue; }
                float k = 255f / a;
                o[i] = new Color32((byte)Mathf.Clamp(b[i].r * k, 0, 255), (byte)Mathf.Clamp(b[i].g * k, 0, 255), (byte)Mathf.Clamp(b[i].b * k, 0, 255), (byte)a);
            }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels32(o);
            tex.Apply();
            return tex;
        }
    }
}
