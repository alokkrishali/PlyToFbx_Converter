using UnityEditor;
using UnityEngine;

namespace PlyToFbx.Editor
{
    public sealed class PlyMeshViewerWindow : EditorWindow
    {
        private PreviewRenderUtility preview;
        private Material previewMaterial;
        private Mesh mesh;
        private string meshLabel;

        public static void Open(Mesh mesh, string meshLabel)
        {
            PlyMeshViewerWindow window = GetWindow<PlyMeshViewerWindow>(true, "PLY Viewer", true);
            if (window.mesh != null)
                DestroyImmediate(window.mesh);
            window.mesh = Instantiate(mesh);
            window.meshLabel = meshLabel;
            window.titleContent = new GUIContent("PLY Viewer");
            window.Show();
        }

        private void OnEnable()
        {
            preview = new PreviewRenderUtility();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader != null)
                previewMaterial = new Material(shader);
        }

        private void OnDisable()
        {
            if (preview != null)
                preview.Cleanup();
            if (previewMaterial != null)
                DestroyImmediate(previewMaterial);
            if (mesh != null)
                DestroyImmediate(mesh);
        }

        private void OnGUI()
        {
            if (mesh == null)
            {
                EditorGUILayout.HelpBox("Load a PLY mesh in the converter window to preview it.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(meshLabel, EditorStyles.boldLabel);
            bool isPointCloud = mesh.GetTopology(0) == MeshTopology.Points;
            EditorGUILayout.LabelField(isPointCloud ? "Points" : "Vertices", mesh.vertexCount.ToString("N0"));
            if (!isPointCloud)
                EditorGUILayout.LabelField("Triangles", (mesh.GetIndexCount(0) / 3).ToString("N0"));
            Rect previewRect = GUILayoutUtility.GetRect(100, 10000, 160, 10000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
                DrawPreview(previewRect);
        }

        private void DrawPreview(Rect rect)
        {
            preview.BeginPreview(rect, GUIStyle.none);
            preview.camera.clearFlags = CameraClearFlags.Color;
            preview.camera.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(0.16f, 0.18f, 0.19f)
                : new Color(0.72f, 0.75f, 0.74f);
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 10000f;

            Bounds bounds = mesh.bounds;
            Vector3 center = bounds.center;
            float distance = Mathf.Max(bounds.extents.magnitude * 2.8f, 1f);
            preview.camera.transform.position = center + new Vector3(1f, 0.8f, -1f).normalized * distance;
            preview.camera.transform.LookAt(center);
            preview.camera.fieldOfView = 30f;
            preview.camera.aspect = Mathf.Max(rect.width, 1f) / Mathf.Max(rect.height, 1f);

            if (previewMaterial != null)
                preview.DrawMesh(mesh, Matrix4x4.identity, previewMaterial, 0);
            preview.Render();
            GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.StretchToFill, false);
        }
    }
}
