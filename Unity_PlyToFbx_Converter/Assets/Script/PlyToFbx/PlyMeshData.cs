using UnityEngine;

namespace PlyToFbx
{
    public sealed class PlyMeshData
    {
        public Vector3[] Vertices { get; }
        public int[] Triangles { get; }

        public PlyMeshData(Vector3[] vertices, int[] triangles)
        {
            Vertices = vertices;
            Triangles = triangles;
        }
    }
}
