# Unity Point Cloud Converter

A Unity Editor tool for importing point-cloud files, previewing them in the Editor, exporting them as FBX, and saving them as reusable prefabs.
<img width="959" height="523" alt="image" src="https://github.com/user-attachments/assets/63933588-66ea-433f-9f4c-e2b2d48c897a" />


## Features

- Import `.ply` files in ASCII or binary little-endian format.
- Import `.pcd` files in ASCII, binary, or binary-compressed format.
- Preview loaded point clouds in a dedicated Editor window.
- Export point clouds to FBX using Unity's FBX Exporter package.
- Save loaded point clouds as Unity prefabs with their mesh assets.
- Import polygon faces from PLY files and triangulate them for Unity meshes.

## Requirements

- Unity `6000.6.2f1` (or a compatible Unity 6 version).
- Unity FBX Exporter package `com.unity.formats.fbx` `5.1.6`. It is declared in `Packages/manifest.json` and should be installed by Unity Package Manager when the project opens.

## Getting Started

1. Clone this repository:

   ```bash
   git clone https://github.com/alokkrishali/Unity_PlyToFbx_Converter.git
   ```

2. Open the project folder `Unity_PlyToFbx_Converter` in Unity Hub.
3. Wait for Unity Package Manager to resolve project dependencies.
4. In the Unity Editor, open **Tools > 3D Tools > PLY to FBX Converter**.
5. Browse to a `.ply` or `.pcd` file, or enter its full path, then select **Load Point Cloud**.
6. Use **View Mesh**, **Convert to FBX**, or **Save as Prefab**.

## Point Cloud FBX Export

Unity's FBX Exporter does not export Unity point-topology meshes directly. For PLY/PCD point clouds, this tool therefore samples up to 50,000 points and represents each selected point as a small triangle-based marker in the exported FBX. The original point cloud is kept for preview and prefab creation. Polygon meshes with faces are exported using their mesh geometry.

The marker conversion preserves point positions approximately; marker size is derived from the cloud bounds. Very dense point clouds are sampled, so the FBX may not contain every source point.

## Project Structure

```text
Assets/Script/
  PlyToFbxConverter.cs          Conversion orchestration
  PlyToFbx/
    IPlyMeshReader.cs           Reader abstraction and extension dispatcher
    PlyFileReader.cs            PLY parser
    PcdFileReader.cs            PCD parser
    PlyMeshData.cs              Parsed point and face data
    PlyMeshFactory.cs           Unity mesh construction
    Editor/
      PlyToFbxWindow.cs         Converter window and user actions
      PlyMeshViewerWindow.cs    Mesh preview window
      PlyEditorServices.cs      FBX export and prefab persistence adapters
```

