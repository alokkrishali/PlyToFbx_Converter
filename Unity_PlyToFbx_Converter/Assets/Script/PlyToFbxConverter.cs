using System;
using System.IO;
using UnityEngine;

namespace PlyToFbx
{
    public sealed class PlyToFbxConverter
    {
        private readonly IPointCloudReader meshReader;
        private readonly PlyMeshFactory meshFactory;

        public PlyToFbxConverter(IPointCloudReader meshReader, PlyMeshFactory meshFactory)
        {
            this.meshReader = meshReader ?? throw new ArgumentNullException(nameof(meshReader));
            this.meshFactory = meshFactory ?? throw new ArgumentNullException(nameof(meshFactory));
        }

        public Mesh Convert(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A point-cloud file path is required.", nameof(filePath));

            PlyMeshData meshData = meshReader.Read(filePath);
            string meshName = Path.GetFileNameWithoutExtension(filePath);
            return meshFactory.Create(meshData, meshName);
        }
    }
}
