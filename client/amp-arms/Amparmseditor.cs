using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UInput = UnityEngine.Input;

namespace C11_TN4_Client.amp_arms
{
    /// <summary>
    /// Blender-style gizmo for positioning AMP-arm parts on an inspect / modding-window preview.
    ///
    ///   F10         toggle edit mode (with a registered helmet open in a preview window)
    ///   R           switch between Move and Rotate
    ///   T           switch part (AMP Arms / RACLINK / ...). Not Tab: that closes the inventory.
    ///   Backspace   reset the selected part to its defaults
    ///
    ///   Move:   drag an arrow to slide along X (red), Y (green) or Z (blue),
    ///           or drag the white centre box to move freely across the screen.
    ///   Rotate: drag a ring to turn around that axis.
    ///
    /// Axes are the part's own slot axes (the same space the F12 X/Y/Z values use).
    /// Values go straight into the helmet's F12 settings, which the tuner on your character
    /// also reads, so edits apply in raid immediately. Saved to disk when you leave edit mode.
    /// </summary>
    public class AmpArmsEditor : MonoBehaviour
    {
        private enum Tool   { Move, Rotate }
        private enum Handle { None, X, Y, Z, Free }

        private const float HitPixels  = 8f;    // grab distance around a handle
        private const int   RingPoints = 64;

        private static readonly Color[] AxisColors = { new Color(1f, 0.25f, 0.25f), new Color(0.35f, 1f, 0.35f), new Color(0.35f, 0.55f, 1f) };
        private static readonly Color   HotColor   = new Color(1f, 0.9f, 0.2f);

        private static readonly int ZTestProp  = Shader.PropertyToID("_ZTest");
        private static readonly int CullProp   = Shader.PropertyToID("_Cull");
        private static readonly int ZWriteProp = Shader.PropertyToID("_ZWrite");

        private bool   _editing;
        private Tool   _tool = Tool.Move;
        private int    _partIndex;
        private Handle _hover, _drag;
        private Vector2 _lastMouse;
        private bool   _savedAutoSave = true;

        private HelmetMountManager _target;
        private readonly List<HelmetSlotTuner> _parts = new List<HelmetSlotTuner>();

        // Per-frame view state, shared by input and drawing
        private Camera _cam;
        private Rect   _view;           // where the preview camera's image sits on screen

        // Holds the preview model still while a handle is dragged (LMB also spins the preview)
        private readonly List<(Transform t, Vector3 pos, Quaternion rot)> _frozen = new List<(Transform, Vector3, Quaternion)>();

        private Material _lineMat;
        private GUIStyle _style;

        // ── Update ────────────────────────────────────────────────────────────

        private void Update()
        {
            // Only a model that's actually on screen in an inspect/modding window counts.
            // Hideout stands (main camera) and icon renders (never displayed) don't.
            _target = FindVisiblePreview(out _cam, out _view);

            if (C11Plugin.AmpEditKey.Value.IsDown())
            {
                if (_editing) StopEditing();
                else if (_target != null) StartEditing();
            }
            if (!_editing) return;
            if (_target == null) { StopEditing(); return; }

            RefreshParts();
            if (_parts.Count == 0) return;

            if (C11Plugin.AmpEditPartKey.Value.IsDown()) { EndDrag(); _partIndex = (_partIndex + 1) % _parts.Count; }
            if (C11Plugin.AmpEditToolKey.Value.IsDown()) { EndDrag(); _tool = _tool == Tool.Move ? Tool.Rotate : Tool.Move; }
            _partIndex = Mathf.Clamp(_partIndex, 0, _parts.Count - 1);
            var part = _parts[_partIndex];
            if (UInput.GetKeyDown(KeyCode.Backspace)) ResetPart(part);

            Vector2 mouse = UInput.mousePosition;

            if (_drag == Handle.None)
            {
                _hover = HitTest(part, mouse);
                if (_hover != Handle.None && UInput.GetMouseButtonDown(0))
                {
                    _drag = _hover;
                    _lastMouse = mouse;
                    Freeze();
                }
                return;
            }

            if (!UInput.GetMouseButton(0)) { EndDrag(); return; }

            if (_tool == Tool.Move) DragMove(part, mouse);
            else                    DragRotate(part, mouse);
            _lastMouse = mouse;
        }

        // ── Dragging ──────────────────────────────────────────────────────────

        private void DragMove(HelmetSlotTuner part, Vector2 mouse)
        {
            Vector3 p = part.transform.position;
            float depth = Depth(p);
            Vector3 worldDelta;

            if (_drag == Handle.Free)
            {
                worldDelta = ScreenToWorld(mouse, depth) - ScreenToWorld(_lastMouse, depth);
            }
            else
            {
                // Mouse movement projected onto the arrow as it appears on screen
                Vector3 axis = Axis(part, _drag);
                float size = GizmoWorldSize(p);
                Vector2 s0 = WorldToScreen(p), s1 = WorldToScreen(p + axis * size);
                Vector2 dir = s1 - s0;
                if (dir.magnitude < 1f) return;                    // arrow points straight at you
                float along = Vector2.Dot(mouse - _lastMouse, dir.normalized) / dir.magnitude * size;
                worldDelta = axis * along;
            }

            var parent = part.transform.parent;
            Vector3 local = parent != null ? parent.InverseTransformVector(worldDelta) : worldDelta;
            var e = part.Entries;
            e[0].Value += local.x;
            e[1].Value += local.y;
            e[2].Value += local.z;
        }

        private void DragRotate(HelmetSlotTuner part, Vector2 mouse)
        {
            Vector3 p = part.transform.position;
            Vector2 c = WorldToScreen(p);
            float a0 = Mathf.Atan2(_lastMouse.y - c.y, _lastMouse.x - c.x) * Mathf.Rad2Deg;
            float a1 = Mathf.Atan2(mouse.y - c.y, mouse.x - c.x) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(a0, a1);                // counter-clockwise on screen = positive
            if (Mathf.Abs(delta) < 0.001f) return;

            Vector3 axis = Axis(part, _drag);
            // A right-handed turn about an axis pointing at the camera looks counter-clockwise
            if (Vector3.Dot(axis, _cam.transform.position - p) < 0f) delta = -delta;

            var parent = part.transform.parent;
            Quaternion world = Quaternion.AngleAxis(delta, axis) * part.transform.rotation;
            Quaternion local = parent != null ? Quaternion.Inverse(parent.rotation) * world : world;
            Vector3 eul = local.eulerAngles;

            var e = part.Entries;
            e[3].Value = Wrap(eul.x);
            e[4].Value = Wrap(eul.y);
            e[5].Value = Wrap(eul.z);
        }

        private void EndDrag()
        {
            _drag = Handle.None;
            Unfreeze();
        }

        // ── Hit testing ───────────────────────────────────────────────────────

        private Handle HitTest(HelmetSlotTuner part, Vector2 mouse)
        {
            Vector3 p = part.transform.position;
            if (Depth(p) <= 0f) return Handle.None;
            float size = GizmoWorldSize(p);
            Vector2 c = WorldToScreen(p);

            if (_tool == Tool.Move)
            {
                if (Mathf.Abs(mouse.x - c.x) <= HitPixels && Mathf.Abs(mouse.y - c.y) <= HitPixels) return Handle.Free;
                Handle best = Handle.None; float bestD = HitPixels;
                foreach (var h in new[] { Handle.X, Handle.Y, Handle.Z })
                {
                    float d = DistToSegment(mouse, c, WorldToScreen(p + Axis(part, h) * size));
                    if (d < bestD) { bestD = d; best = h; }
                }
                return best;
            }
            else
            {
                Handle best = Handle.None; float bestD = HitPixels;
                foreach (var h in new[] { Handle.X, Handle.Y, Handle.Z })
                {
                    var ring = RingScreen(part, h, size);
                    for (int i = 0; i < ring.Length; i++)
                    {
                        float d = DistToSegment(mouse, ring[i], ring[(i + 1) % ring.Length]);
                        if (d < bestD) { bestD = d; best = h; }
                    }
                }
                return best;
            }
        }

        // ── Drawing ───────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (_target == null) return;
            DrawHud();

            if (!_editing || Event.current.type != EventType.Repaint) return;
            if (_parts.Count == 0 || _cam == null) return;

            var part = _parts[Mathf.Clamp(_partIndex, 0, _parts.Count - 1)];
            Vector3 p = part.transform.position;
            if (Depth(p) <= 0f) return;
            float size = GizmoWorldSize(p);
            Vector2 c = WorldToScreen(p);

            if (!EnsureLineMaterial()) return;
            _lineMat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();                 // bottom-left origin, same as Input.mousePosition
            GL.Begin(GL.LINES);

            if (_tool == Tool.Move)
            {
                foreach (var h in new[] { Handle.X, Handle.Y, Handle.Z })
                {
                    Color col = Col(h);
                    Vector2 tip = WorldToScreen(p + Axis(part, h) * size);
                    Line(c, tip, col, 3);
                    Vector2 dir = (tip - c).sqrMagnitude > 1f ? (tip - c).normalized : Vector2.up;
                    Vector2 side = new Vector2(-dir.y, dir.x);
                    Line(tip, tip - dir * 12f + side * 6f, col, 3);   // arrowhead
                    Line(tip, tip - dir * 12f - side * 6f, col, 3);
                }
                Color box = _drag == Handle.Free || _hover == Handle.Free ? HotColor : Color.white;
                Square(c, HitPixels, box);
            }
            else
            {
                foreach (var h in new[] { Handle.X, Handle.Y, Handle.Z })
                {
                    var ring = RingScreen(part, h, size);
                    for (int i = 0; i < ring.Length; i++) Line(ring[i], ring[(i + 1) % ring.Length], Col(h), 2);
                }
                Square(c, 3f, Color.white);
            }

            GL.End();
            GL.PopMatrix();
        }

        private Color Col(Handle h) => (_drag == h || (_drag == Handle.None && _hover == h)) ? HotColor : AxisColors[(int)h - 1];

        private static void Line(Vector2 a, Vector2 b, Color col, int thickness)
        {
            GL.Color(col);
            Vector2 n = (b - a).sqrMagnitude > 0.0001f ? new Vector2(-(b - a).y, (b - a).x).normalized : Vector2.zero;
            for (int i = 0; i < thickness; i++)
            {
                Vector2 o = n * (i - (thickness - 1) * 0.5f);
                GL.Vertex3(a.x + o.x, a.y + o.y, 0f);
                GL.Vertex3(b.x + o.x, b.y + o.y, 0f);
            }
        }

        private static void Square(Vector2 c, float half, Color col)
        {
            Vector2 a = c + new Vector2(-half, -half), b = c + new Vector2(half, -half),
                    d = c + new Vector2(half, half),   e = c + new Vector2(-half, half);
            Line(a, b, col, 2); Line(b, d, col, 2); Line(d, e, col, 2); Line(e, a, col, 2);
        }

        private bool EnsureLineMaterial()
        {
            if (_lineMat != null) return true;
            var shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default");
            if (shader == null) return false;
            _lineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _lineMat.SetInt(ZTestProp, (int)UnityEngine.Rendering.CompareFunction.Always);
            _lineMat.SetInt(CullProp, (int)UnityEngine.Rendering.CullMode.Off);
            _lineMat.SetInt(ZWriteProp, 0);
            return true;
        }

        private void DrawHud()
        {
            if (_style == null) _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, wordWrap = false };
            string key = C11Plugin.AmpEditKey.Value.ToString();

            if (!_editing) { GUI.Box(new Rect(12, 12, 320, 26), $" AMP arms: press {key} to edit", _style); return; }
            if (_parts.Count == 0) { GUI.Box(new Rect(12, 12, 380, 44), $" EDITING - no AMP parts mounted on this helmet\n {key} to exit", _style); return; }

            var part = _parts[Mathf.Clamp(_partIndex, 0, _parts.Count - 1)];
            var e = part.Entries;
            string tool = _tool == Tool.Move ? "MOVE" : "ROTATE";
            string vals = e != null && e.Length >= 6
                ? $" pos  {e[0].Value,8:0.0000} {e[1].Value,8:0.0000} {e[2].Value,8:0.0000}\n rot  {e[3].Value,8:0.0} {e[4].Value,8:0.0} {e[5].Value,8:0.0}"
                : " (no settings)";

            GUI.Box(new Rect(12, 12, 430, 150),
                $" {tool}: {part.PartLabel}   [{_partIndex + 1}/{_parts.Count}]\n" +
                $"{vals}\n\n" +
                (_tool == Tool.Move ? " Drag an arrow along X/Y/Z, or the box to move freely\n"
                                    : " Drag a ring to rotate around that axis\n") +
                $" {C11Plugin.AmpEditToolKey.Value} move/rotate   {C11Plugin.AmpEditPartKey.Value} part   Backspace reset\n" +
                $" {key} save & exit", _style);
        }

        // ── Geometry ──────────────────────────────────────────────────────────

        /// <summary>The part's slot axis in world space (the space its F12 X/Y/Z values use).</summary>
        private static Vector3 Axis(HelmetSlotTuner part, Handle h)
        {
            Vector3 local = h == Handle.X ? Vector3.right : h == Handle.Y ? Vector3.up : Vector3.forward;
            var parent = part.transform.parent;
            return (parent != null ? parent.TransformDirection(local) : local).normalized;
        }

        private Vector2[] RingScreen(HelmetSlotTuner part, Handle h, float radius)
        {
            Vector3 p = part.transform.position, axis = Axis(part, h);
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            var pts = new Vector2[RingPoints];
            for (int i = 0; i < RingPoints; i++)
            {
                float a = i * Mathf.PI * 2f / RingPoints;
                pts[i] = WorldToScreen(p + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius);
            }
            return pts;
        }

        /// <summary>World length that appears as a fixed number of pixels, so the gizmo stays one size on screen.</summary>
        private float GizmoWorldSize(Vector3 p)
        {
            float depth = Depth(p);
            Vector2 s = WorldToScreen(p);
            float px = C11Plugin.AmpEditGizmoSize.Value;
            return Vector3.Distance(ScreenToWorld(s, depth), ScreenToWorld(s + Vector2.right * px, depth));
        }

        private float Depth(Vector3 world) => _cam.WorldToViewportPoint(world).z;

        private Vector2 WorldToScreen(Vector3 world)
        {
            Vector3 vp = _cam.WorldToViewportPoint(world);
            return new Vector2(_view.x + vp.x * _view.width, _view.y + vp.y * _view.height);
        }

        private Vector3 ScreenToWorld(Vector2 screen, float depth)
        {
            float vx = (screen.x - _view.x) / _view.width, vy = (screen.y - _view.y) / _view.height;
            return _cam.ViewportToWorldPoint(new Vector3(vx, vy, depth));
        }

        /// <summary>
        /// Screen rectangle (bottom-left origin) where this camera's image is shown. Inspect
        /// windows render the model into a texture displayed by a RawImage; false if the camera
        /// has no texture or nothing on screen is showing it.
        /// </summary>
        private static bool TryViewRect(Camera cam, out Rect view)
        {
            view = default;
            var rt = cam.targetTexture;
            if (rt == null) return false;

            foreach (var img in FindObjectsOfType<RawImage>())
            {
                if (img.texture != rt || !img.isActiveAndEnabled) continue;
                var corners = new Vector3[4];
                img.rectTransform.GetWorldCorners(corners);
                var canvas = img.canvas;
                Camera uiCam = canvas != null && canvas.renderMode != UnityEngine.RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCam, corners[0]);
                Vector2 max = RectTransformUtility.WorldToScreenPoint(uiCam, corners[2]);
                view = new Rect(min, max - min);
                return view.width > 1f && view.height > 1f;
            }
            return false;
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        private static float Wrap(float deg) => Mathf.Repeat(deg + 180f, 360f) - 180f;

        // ── Freeze the preview while dragging ─────────────────────────────────

        private void Freeze()
        {
            _frozen.Clear();
            for (var t = _target.transform; t != null; t = t.parent)
                _frozen.Add((t, t.localPosition, t.localRotation));
            Camera.onPreCull += HoldPose;
        }

        private void Unfreeze()
        {
            if (_frozen.Count == 0) return;
            Camera.onPreCull -= HoldPose;
            _frozen.Clear();
        }

        // Right before the preview camera draws, undo any spin applied to the model this frame
        private void HoldPose(Camera cam)
        {
            if (cam != _cam) return;
            foreach (var (t, pos, rot) in _frozen)
                if (t != null) { t.localPosition = pos; t.localRotation = rot; }
        }

        // ── Mode / lookup ─────────────────────────────────────────────────────

        private static void ResetPart(HelmetSlotTuner part)
        {
            if (part.Entries == null) return;
            foreach (var entry in part.Entries)
                if (entry != null) entry.Value = (float)entry.DefaultValue;
        }

        private void StartEditing()
        {
            _editing = true;
            _partIndex = 0;
            var cfg = C11Plugin.Instance.Config;
            _savedAutoSave = cfg.SaveOnConfigSet;
            cfg.SaveOnConfigSet = false;       // values change every frame while dragging
        }

        private void StopEditing()
        {
            EndDrag();
            _editing = false;
            var cfg = C11Plugin.Instance.Config;
            cfg.Save();
            cfg.SaveOnConfigSet = _savedAutoSave;
            C11Plugin.DebugLog("[AmpArmsEditor] Edit mode off, settings saved");
        }

        private void OnDisable()
        {
            if (_editing) StopEditing();
        }

        /// <summary>Newest preview model that is on screen in an inspect / modding window, or null.</summary>
        private static HelmetMountManager FindVisiblePreview(out Camera cam, out Rect view)
        {
            cam = null; view = default;
            var list = HelmetMountManager.Previews;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var m = list[i];
                if (m == null) { list.RemoveAt(i); continue; }
                if (!m.gameObject.activeInHierarchy) continue;

                var c = FindCameraFor(m.transform);
                if (c != null && TryViewRect(c, out var v)) { cam = c; view = v; return m; }
            }
            return null;
        }

        private void RefreshParts()
        {
            _parts.Clear();
            if (_target.EquipTuner != null && _target.EquipTuner.Entries?.Length >= 6) _parts.Add(_target.EquipTuner);
            if (_target.ArmsTuner  != null && _target.ArmsTuner.Entries?.Length  >= 6) _parts.Add(_target.ArmsTuner);
        }

        /// <summary>The enabled camera that renders this object's layer and has it in view.</summary>
        private static Camera FindCameraFor(Transform t)
        {
            Camera best = null;
            float bestDist = float.MaxValue;
            int layerBit = 1 << t.gameObject.layer;
            foreach (var cam in Camera.allCameras)
            {
                if (!cam.enabled || (cam.cullingMask & layerBit) == 0) continue;
                Vector3 vp = cam.WorldToViewportPoint(t.position);
                if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;
                if (vp.z < bestDist) { best = cam; bestDist = vp.z; }
            }
            return best;
        }
    }
}