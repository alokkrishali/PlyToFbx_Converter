using System;
using System.IO;

namespace PlyToFbx
{
    public interface IPointCloudReader
    {
        PlyMeshData Read(string filePath);
    }

    public sealed class PointCloudFileReader : IPointCloudReader
    {
        private readonly IPointCloudReader plyReader = new PlyFileReader();
        private readonly IPointCloudReader pcdReader = new PcdFileReader();

        public PlyMeshData Read(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            if (string.Equals(extension, ".ply", StringComparison.OrdinalIgnoreCase))
                return plyReader.Read(filePath);
            if (string.Equals(extension, ".pcd", StringComparison.OrdinalIgnoreCase))
                return pcdReader.Read(filePath);
            throw new NotSupportedException("Only .ply and .pcd point-cloud files are supported.");
        }
    }
}
