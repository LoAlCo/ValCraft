using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValCraft
{
    // Minecraft moves the player: Valheim's own motion (walking, gravity, swimming) is skipped and
    // its body is put where Minecraft's physics says, with no momentum or fall damage of its own.
    [HarmonyPatch(typeof(Character), "UpdateMotion")]
    static class MotionPatch
    {
        static readonly int ForwardSpeed = ZSyncAnimation.GetHash("forward_speed");
        static readonly int SidewaySpeed = ZSyncAnimation.GetHash("sideway_speed");
        static readonly int TurnSpeed = ZSyncAnimation.GetHash("turn_speed");
        static readonly int InWater = ZSyncAnimation.GetHash("inWater");
        static readonly int OnGround = ZSyncAnimation.GetHash("onGround");
        static readonly int Falling = ZSyncAnimation.GetHash("falling");
        static readonly MethodInfo SetCrouch = AccessTools.Method(typeof(Player), "SetCrouch");
        static Vector3 _smoothVel;
        static bool _wasOnGround = true;
        static bool _wasCrouching;

        static bool Prefix(Character __instance, float dt, Rigidbody ___m_body, ref float ___m_maxAirAltitude, ref float ___m_lastGroundTouch,
            ref float ___m_fallTimer, ref Vector3 ___m_currentVel, ZSyncAnimation ___m_zanim)
        {
            if (!(Puppet.Puppeting || Puppet.Parked) || __instance != Player.m_localPlayer || !___m_body) return true;
            var target = Puppet.Parked ? Puppet.ParkPos : Puppet.FeetPos;
            var prev = ___m_body.position;
            // Kinematic while Minecraft drives: still solid to creatures and other players (it pushes
            // them), but physics can't shove it. A dynamic body put down inside terrain (Minecraft
            // moving fast past collision it hasn't got yet) was thrown out of it, and that throw read
            // as Valheim teleporting the player: Minecraft followed and fell to its death.
            if (!___m_body.isKinematic)
            {
                ___m_body.linearVelocity = Vector3.zero;
                ___m_body.angularVelocity = Vector3.zero;
                ___m_body.isKinematic = true;
                Puppet.BodyKinematic = true;
            }
            ___m_body.useGravity = false;
            ___m_body.position = target;
            Puppet.LastSetPos = target;
            Puppet.HaveLastSet = true;
            ___m_currentVel = dt > 0f ? (target - prev) / dt : Vector3.zero;  // what Valheim's animation reads
            ___m_maxAirAltitude = target.y;
            ___m_fallTimer = 0f;
            if (Puppet.Parked || (Puppet.Mc.flags & Link.Proto.McOnGround) != 0) ___m_lastGroundTouch = 0f;
            // The body faces where Minecraft looks.
            ___m_body.rotation = Quaternion.Euler(0f, Coords.McYawToUnity(Puppet.Yaw), 0f);
            Animate(__instance, ___m_zanim, ___m_currentVel, dt);
            return false;
        }

        // What Valheim's own walking/swimming code would tell the animator (synced to other players by
        // ZSyncAnimation): they see the Viking walk, run, swim, crouch and jump the way we move.
        static void Animate(Character c, ZSyncAnimation zanim, Vector3 vel, float dt)
        {
            if (!zanim) return;
            _smoothVel = Vector3.Lerp(_smoothVel, vel, 1f - Mathf.Exp(-dt / 0.08f));
            var t = c.transform;
            uint flags = Puppet.Mc.flags;
            bool onGround = (flags & Link.Proto.McOnGround) != 0;
            bool swimming = (flags & Link.Proto.McSwimming) != 0 || c.IsSwimming();
            var flat = new Vector3(_smoothVel.x, 0f, _smoothVel.z);
            zanim.SetFloat(ForwardSpeed, Vector3.Dot(flat, t.forward));
            zanim.SetFloat(SidewaySpeed, Vector3.Dot(flat, t.right));
            zanim.SetFloat(TurnSpeed, 0f);
            zanim.SetBool(InWater, swimming);
            zanim.SetBool(OnGround, onGround && !swimming);
            zanim.SetBool(Falling, !onGround && !swimming && _smoothVel.y < -2f);
            if (_wasOnGround && !onGround && !swimming && vel.y > 2f) zanim.SetTrigger("jump");
            _wasOnGround = onGround;
            // Minecraft's sneak is Valheim's: crouched for everyone, and stealthier to creatures.
            bool crouch = (flags & Link.Proto.McSneaking) != 0 && !swimming;
            if (crouch != _wasCrouching && c is Player player && SetCrouch != null)
            {
                SetCrouch.Invoke(player, new object[] { crouch });
                _wasCrouching = crouch;
            }
        }
    }

    // Also place the transform every rendered frame (FixedUpdate runs at 50 Hz).
    [HarmonyPatch(typeof(Player), "LateUpdate")]
    static class PlayerLateUpdatePatch
    {
        static void Postfix(Player __instance)
        {
            if (!Puppet.Puppeting || __instance != Player.m_localPlayer) return;
            __instance.transform.position = Puppet.FeetPos;
            Puppet.LastSetPos = Puppet.FeetPos;  // placed here too: the teleport check compares against the latest
        }
    }

    // Valheim's own player controls get nothing while Minecraft drives the player.
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    static class ControlsPatch
    {
        static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold, ref bool secondaryAttack,
            ref bool secondaryAttackHold, ref bool block, ref bool blockHold, ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
        {
            if (!Puppet.MinecraftOwnsPlayer || __instance != Player.m_localPlayer) return;
            movedir = Vector3.zero;
            attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = false;
            jump = crouch = run = autoRun = dodge = false;
        }
    }

    // Number keys are Minecraft's hotbar.
    [HarmonyPatch(typeof(Player), nameof(Player.UseHotbarItem))]
    static class HotbarPatch
    {
        static bool Prefix(Player __instance) => !(Puppet.MinecraftOwnsPlayer && __instance == Player.m_localPlayer);
    }

    // The camera is Minecraft's: its eye (sneak height, walk bob, F5 modes), look and FOV.
    [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
    static class CameraPatch
    {
        static void Postfix(GameCamera __instance)
        {
            if (!Puppet.EyeValid || !Player.m_localPlayer || GameCamera.InFreeFly()) return;
            __instance.transform.position = Puppet.EyePos;
            __instance.transform.rotation = Puppet.EyeRot;
            var cam = __instance.GetComponent<Camera>();
            if (cam)
            {
                cam.nearClipPlane = 0.05f;
                cam.fieldOfView = Puppet.FovDeg;  // both vertical
            }
        }
    }

    // Valheim's key hints (attack, block, dodge) are for its own controls: hidden while Minecraft drives.
    [HarmonyPatch(typeof(KeyHints), "UpdateHints")]
    static class KeyHintsPatch
    {
        static void Postfix(KeyHints __instance)
        {
            if (!Puppet.MinecraftOwnsPlayer || Plugin.Paused) return;
            if (__instance.m_combatHints) __instance.m_combatHints.SetActive(false);
            if (__instance.m_buildHints) __instance.m_buildHints.SetActive(false);
            if (__instance.m_fishingHints) __instance.m_fishingHints.SetActive(false);
        }
    }

    // A Minecraft screen (inventory, chest, chat) uses the real cursor.
    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    static class MouseCapturePatch
    {
        static void Postfix()
        {
            if (!Puppet.MinecraftOwnsPlayer || !Puppet.McScreenOpen) return;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
        }
    }

    // ZInput: Valheim reads buttons, keys and the mouse through these. While Minecraft drives the
    // player they report nothing, except Valheim's menu keys (Esc, M) when no Minecraft screen is open.
    [HarmonyPatch]
    static class ZInputBlock
    {
        static bool Blocked => InputBridge.BlockValheim;

        // Holding a Minecraft hoe or the Build Hammer: Valheim's build buttons reach its build mode
        // (see BuildTools): place (left click), menu (right click), and for the hammer remove
        // (middle click) and its Shift variants. Alt + mouse wheel rotates (the wheel alone stays
        // Minecraft's hotbar).
        static bool HoeButton(string name) => BuildTools.Active &&
            (name == "Attack" || name == "BuildMenu" || (BuildTools.Hammer && (name == "Remove" || name == "AltPlace")));

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton)), HarmonyPrefix]
        static bool GetButton(string name, ref bool __result) { if (!Blocked || HoeButton(name)) return true; __result = false; return false; }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown)), HarmonyPrefix]
        static bool GetButtonDown(string name, ref bool __result)
        {
            if (!Blocked || (name == "Map" && InputBridge.AllowValheimMenuKeys) || HoeButton(name)) return true;
            __result = false;
            return false;
        }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp)), HarmonyPrefix]
        static bool GetButtonUp(string name, ref bool __result) { if (!Blocked || HoeButton(name)) return true; __result = false; return false; }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKey)), HarmonyPrefix]
        static bool GetKey(ref bool __result) { if (!Blocked) return true; __result = false; return false; }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyDown)), HarmonyPrefix]
        static bool GetKeyDown(KeyCode key, ref bool __result)
        {
            if (!Blocked || (key == KeyCode.Escape && InputBridge.AllowValheimMenuKeys)) return true;
            __result = false;
            return false;
        }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyUp)), HarmonyPrefix]
        static bool GetKeyUp(ref bool __result) { if (!Blocked) return true; __result = false; return false; }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseButton)), HarmonyPrefix]
        static bool GetMouseButton(ref bool __result) { if (!Blocked) return true; __result = false; return false; }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseButtonDown)), HarmonyPrefix]
        static bool GetMouseButtonDown(ref bool __result) { if (!Blocked) return true; __result = false; return false; }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseButtonUp)), HarmonyPrefix]
        static bool GetMouseButtonUp(ref bool __result) { if (!Blocked) return true; __result = false; return false; }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseDelta)), HarmonyPrefix]
        static bool GetMouseDelta(ref Vector2 __result) { if (!Blocked) return true; __result = Vector2.zero; return false; }

        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel)), HarmonyPrefix]
        static bool GetMouseScrollWheel(ref float __result) { if (!Blocked || InputBridge.BuildRotate) return true; __result = 0f; return false; }
    }

    // Valheim hitting the player: Minecraft takes the hit instead (armour, shields, knockback, death).
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    static class PlayerDamagePatch
    {
        static bool Prefix(Character __instance, HitData hit)
        {
            if (__instance != Player.m_localPlayer || hit == null) return true;
            return !Combat.ForwardPlayerDamage(Player.m_localPlayer, hit);
        }
    }

    // A door opening or closing (anyone's): Minecraft's copy of the collision around it is re-sent
    // straight away and again while it swings, or the closed door would stay a wall.
    [HarmonyPatch(typeof(Door), "SetState")]
    static class DoorPatch
    {
        static void Prefix(Door __instance, int state, Animator ___m_animator)
        {
            if (!___m_animator || ___m_animator.GetInteger("state") == state) return;
            Collision.DoorMoved(__instance);
        }
    }

    // Valheim items with a Minecraft counterpart go to the Minecraft inventory (Loot).
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
    static class PickupPatch
    {
        static bool Prefix(Humanoid __instance, GameObject go, bool autoPickupDelay, ref bool __result)
        {
            if (__instance != Player.m_localPlayer || !go) return true;
            var drop = go.GetComponent<ItemDrop>();
            if (drop) drop.Load();
            if (!Loot.TryTake(Player.m_localPlayer, drop, autoPickupDelay)) return true;
            __result = true;
            return false;
        }
    }

    // Valheim's hover prompts name its Use key (E, which is Minecraft's inventory): show G instead.
    [HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
    static class HoverPromptPatch
    {
        static string Key => InputBridge.UseKey.ToString();

        static void Postfix(Hud __instance)
        {
            if (!Puppet.MinecraftOwnsPlayer || !__instance.m_hoverName) return;
            string text = __instance.m_hoverName.text;
            if (string.IsNullOrEmpty(text)) return;
            string use = ZInput.instance != null ? ZInput.instance.GetBoundKeyString("Use") : "E";
            if (string.IsNullOrEmpty(use) || use == Key) return;
            string fixedText = text.Replace("<b>" + use + "</b>", "<b>" + Key + "</b>").Replace("+ " + use + "</b>", "+ " + Key + "</b>")
                .Replace(" " + use + "</b>", " " + Key + "</b>");
            if (fixedText != text) __instance.m_hoverName.text = fixedText;
        }
    }

    // Hide Valheim's crosshair, hotbar and health/stamina/eitr/food bars while Minecraft's HUD
    // (hearts, hunger, hotbar) is up. Every frame: Valheim's HUD turns some back on as it updates.
    [HarmonyPatch(typeof(Hud), "Update")]
    static class HudPatch
    {
        static void Postfix(Hud __instance)
        {
            bool mc = Puppet.MinecraftOwnsPlayer;
            if (__instance.m_crosshair) __instance.m_crosshair.enabled = !mc;
            if (__instance.m_crosshairBow) __instance.m_crosshairBow.enabled = !mc && __instance.m_crosshairBow.enabled;
            if (mc || _barsHidden)
            {
                foreach (var bar in new Component[] { __instance.m_healthPanel, __instance.m_staminaBar2Root, __instance.m_eitrBarRoot,
                             __instance.m_adrenalineBarRoot, __instance.m_foodBarRoot })
                    if (bar && bar.gameObject.activeSelf == mc) bar.gameObject.SetActive(!mc);
                _barsHidden = mc;
            }
            if (mc != _hidden)
            {
                _hidden = mc;
                if (mc) _bars = Object.FindObjectsByType<HotkeyBar>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (_bars != null) foreach (var bar in _bars) if (bar) bar.gameObject.SetActive(!mc);
            }
        }

        static bool _hidden, _barsHidden;
        static HotkeyBar[] _bars;
    }
}
