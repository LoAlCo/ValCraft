using UnityEngine;
using ValCraft.Link;

namespace ValCraft
{
    // In a Minecraft boat the Viking sits, as on one of Valheim's chairs (its "attach_chair" pose),
    // so other players see it seated in the boat instead of standing in the water. Only the pose:
    // a real Valheim attach would hand the player to Valheim (Puppet.ValheimTakeover).
    public static class BoatSeat
    {
        const string Pose = "attach_chair";
        static bool _sitting;
        static bool? _havePose;
        static Player _for;

        // Main thread, every frame.
        public static void Frame()
        {
            var player = Player.m_localPlayer;
            bool want = player && Puppet.Puppeting && Puppet.McConnected && (Puppet.Mc.flags & Proto.McInBoat) != 0 && !player.IsAttached();
            if (player != _for) { _for = player; _sitting = false; _havePose = null; }
            if (want == _sitting || !player) return;
            var zanim = player.GetComponent<ZSyncAnimation>();
            if (!zanim) return;
            if (_havePose == null)
            {
                _havePose = false;
                var animator = player.GetComponentInChildren<Animator>();
                if (animator)
                    foreach (var p in animator.parameters)
                        if (p.name == Pose && p.type == AnimatorControllerParameterType.Bool) _havePose = true;
                if (_havePose == false) Plugin.Warn($"boat: the Viking has no {Pose} pose; it won't sit in Minecraft boats");
            }
            if (_havePose != true) return;
            zanim.SetBool(Pose, want);
            _sitting = want;
            Plugin.Log(want ? "boat: in a Minecraft boat, the Viking sits" : "boat: out of the boat");
        }
    }
}
