using SharpDX;
using SharpDX.DXGI;

namespace SharpD12
{
  using SharpDX.Direct3D12;

  public class StaticMesh
  {
    // Vertex buffer.
    public int VertexCount { get => vertexCount; }
    public readonly VertexBufferView vertexBufferView;
    public readonly Vertex[] vertices;
    UploadBuffer<Vertex> vertexBuffer;
    readonly int vertexCount;

    // Index buffer.
    public int IndexCount { get => indexCount; }
    public readonly IndexBufferView indexBufferView;
    public readonly uint[] indices;
    UploadBuffer<uint> indexBuffer;
    readonly int indexCount;

    public StaticMesh(Device device, ref Vertex[] _vertices, ref uint[] _indices)
    {
      // Build vertex buffer.
      vertices = _vertices;
      vertexCount = vertices.Length;
      vertexBuffer = new UploadBuffer<Vertex>(device, vertexCount, false);
      vertexBuffer.Write(0, vertices);
      vertexBufferView = new VertexBufferView()
      {
        BufferLocation = vertexBuffer.GetGPUAddress(),
        StrideInBytes = vertexBuffer.ElementSize,
        SizeInBytes = vertexBuffer.Size
      };

      // Build index buffer.
      indices = _indices;
      indexCount = indices.Length;
      indexBuffer = new UploadBuffer<uint>(device, indexCount, false);
      indexBuffer.Write(0, indices);
      indexBufferView = new IndexBufferView()
      {
        BufferLocation = indexBuffer.GetGPUAddress(),
        SizeInBytes = indexBuffer.Size,
        Format = Format.R32_UInt
      };
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

      return new StaticMesh(device, ref v, ref i);
    }
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
        throw new System.ArgumentException("Arguments arem't enough.");

      position = new Vector3(v[0], v[1], v[2]);
      normal = new Vector3(v[3], v[4], v[5]);
      tangent = new Vector3(v[6], v[7], v[8]);
      uv = new Vector2(v[9], v[10]);
    }
  }

  public static class StandardInputLayout
  {
    public static readonly InputLayoutDescription value = new InputElement[]
    {
        new InputElement("POSITION", 0, Format.R32G32B32_Float, 0),
        new InputElement("NORMAL", 0, Format.R32G32B32_Float, 0),
        new InputElement("TANGENT", 0, Format.R32G32B32_Float, 0),
        new InputElement("TEXCOORD", 0, Format.R32G32_Float, 0)
    };
  }
}
