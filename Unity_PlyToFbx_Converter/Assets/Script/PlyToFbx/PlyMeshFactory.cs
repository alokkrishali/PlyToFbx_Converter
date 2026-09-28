using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlyToFbx
{
    public sealed class PlyMeshFactory
    {
        public Mesh Create(PlyMeshData data, string meshName)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Vertices == null || data.Vertices.Length == 0)
                throw new InvalidOperationException("The PLY file contains no vertices.");

            Mesh mesh = new Mesh
            {
                name = string.IsNullOrWhiteSpace(meshName) ? "PLY Mesh" : meshName
            };

            if (data.Vertices.Length > ushort.MaxValue)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.vertices = data.Vertices;
            if (data.Triangles == null || data.Triangles.Length == 0)
            {
                int[] pointIndices = new int[data.Vertices.Length];
                for (int index = 0; index < pointIndices.Length; index++)
                    pointIndices[index] = index;
                mesh.SetIndices(pointIndices, MeshTopology.Points, 0);
            }
            else
            {
                mesh.triangles = data.Triangles;
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
