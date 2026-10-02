using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // ValCraft: a passthrough mod. Minecraft (with the ValCraft Fabric mod) runs next to Valheim;
    // Minecraft is the player (movement, inventory, HUD, blocks), Valheim is the world.
    [BepInPlugin(Guid, "ValCraft", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "loalco.valcraft";
        public const string Version = "0.5.4";

        public static Plugin Instance;
        public static bool Paused;  // F7: Valheim has its own controls back
        public static ConfigEntry<bool> Diagnostics;

        void Awake()
        {
            Instance = this;
            Diagnostics = Config.Bind("Debug", "Diagnostics", false, "Log link, collision and frame stats every few seconds.");
            try
            {
                Shm.CheckLayout();
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message + " - ValCraft disabled");
                return;
            }
            if (!Shm.Create())
            {
                Logger.LogError("ValCraft disabled: could not create shared memory");
                return;
            }
            InputBridge.Init(Config);
            Combat.Init(Config);
            Launcher.Init(Config);
            BlockTerrain.Init(Config);
            Loot.Load();
            Collision.Start();
            new Harmony(Guid).PatchAll();
            gameObject.AddComponent<Runner>();
            gameObject.AddComponent<PhaseEarly>();
            gameObject.AddComponent<PhaseLate>();
            // As early as possible: Minecraft takes about as long to start as Valheim does to reach its menu.
            Launcher.StartMinecraft();
            Logger.LogInfo($"ValCraft {Version} loaded. Start Minecraft with the ValCraft Fabric mod; it connects on its own.");
        }

        public static void Log(string msg) => Instance.Logger.LogInfo(msg);
        public static void Warn(string msg) => Instance.Logger.LogWarning(msg);
        public static void Error(string msg) => Instance.Logger.LogError(msg);

        public static void Message(string msg)
        {
            if (MessageHud.instance) MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, msg);
            Log(msg);
        }
    }

    class Runner : MonoBehaviour
    {
        float _statsTimer = 5f;

        void Update()
        {
            try
            {
                long t = Prof.Start(); InputBridge.Frame(); Prof.Stop("input", t);
                t = Prof.Start(); Puppet.Frame(Time.unscaledDeltaTime); Prof.Stop("puppet", t);
                Underwater.Frame();
                BuildTools.Frame();
                t = Prof.Start(); WorldRender.Frame(Time.unscaledDeltaTime); Prof.Stop("render", t);
                t = Prof.Start(); Combat.Frame(Time.unscaledDeltaTime); Prof.Stop("combat", t);
                t = Prof.Start(); Overlay.Upload(); Prof.Stop("overlay", t);
                Report();
            }
            catch (Exception e)
            {
                Plugin.Error("frame: " + e);
            }
        }

        void OnGUI()
        {
            GUI.depth = -1000;
            Overlay.Draw();
        }

        void Report()
        {
            Launcher.Report(Puppet.McConnected, Player.m_localPlayer, Time.unscaledDeltaTime);
            if (!Plugin.Diagnostics.Value) return;
            _statsTimer -= Time.unscaledDeltaTime;
            if (_statsTimer > 0f) return;
            _statsTimer = 5f;
            var m = Puppet.Mc;
            Plugin.Log($"stats: mc={(Puppet.McConnected ? "up" : "down")} inWorld={Puppet.McInWorld} puppet={Puppet.Puppeting} owns={Puppet.MinecraftOwnsPlayer} " +
                       $"screen={Puppet.McScreenOpen} vmenu={Puppet.ValheimMenuOpen} mcPos=({m.x:F1} {m.y:F1} {m.z:F1}) ack={m.teleportAck} " +
                       $"regions={Collision.StatRegions} tris={Collision.StatTris} boxes={Collision.StatBoxes} unreadable={Collision.StatUnreadable} overlayFrames={Overlay.Frames}");
        }
    }
}
