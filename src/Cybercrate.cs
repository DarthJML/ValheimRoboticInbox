using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DarthJML.RoboticInbox
{
    // Gives the cloned wood chest the Viking Cybercrate look. Mesh, textures and icon come from an
    // AssetBundle embedded in this DLL (built by the roboticinbox-bundle Unity project). Gameplay
    // components (ZNetView, Piece, Container, WearNTear) are untouched; only the visual child, its
    // collider and the icon change. On any failure the vanilla chest look is kept.
    internal static class Cybercrate
    {
        const string ResourceName = "RoboticInbox.cybercrate";
        const string VisualName = "Cybercrate";
        // Custom/Piece texture slots -> bundle texture names.
        static readonly string[,] TextureSlots =
        {
            { "_MainTex", "cybercrate_v3_basecolor" },
            { "_BumpMap", "cybercrate_v3_normal_gl" },
            { "_EmissionMap", "cybercrate_v3_emission" },
            { "_MetallicTex", "cybercrate_v3_metallic" },
        };

        static AssetBundle _bundle;
        static bool _loadFailed;
        static Material _material;

        static AssetBundle LoadBundle()
        {
            if (_bundle != null || _loadFailed) return _bundle;
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                {
                    if (s == null) throw new Exception("embedded resource " + ResourceName + " missing");
                    var bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = s.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) throw new EndOfStreamException();
                        read += n;
                    }
                    _bundle = AssetBundle.LoadFromMemory(bytes);
                }
                if (_bundle == null) throw new Exception("AssetBundle.LoadFromMemory returned null");
                Debug.Log("RoboticInbox: Cybercrate bundle loaded: " + string.Join(", ", _bundle.GetAllAssetNames()));
            }
            catch (Exception e)
            {
                _loadFailed = true;
                Debug.LogError("RoboticInbox: Cybercrate bundle failed to load, keeping the wood chest look: " + e);
            }
            return _bundle;
        }

        internal static void Apply(GameObject clone)
        {
            var bundle = LoadBundle();
            if (bundle == null) return;
            var prefab = bundle.LoadAsset<GameObject>(VisualName);
            if (prefab == null)
            {
                Debug.LogError("RoboticInbox: Cybercrate prefab missing from bundle, keeping the wood chest look.");
                return;
            }

            var wnt = clone.GetComponent<WearNTear>();
            var container = clone.GetComponent<Container>();
            var piece = clone.GetComponent<Piece>();

            // Old visuals: the "New" child (woodchest body + open/closed lids + LODGroup + MeshCollider)
            // and anything WearNTear swaps between, in case a game update adds Worn/Broken variants.
            var oldVisuals = new List<GameObject>();
            void AddOld(GameObject go) { if (go != null && go != clone && !oldVisuals.Contains(go)) oldVisuals.Add(go); }
            AddOld(clone.transform.Find("New")?.gameObject);
            if (wnt != null) { AddOld(wnt.m_new); AddOld(wnt.m_worn); AddOld(wnt.m_broken); }

            // Vanilla look we replace: material (Custom/Piece), LOD cull height, layer, footprint.
            var main = wnt != null && wnt.m_new != null ? wnt.m_new : (oldVisuals.Count > 0 ? oldVisuals[0] : null);
            Material source = null;
            float lodHeight = 0f;
            int layer = main != null ? main.layer : clone.layer;
            var oldBounds = new Bounds();
            bool haveOld = main != null && Encapsulate(clone.transform, main.transform, ref oldBounds);
            if (main != null)
            {
                var lod = main.GetComponent<LODGroup>();
                if (lod != null && lod.lodCount > 0) lodHeight = lod.GetLODs()[lod.lodCount - 1].screenRelativeTransitionHeight;
            }
            foreach (var go in oldVisuals)
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var m = mr.sharedMaterial;
                    if (m == null) continue;
                    if (m.shader != null && m.shader.name == "Custom/Piece") { source = m; break; }
                    if (source == null) source = m;
                }

            // Material first: if it throws, the chest is still untouched (vanilla look).
            var material = MakeMaterial(source, bundle);
            _material = material;

            var visual = Object.Instantiate(prefab, clone.transform, false);
            visual.name = VisualName;
            foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            if (lodHeight > 0f)
            {
                var lod = visual.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(lodHeight, renderers) });
                lod.RecalculateBounds();
            }

            // Collider fitted to the crate (vanilla used a convex MeshCollider of the wood chest).
            var newBounds = new Bounds();
            Encapsulate(visual.transform, visual.transform, ref newBounds);
            var box = visual.AddComponent<BoxCollider>();
            box.center = newBounds.center;
            box.size = newBounds.size;

            // Snow cover (WearNTear.m_snow, a sibling of "New"): fit it onto the crate lid.
            var snow = wnt != null ? wnt.m_snow : null;
            if (snow != null && haveOld && !IsUnderAny(snow.transform, oldVisuals))
            {
                var crate = new Bounds();
                Encapsulate(clone.transform, visual.transform, ref crate);
                var st = snow.transform;
                var sc = st.localScale;
                if (oldBounds.size.x > 0.01f) sc.x *= crate.size.x / oldBounds.size.x;
                if (oldBounds.size.z > 0.01f) sc.z *= crate.size.z / oldBounds.size.z;
                st.localScale = sc;
                st.localPosition += new Vector3(crate.center.x - oldBounds.center.x, crate.max.y - oldBounds.max.y, crate.center.z - oldBounds.center.z);
            }

            string oldNames = string.Join(",", oldVisuals.ConvertAll(g => g.name));
            foreach (var go in oldVisuals) Object.DestroyImmediate(go);

            // Anything else visible that isn't ours or the snow (shouldn't exist today) gets hidden.
            foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(visual.transform) || (snow != null && r == snow)) continue;
                Debug.LogWarning("RoboticInbox: hiding leftover chest renderer " + r.name);
                r.enabled = false;
            }

            if (wnt != null) wnt.m_new = wnt.m_worn = wnt.m_broken = visual;
            if (container != null) { container.m_open = null; container.m_closed = null; }

            var icon = bundle.LoadAsset<Sprite>("cybercrate_icon");
            if (piece != null && icon != null) piece.m_icon = icon;
            else Debug.LogWarning("RoboticInbox: Cybercrate icon not found in bundle.");

            Debug.Log($"RoboticInbox: Cybercrate visual applied: renderers={renderers.Length} layer={LayerMask.LayerToName(layer)} lod={lodHeight:F3} " +
                $"collider center={box.center.ToString("F3")} size={box.size.ToString("F3")} oldVisuals=[{oldNames}] snowFitted={snow != null && haveOld} icon={icon != null}");
        }

        static Material MakeMaterial(Material source, AssetBundle bundle)
        {
            var shader = source != null ? source.shader : Shader.Find("Custom/Piece");
            var mat = source != null ? new Material(source) : new Material(shader);
            mat.name = "RoboticInbox_Cybercrate";

            var set = new List<string>();
            var missing = new List<string>();
            for (int i = 0; i < TextureSlots.GetLength(0); i++)
            {
                string prop = TextureSlots[i, 0], asset = TextureSlots[i, 1];
                var tex = bundle.LoadAsset<Texture2D>(asset);
                if (tex != null && mat.HasProperty(prop)) { mat.SetTexture(prop, tex); set.Add(prop + "=" + asset); }
                else missing.Add(prop + (tex == null ? "(no texture)" : "(no property)"));
            }
            if (mat.HasProperty("_MainTex")) { mat.mainTextureScale = Vector2.one; mat.mainTextureOffset = Vector2.zero; }

            // Floats/colours (names from Custom/Piece; values follow vanilla emissive/metal pieces).
            SetFloat(mat, "_BumpScale", 1f, set, missing);          // baked tangent-space normal, no exaggeration (chest had 3.41)
            SetFloat(mat, "_Metallic", 1f, set, missing);           // metal strength inside _MetallicTex mask (iron hinges/hasp)
            SetFloat(mat, "_Glossiness", 0.08f, set, missing);      // base smoothness: v3 stone/wood roughness 0.86-0.97
            SetFloat(mat, "_MetallicAlphaGloss", 0.35f, set, missing); // smoothness of the rusty iron
            SetColor(mat, "_Color", Color.white, set, missing);
            SetColor(mat, "_MetalColor", Color.white, set, missing);
            UpdateGlow(mat);
            set.Add("_EmissionColor=white*" + RoboticInboxPlugin.GlowIntensity.Value);

            var props = new List<string>();
            if (shader != null)
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                    props.Add(shader.GetPropertyName(i) + ":" + shader.GetPropertyType(i));
            Debug.Log($"RoboticInbox: Cybercrate material from '{(source != null ? source.name : "<none>")}' shader='{(shader != null ? shader.name : "<null>")}' " +
                $"set=[{string.Join(", ", set)}] missing=[{string.Join(", ", missing)}] shaderProps=[{string.Join(", ", props)}]");
            if (shader == null || shader.name != "Custom/Piece")
                Debug.LogWarning("RoboticInbox: Cybercrate expected shader Custom/Piece, got " + (shader != null ? shader.name : "null"));
            return mat;
        }

        // Emission texture is already cyan; the colour is a white HDR multiplier (vanilla glow ~1-2).
        internal static void UpdateGlow(Material mat = null)
        {
            mat = mat ?? _material;
            if (mat != null && mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", Color.white * RoboticInboxPlugin.GlowIntensity.Value);
        }

        static void SetFloat(Material m, string p, float v, List<string> set, List<string> missing)
        {
            if (m.HasProperty(p)) { m.SetFloat(p, v); set.Add(p + "=" + v); } else missing.Add(p);
        }

        static void SetColor(Material m, string p, Color v, List<string> set, List<string> missing)
        {
            if (m.HasProperty(p)) { m.SetColor(p, v); set.Add(p + "=" + v); } else missing.Add(p);
        }

        static bool IsUnderAny(Transform t, List<GameObject> roots) => roots.Exists(r => t.IsChildOf(r.transform));

        // Mesh bounds of everything under `of`, expressed in `space`'s local coordinates (works on
        // inactive templates, unlike Renderer.bounds). Skips hidden parts like the open lid.
        static bool Encapsulate(Transform space, Transform of, ref Bounds bounds)
        {
            bool any = false;
            foreach (var mf in of.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !ActiveWithin(mf.transform, of)) continue;
                var b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var p = space.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return any;
        }

        static bool ActiveWithin(Transform t, Transform root)
        {
            for (; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return false;
                if (t == root) return true;
            }
            return true;
        }
    }
}
