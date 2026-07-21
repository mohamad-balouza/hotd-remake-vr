using UnityEngine;

namespace HotdVR
{
    /// <summary>
    /// Procedural gun model attached to the aim hand. The game has no
    /// player-held weapon meshes in gameplay (weapons are logic-only; the
    /// armory's display models are menu-scene-embedded and not addressable),
    /// so the mod builds a simple primitive gun per weapon type and swaps it
    /// when the player's HD_WeaponHolder reports a different CurrentWeaponType.
    /// Barrel points along the aim ray (incl. the pitch tilt), so the laser
    /// continues exactly where the barrel points.
    /// </summary>
    [DefaultExecutionOrder(30003)] // after VRControllers computed the aim pose
    public class VRGunModel : MonoBehaviour
    {
        private GameObject root;
        private WeaponType builtType = (WeaponType)(-1);
        private WeaponType lastKnownType = WeaponType.Pistol;
        private HD_WeaponHolder holder;
        private float nextResolve;

        private Material gunmetal, grip, accent;

        private void LateUpdate()
        {
            if (!VRRuntimeBootstrap.Active || !Plugin.Cfg.ShowGunModel.Value || !VRControllers.AimValid)
            {
                if (root != null)
                    root.SetActive(false);
                return;
            }

            WeaponType type = ResolveWeaponType();
            if (root == null || type != builtType)
                Rebuild(type);
            root.SetActive(true);
            // Forward/back offset along the barrel axis, live-adjustable
            // (off-hand stick click + stick left/right, or Home/End).
            float z = float.IsNaN(VRControllers.LiveGunZ)
                ? Mathf.Clamp(Plugin.Cfg.GunModelZOffset.Value, -0.2f, 0.2f)
                : VRControllers.LiveGunZ;
            var rot = Quaternion.LookRotation(VRControllers.AimRay.direction, VRControllers.AimWorldRot * Vector3.up);
            root.transform.SetPositionAndRotation(
                VRControllers.AimRay.origin + rot * new Vector3(0f, 0f, z),
                rot);
        }

        private WeaponType ResolveWeaponType()
        {
            // Unity fake-null covers destroyed holders across scene loads.
            if (holder == null && Time.realtimeSinceStartup >= nextResolve)
            {
                nextResolve = Time.realtimeSinceStartup + 1f;
                var p1 = HD_Player.Player1;
                holder = p1 != null ? p1.WeaponHolder : null;
                if (holder != null)
                    Plugin.Log.LogInfo("[VRCtl] gun model bound to player 1 weapon holder");
            }
            if (holder != null)
            {
                try { lastKnownType = holder.CurrentWeaponType; }
                catch { holder = null; } // holder mid-teardown - re-resolve later
            }
            return lastKnownType;
        }

        private void Rebuild(WeaponType type)
        {
            if (root == null)
            {
                root = new GameObject("HotdVR_GunModel");
                DontDestroyOnLoad(root);
                gunmetal = MakeMaterial(new Color(0.13f, 0.13f, 0.15f));
                grip = MakeMaterial(new Color(0.24f, 0.17f, 0.11f));
                accent = MakeMaterial(Color.white);
            }
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Destroy(root.transform.GetChild(i).gameObject);

            // Common grip under the controller position, leaning back.
            AddBox(new Vector3(0f, -0.045f, -0.01f), new Vector3(20f, 0f, 0f), new Vector3(0.026f, 0.09f, 0.032f), grip);

            switch (type)
            {
                case WeaponType.Crossbow:
                    SetColor(accent, new Color(0.9f, 0.55f, 0.15f));
                    AddBox(new Vector3(0f, 0.005f, 0.05f), Vector3.zero, new Vector3(0.024f, 0.03f, 0.26f), gunmetal);
                    AddBox(new Vector3(0f, 0.01f, 0.14f), Vector3.zero, new Vector3(0.30f, 0.014f, 0.02f), grip);
                    AddBox(new Vector3(0f, 0.03f, 0.0f), Vector3.zero, new Vector3(0.008f, 0.012f, 0.025f), accent);
                    break;
                case WeaponType.GrenadeLauncher:
                    SetColor(accent, new Color(0.3f, 0.75f, 0.3f));
                    AddBox(new Vector3(0f, 0f, -0.02f), Vector3.zero, new Vector3(0.04f, 0.05f, 0.14f), gunmetal);
                    AddCylinder(new Vector3(0f, 0.01f, 0.10f), 0.028f, 0.16f, gunmetal);
                    AddBox(new Vector3(0f, 0.04f, 0.0f), Vector3.zero, new Vector3(0.008f, 0.012f, 0.025f), accent);
                    break;
                case WeaponType.StakeGun:
                    SetColor(accent, new Color(0.95f, 0.85f, 0.25f));
                    AddBox(new Vector3(0f, 0.005f, 0.0f), Vector3.zero, new Vector3(0.034f, 0.05f, 0.12f), gunmetal);
                    AddCylinder(new Vector3(0f, 0.01f, 0.16f), 0.007f, 0.16f, gunmetal);
                    AddBox(new Vector3(0f, 0.038f, 0.0f), Vector3.zero, new Vector3(0.008f, 0.012f, 0.025f), accent);
                    break;
                case WeaponType.AssaultRifle:
                    SetColor(accent, new Color(0.3f, 0.5f, 0.95f));
                    AddBox(new Vector3(0f, 0.005f, 0.05f), Vector3.zero, new Vector3(0.032f, 0.05f, 0.34f), gunmetal);
                    AddCylinder(new Vector3(0f, 0.01f, 0.26f), 0.008f, 0.10f, gunmetal);
                    AddBox(new Vector3(0f, -0.005f, -0.14f), Vector3.zero, new Vector3(0.028f, 0.045f, 0.10f), grip);
                    AddBox(new Vector3(0f, -0.04f, 0.05f), new Vector3(-15f, 0f, 0f), new Vector3(0.024f, 0.07f, 0.03f), gunmetal);
                    AddBox(new Vector3(0f, 0.04f, 0.02f), Vector3.zero, new Vector3(0.008f, 0.012f, 0.025f), accent);
                    break;
                default: // Pistol
                    SetColor(accent, Color.white);
                    AddBox(new Vector3(0f, 0.01f, 0.04f), Vector3.zero, new Vector3(0.030f, 0.045f, 0.14f), gunmetal);
                    AddCylinder(new Vector3(0f, 0.015f, 0.13f), 0.010f, 0.06f, gunmetal);
                    AddBox(new Vector3(0f, 0.038f, 0.06f), Vector3.zero, new Vector3(0.008f, 0.01f, 0.02f), accent);
                    break;
            }

            builtType = type;
            Plugin.Log.LogInfo($"[VRCtl] gun model built: {type}");
        }

        private void AddBox(Vector3 pos, Vector3 euler, Vector3 size, Material mat) =>
            AddPart(PrimitiveType.Cube, pos, euler, size, mat);

        // Unity cylinders are Y-axis, height 2 at scale 1 - rotate onto Z (barrel axis).
        private void AddCylinder(Vector3 pos, float radius, float length, Material mat) =>
            AddPart(PrimitiveType.Cylinder, pos, new Vector3(90f, 0f, 0f), new Vector3(radius * 2f, length * 0.5f, radius * 2f), mat);

        private void AddPart(PrimitiveType type, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var part = GameObject.CreatePrimitive(type);
            Destroy(part.GetComponent<Collider>());
            var mr = part.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = pos;
            part.transform.localEulerAngles = euler;
            part.transform.localScale = scale;
        }

        private static Material MakeMaterial(Color color)
        {
            var shader = Shader.Find("HDRP/Lit");
            if (shader == null)
                shader = Shader.Find("HDRP/Unlit");
            var mat = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            SetColor(mat, color);
            return mat;
        }

        private static void SetColor(Material mat, Color color)
        {
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_UnlitColor"))
                mat.SetColor("_UnlitColor", color);
        }
    }
}
