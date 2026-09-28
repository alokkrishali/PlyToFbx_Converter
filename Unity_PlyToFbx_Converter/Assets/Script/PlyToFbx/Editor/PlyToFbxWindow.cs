using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PlyToFbx.Editor
{
    public sealed class PlyToFbxWindow : EditorWindow
    {
        private readonly PlyToFbxConverter converter = new PlyToFbxConverter(new PointCloudFileReader(), new PlyMeshFactory());
        private readonly IFbxExporter fbxExporter = new UnityFbxExporter();
        private readonly IPrefabSaver prefabSaver = new UnityPrefabSaver();
        private string sourcePath = string.Empty;
        private string status = "Choose a PLY or PCD point-cloud file.";
        private Mesh mesh;

        [MenuItem("Tools/3D Tools/PLY to FBX Converter")]
        public static void ShowWindow()
        {
            PlyToFbxWindow window = GetWindow<PlyToFbxWindow>();
            window.titleContent = new GUIContent("PLY Converter");
            window.minSize = new Vector2(360, 240);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Point Cloud Converter", EditorStyles.boldLabel);
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            sourcePath = EditorGUILayout.TextField("PLY File", sourcePath);
            if (GUILayout.Button("Browse", GUILayout.Width(72)))
                BrowseForPly();
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(sourcePath)))
            {
                if (GUILayout.Button("Load Point Cloud", GUILayout.Height(28)))
                    LoadMesh();
            }

            if (mesh != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Mesh", mesh.name);
                bool isPointCloud = mesh.GetTopology(0) == MeshTopology.Points;
                EditorGUILayout.LabelField(isPointCloud ? "Points" : "Vertices", mesh.vertexCount.ToString("N0"));
                if (!isPointCloud)
                    EditorGUILayout.LabelField("Triangles", (mesh.GetIndexCount(0) / 3).ToString("N0"));

                EditorGUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("View Mesh", GUILayout.Height(32)))
                    PlyMeshViewerWindow.Open(mesh, mesh.name);
                if (GUILayout.Button("Convert to FBX", GUILayout.Height(32)))
                    ExportFbx();
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("Save as Prefab", GUILayout.Height(28)))
                    SavePrefab();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        private void BrowseForPly()
        {
            string selectedPath = EditorUtility.OpenFilePanelWithFilters(
                "Select PLY or PCD file",
                string.Empty,
                new[] { "Point cloud files", "ply,pcd", "PLY files", "ply", "PCD files", "pcd" });
            if (!string.IsNullOrEmpty(selectedPath))
            {
                sourcePath = selectedPath;
                LoadMesh();
            }
        }

        private void LoadMesh()
        {
            try
            {
                Mesh loadedMesh = converter.Convert(sourcePath);
                if (mesh != null)
                    DestroyImmediate(mesh);
                mesh = loadedMesh;
                string format = Path.GetExtension(sourcePath).TrimStart('.').ToUpperInvariant();
                status = loadedMesh.GetTopology(0) == MeshTopology.Points
                    ? format + " point cloud loaded. Preview, export, or save it as a prefab."
                    : format + " mesh loaded. Preview, export, or save it as a prefab.";
            }
            catch (Exception exception)
            {
                status = exception.Message;
                Debug.LogException(exception);
            }
        }

        private void ExportFbx()
        {
            string defaultName = Path.GetFileNameWithoutExtension(sourcePath) + ".fbx";
            string filePath = EditorUtility.SaveFilePanel("Export FBX", Path.GetDirectoryName(sourcePath), defaultName, "fbx");
            if (string.IsNullOrEmpty(filePath))
                return;

            GameObject exportObject = null;
            try
            {
                exportObject = PlyMeshGameObjectFactory.Create(mesh, Path.GetFileNameWithoutExtension(sourcePath));
                string exportedPath = fbxExporter.Export(exportObject, filePath);
                status = mesh.GetTopology(0) == MeshTopology.Points
                    ? "FBX saved with sampled point markers (up to 50,000) to " + exportedPath
                    : "FBX saved to " + exportedPath;
                if (exportedPath.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                    AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                status = exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                if (exportObject != null)
                    DestroyImmediate(exportObject);
            }
        }

        private void SavePrefab()
        {
            string defaultName = Path.GetFileNameWithoutExtension(sourcePath);
            string prefabPath = EditorUtility.SaveFilePanelInProject("Save PLY Prefab", defaultName, "prefab", "Choose a project folder for the prefab.");
            if (string.IsNullOrEmpty(prefabPath))
                return;

            GameObject prefabObject = null;
            try
            {
                prefabObject = PlyMeshGameObjectFactory.Create(mesh, defaultName);
                string savedPath = prefabSaver.Save(prefabObject, prefabPath);
                status = "Prefab saved to " + savedPath;
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                status = exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                if (prefabObject != null)
                    DestroyImmediate(prefabObject);
            }
        }

        private void OnDisable()
        {
            if (mesh != null)
                DestroyImmediate(mesh);
        }
    }
}
