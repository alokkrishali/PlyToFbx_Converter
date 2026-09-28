using System;
using System.IO;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlyToFbx.Editor
{
    public interface IFbxExporter
    {
        string Export(GameObject gameObject, string filePath);
    }

    public interface IPrefabSaver
    {
        string Save(GameObject gameObject, string prefabPath);
    }

    public sealed class UnityFbxExporter : IFbxExporter
    {
        private const int MaxPointMarkers = 50000;
        private static readonly int[] OctahedronTriangles =
        {
            0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4,
            5, 1, 2, 5, 2, 3, 5, 3, 4, 5, 4, 1
        };

        public string Export(GameObject gameObject, string filePath)
        {
            if (gameObject == null)
                throw new ArgumentNullException(nameof(gameObject));

            MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                throw new InvalidOperationException("The object has no mesh to export.");

            Mesh originalMesh = meshFilter.sharedMesh;
            Mesh exportMesh = null;
            try
            {
                if (originalMesh.GetTopology(0) == MeshTopology.Points)
                {
                    exportMesh = CreatePointMarkerMesh(originalMesh);
                    meshFilter.sharedMesh = exportMesh;
                }

                string exportedPath = ModelExporter.ExportObject(filePath, gameObject);
                if (string.IsNullOrEmpty(exportedPath))
                    throw new IOException("The FBX Exporter could not write the requested file.");
                return exportedPath;
            }
            finally
            {
                meshFilter.sharedMesh = originalMesh;
                if (exportMesh != null)
                    UnityEngine.Object.DestroyImmediate(exportMesh);
            }
        }

        private static Mesh CreatePointMarkerMesh(Mesh pointCloud)
        {
            Vector3[] points = pointCloud.vertices;
            if (points.Length == 0)
                throw new InvalidOperationException("The point cloud contains no points to export.");

            int markerCount = Mathf.Min(points.Length, MaxPointMarkers);
            int vertexCount = markerCount * 6;
            Vector3[] vertices = new Vector3[vertexCount];
            int[] triangles = new int[markerCount * OctahedronTriangles.Length];
            float radius = Mathf.Max(pointCloud.bounds.size.magnitude / 1000f, 0.0001f);
            Vector3[] offsets =
            {
                Vector3.up * radius,
                Vector3.right * radius,
                Vector3.forward * radius,
                Vector3.left * radius,
                Vector3.back * radius,
                Vector3.down * radius
            };

            for (int marker = 0; marker < markerCount; marker++)
            {
                int pointIndex = (int)((long)marker * points.Length / markerCount);
                int vertexOffset = marker * offsets.Length;
                for (int vertex = 0; vertex < offsets.Length; vertex++)
                    vertices[vertexOffset + vertex] = points[pointIndex] + offsets[vertex];

                int triangleOffset = marker * OctahedronTriangles.Length;
                for (int index = 0; index < OctahedronTriangles.Length; index++)
                    triangles[triangleOffset + index] = vertexOffset + OctahedronTriangles[index];
            }

            Mesh mesh = new Mesh
            {
                name = pointCloud.name + " FBX Markers",
                indexFormat = vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    public sealed class UnityPrefabSaver : IPrefabSaver
    {
        public string Save(GameObject gameObject, string prefabPath)
        {
            if (gameObject == null)
                throw new ArgumentNullException(nameof(gameObject));
            if (string.IsNullOrWhiteSpace(prefabPath) || !prefabPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("The prefab must be saved inside the project's Assets folder.", nameof(prefabPath));

            MeshFilter filter = gameObject.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException("The object has no mesh to save.");

            string directory = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            string meshPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(directory, Path.GetFileNameWithoutExtension(prefabPath) + "_Mesh.asset").Replace('\\', '/'));
            Mesh persistentMesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
            persistentMesh.name = filter.sharedMesh.name;

            try
            {
                AssetDatabase.CreateAsset(persistentMesh, meshPath);
                filter.sharedMesh = persistentMesh;
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(gameObject, prefabPath);
                if (prefab == null)
                    throw new IOException("Unity could not save the prefab asset.");
                AssetDatabase.SaveAssets();
                return prefabPath;
            }
            catch
            {
                AssetDatabase.DeleteAsset(meshPath);
                throw;
            }
        }
    }

    public static class PlyMeshGameObjectFactory
    {
        public static GameObject Create(Mesh mesh, string objectName)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            GameObject gameObject = new GameObject(string.IsNullOrWhiteSpace(objectName) ? mesh.name : objectName);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>();
            return gameObject;
        }
    }
}
