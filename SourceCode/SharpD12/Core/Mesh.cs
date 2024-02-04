using SharpDX;
using SharpDX.DXGI;
using Assimp;

namespace SharpD12
{
  using Assimp.Unmanaged;
  using SharpDX.Direct3D12;
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.Drawing;
  using System.IO;
  using System.Linq;

  public static class InputLayoutManager
  {
    public static readonly InputLayoutDescription Layout_Vertex =
      new InputElement[]
      {
        new InputElement("POSITION", 0, Format.R32G32B32_Float, 0),
        new InputElement("NORMAL", 0, Format.R32G32B32_Float, 0),
        new InputElement("TANGENT", 0, Format.R32G32B32_Float, 0),
        new InputElement("TEXCOORD", 0, Format.R32G32_Float, 0)
      };

    public static readonly InputLayoutDescription Layout_UIVertex =
      new InputElement[]
      {
        new InputElement("POSITION", 0, Format.R32G32_Float, 0),
        new InputElement("TEXCOORD", 0, Format.R32G32_Float, 0),
      };
  }

  public struct Vertex
  {
    public Vector3 position;
    public Vector3 normal;
    public Vector3 tangent;
    public Vector2 uv;

    public Vertex(ref Vector3 pos, ref Vector3 n, ref Vector3 t, ref Vector2 _uv)
    {
      position = pos;
      normal = n;
      tangent = t;
      uv = _uv;
    }

    public Vertex(params float[] v)
    {
      if (v.Length != 11)
        throw new System.ArgumentException("Incorrect arguments.");
      position = new Vector3(v[0], v[1], v[2]);
      tangent = new Vector3(v[6], v[7], v[8]);
      normal = new Vector3(v[3], v[4], v[5]);
      uv = new Vector2(v[9], v[10]);
    }
  }

  public struct UIVertex
  {
    public Vector2 pos;
    public Vector2 uv;

    public UIVertex(ref Vector2 pos, ref Vector2 uv)
    {
      this.pos = pos;
      this.uv = uv;
    }

    public UIVertex(params float[] v)
    {
      if (v.Length != 4)
        throw new System.ArgumentException("Incorrect arguments.");
      pos = new Vector2(v[0], v[1]);
      uv = new Vector2(v[3], v[4]);
    }
  }

  public static class MeshManager
  {
    private static AssimpContext assimpCtx = new AssimpContext();
    private static readonly PostProcessSteps flags =
      PostProcessSteps.Triangulate
      | PostProcessSteps.GenerateSmoothNormals
      | PostProcessSteps.CalculateTangentSpace
      | PostProcessSteps.MakeLeftHanded
      | PostProcessSteps.FlipWindingOrder
      | PostProcessSteps.FlipUVs;

    public static StaticMesh LoadExternalModel(Device device, string name)
    {
      Assimp.Scene scene = assimpCtx.ImportFile(PathHelper.GetPath(Path.Combine("Models", name)), flags);
      if (scene == null)
        throw new FileLoadException();

      var mesh = scene.Meshes[0];
      int vCount = mesh.VertexCount;
      int iCount = mesh.FaceCount * 3;
      Vertex[] vertices = new Vertex[vCount];
      uint[] indices = new uint[iCount];

      foreach (int i in Enumerable.Range(0, vCount))
      {
        vertices[i].position = Vector3.Multiply(new Vector3(mesh.Vertices[i].X, mesh.Vertices[i].Y, mesh.Vertices[i].Z), 0.1f);
        vertices[i].normal = new Vector3(mesh.Normals[i].X, mesh.Normals[i].Y, mesh.Normals[i].Z);
        //vertices[i].tangent = new Vector3(mesh.Tangents[i].X, mesh.Tangents[i].Y, mesh.Tangents[i].Z);
        vertices[i].uv = new Vector2(vertices[i].position.X, 1 - vertices[i].position.Y);
      }

      var idx = mesh.GetIndices();
      foreach (int i in Enumerable.Range(0, iCount))
      {
        indices[i] = (uint)(idx[i]);
      }

      return new StaticMesh(device, vertices, indices);
    }

    private static void ComputeSceneSize(Assimp.Scene scene, out int vCount, out int iCount)
    {
      vCount = 0;
      iCount = 0;
      Stack<Node> nodes = new Stack<Node>();
      nodes.Push(scene.RootNode);
      while (nodes.TryPop(out Node currNode))
      {
        // Manage stack
        foreach (Node child in currNode.Children)
        {
          nodes.Push(child);
        }
        // Compute
        foreach (int meshIndex in currNode.MeshIndices)
        {
          if (scene.Meshes[meshIndex].PrimitiveType != PrimitiveType.Triangle)
            continue;
          vCount += scene.Meshes[meshIndex].VertexCount;
          iCount += scene.Meshes[meshIndex].FaceCount;
        }
      }
    }

    public static StaticMesh CreateBox(Device device, float xHalf = 0.5f, float yHalf = 0.5f, float zHalf = 0.5f)
    {
      if (xHalf < 0 || yHalf < 0 || zHalf < 0)
        throw new System.ArgumentException("The half size of box can't be negative.");

      var v = new Vertex[]
      {
        // Front
        new Vertex(-xHalf,-yHalf,-zHalf, 0, 0,-1, 1, 0, 0, 0, 1),
        new Vertex(-xHalf, yHalf,-zHalf, 0, 0,-1, 1, 0, 0, 0, 0),
        new Vertex( xHalf, yHalf,-zHalf, 0, 0,-1, 1, 0, 0, 1, 0),
        new Vertex( xHalf,-yHalf,-zHalf, 0, 0,-1, 1, 0, 0, 1, 1),
        // Back
        new Vertex( xHalf,-yHalf, zHalf, 0, 0, 1,-1, 0, 0, 0, 1),
        new Vertex( xHalf, yHalf, zHalf, 0, 0, 1,-1, 0, 0, 0, 0),
        new Vertex(-xHalf, yHalf, zHalf, 0, 0, 1,-1, 0, 0, 1, 0),
        new Vertex(-xHalf,-yHalf, zHalf, 0, 0, 1,-1, 0, 0, 1, 1),
        // Left
        new Vertex(-xHalf,-yHalf, zHalf,-1, 0, 0, 0, 0,-1, 0, 1),
        new Vertex(-xHalf, yHalf, zHalf,-1, 0, 0, 0, 0,-1, 0, 0),
        new Vertex(-xHalf, yHalf,-zHalf,-1, 0, 0, 0, 0,-1, 1, 0),
        new Vertex(-xHalf,-yHalf,-zHalf,-1, 0, 0, 0, 0,-1, 1, 1),
        // Right
        new Vertex( xHalf,-yHalf,-zHalf, 1, 0, 0, 0, 0, 1, 0, 1),
        new Vertex( xHalf, yHalf,-zHalf, 1, 0, 0, 0, 0, 1, 0, 0),
        new Vertex( xHalf, yHalf, zHalf, 1, 0, 0, 0, 0, 1, 1, 0),
        new Vertex( xHalf,-yHalf, zHalf, 1, 0, 0, 0, 0, 1, 1, 1),
        // Top
        new Vertex(-xHalf, yHalf,-zHalf, 0, 1, 0, 1, 0, 0, 0, 1),
        new Vertex(-xHalf, yHalf, zHalf, 0, 1, 0, 1, 0, 0, 0, 0),
        new Vertex( xHalf, yHalf, zHalf, 0, 1, 0, 1, 0, 0, 1, 0),
        new Vertex( xHalf, yHalf,-zHalf, 0, 1, 0, 1, 0, 0, 1, 1),
        // Bottom 
        new Vertex(-xHalf,-yHalf, zHalf, 0, 0, 0, 0, 0, 0, 0, 1),
        new Vertex(-xHalf,-yHalf,-zHalf, 0, 0, 0, 0, 0, 0, 0, 0),
        new Vertex( xHalf,-yHalf,-zHalf, 0, 0, 0, 0, 0, 0, 1, 0),
        new Vertex( xHalf,-yHalf, zHalf, 0, 0, 0, 0, 0, 0, 1, 1),
      };
      var i = new uint[]
      {
         0, 1, 2, 0, 2, 3,
         4, 5, 7, 5, 6, 7,
         8, 9,10, 8,10,11,
        12,13,15,13,14,15,
        16,17,18,16,18,19,
        20,21,23,21,22,23,
      };

      return new StaticMesh(device, v, i);
    }

    public static UIMesh MakeSimpleTextMesh(Device dx12Device, string text)
    {
      var v = UI.BitFont.Text2Mesh(text);
      return new UIMesh(dx12Device, v);
    }
  }

  public class StaticMesh : IDisposable
  {
    // Vertex buffer.
    readonly int vertexCount;
    public int VertexCount { get => vertexCount; }
    public readonly Vertex[] vertices;
    DefaultBuffer<Vertex> vertexBuffer;
    public readonly VertexBufferView vertexBufferView;

    // Index buffer.
    readonly int indexCount;
    public int IndexCount { get => indexCount; }
    public readonly uint[] indices;
    DefaultBuffer<uint> indexBuffer;
    public readonly IndexBufferView indexBufferView;

    public StaticMesh(Device device, Vertex[] _vertices, uint[] _indices)
    {
      // Build vertex buffer.
      vertices = _vertices;
      vertexCount = vertices.Length;
      vertexBuffer = new DefaultBuffer<Vertex>(device, vertexCount * Utilities.SizeOf<Vertex>(), BufferDataType.VBIB, true);
      vertexBuffer.Write(0, vertices);
      vertexBufferView = new VertexBufferView()
      {
        BufferLocation = vertexBuffer.defaultHeap.GPUVirtualAddress,
        StrideInBytes = Utilities.SizeOf<Vertex>(),
        SizeInBytes = vertexBuffer.Size
      };

      // Build index buffer.
      indices = _indices;
      indexCount = indices.Length;
      indexBuffer = new DefaultBuffer<uint>(device, indexCount * Utilities.SizeOf<uint>(), BufferDataType.VBIB, true);
      indexBuffer.Write(0, indices);
      indexBufferView = new IndexBufferView()
      {
        BufferLocation = indexBuffer.defaultHeap.GPUVirtualAddress,
        SizeInBytes = indexBuffer.Size,
        Format = Format.R32_UInt
      };
    }

    public void Dispose()
    {
      vertexBuffer.Dispose();
      indexBuffer.Dispose();
    }
  }

  // TODO: Temporary implementation
  public class UIMesh : IDisposable
  {
    // Vertex buffer.
    public int VertexCount { get => vertexCount; }
    public readonly VertexBufferView vertexBufferView;
    public readonly UIVertex[] vertices;
    UploadBuffer<UIVertex> vertexBuffer;
    readonly int vertexCount;

    public UIMesh(Device device, UIVertex[] _vertices)
    {
      // Build vertex buffer.
      vertices = _vertices;
      vertexCount = vertices.Length;
      vertexBuffer = new UploadBuffer<UIVertex>(device, vertexCount, false);
      vertexBuffer.Write(0, vertices);
      vertexBufferView = new VertexBufferView()
      {
        BufferLocation = vertexBuffer.GetGPUAddress(),
        StrideInBytes = vertexBuffer.ElementSize,
        SizeInBytes = vertexBuffer.Size
      };
    }

    public void Dispose() => vertexBuffer.Dispose();
  }
}
