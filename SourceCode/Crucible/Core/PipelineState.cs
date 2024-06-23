using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SharpDX.DXGI;
using SharpDX.Direct3D12;
using SharpDX.D3DCompiler;
using D12Device = SharpDX.Direct3D12.Device;
using D12ByteCode = SharpDX.Direct3D12.ShaderBytecode;
using DCByteCode = SharpDX.D3DCompiler.ShaderBytecode;

namespace Crucible;

public class PipelineConfig
{
  public readonly PipelineState pso;
  public readonly RootSignature sign;
  public readonly int vsTexCount;
  public readonly int psTexCount;
  public readonly int rtCount;

  public PipelineConfig(PipelineState state, RootSignature _sign, int _rtCount, int _vsTexCount, int _psTexCount)
  {
    pso = state;
    sign = _sign;
    rtCount = _rtCount;
    vsTexCount = _vsTexCount;
    psTexCount = _psTexCount;
  }
}

public static class PipelineStateManager
{
  private static class InputLayoutManager
  {
    static readonly Dictionary<Type, InputLayoutDescription> layouts = new Dictionary<Type, InputLayoutDescription>
    (
      [
        new KeyValuePair<Type, InputLayoutDescription>(typeof(Vertex), new InputElement[]
        {
          new InputElement("POSITION", 0, Format.R32G32B32_Float, 0),
          new InputElement("NORMAL", 0, Format.R32G32B32_Float, 0),
          new InputElement("TANGENT", 0, Format.R32G32B32_Float, 0),
          new InputElement("TEXCOORD", 0, Format.R32G32_Float, 0)
        }),
        new KeyValuePair<Type, InputLayoutDescription>(typeof(UIVertex), new InputElement[]
        {
          new InputElement("POSITION", 0, Format.R32G32_Float, 0),
          new InputElement("TEXCOORD", 0, Format.R32G32_Float, 0),
        })
      ]
    );

    public static InputLayoutDescription GetLayout(Type type) => layouts[type];

    public static Type GetLayoutType(ShaderReflection vsReflection)
    {
      // Get input elements of the VS
      int vsInputCount = vsReflection.Description.InputParameters;
      List<string> semantics = new List<string>();
      for (int i = 0; i < vsInputCount; i++)
      {
        string semantic = vsReflection.GetInputParameterDescription(i).SemanticName;
        // Ignore system semantics which are produced automatically.
        if (semantic.Contains("SV_")) continue;
        semantics.Add(semantic);
      }
      vsInputCount = semantics.Count();
      // Search for a matched layout
      foreach (var pair in layouts)
      {
        InputElement[] elements = pair.Value.Elements;
        if (elements.Count() != vsInputCount) continue;
        for (int i = 0; i < vsInputCount; i++)
        {
          if (elements[i].SemanticName != semantics[i]) break;
          // Find a matching layout
          if (i == vsInputCount - 1) return pair.Key;
        }
      }
      throw new ArgumentException("Failed to find a matching input layout for a shader.");
    }
  }

  /// <summary>
  /// Help D3DCompiler to open HLSL header.<br/>
  /// Only support relative location for "#include" currently.
  /// </summary>
  public class HLSLInclude : Include
  {
    public IDisposable Shadow { get; set; }

    public void Dispose() => Shadow?.Dispose();

    public Stream Open(IncludeType type, string fileName, Stream parentStream)
    {
      string includeDir = Path.Combine(PathHelper.GetPath("Shaders"), fileName);
      return new FileStream(includeDir, FileMode.Open, FileAccess.Read);
    }

    public void Close(Stream stream) => stream?.Dispose();
  }

  private static readonly StaticSamplerDescription[] commonSamplers = new StaticSamplerDescription[]
  {
    new StaticSamplerDescription() // Point-Clamp (Post-processing)
    {
      ShaderRegister = 0,
      MaxLOD = 0,
      Filter = Filter.MinMagMipPoint,
      AddressUVW = TextureAddressMode.Clamp,
      ShaderVisibility = ShaderVisibility.All,
    },
    new StaticSamplerDescription() // Trilinear-Clamp (Post-processing / UI)
    {
      ShaderRegister = 1,
      MaxLOD = float.MaxValue,
      Filter = Filter.MinMagMipLinear,
      AddressUVW = TextureAddressMode.Clamp,
      ShaderVisibility = ShaderVisibility.All,
    },
    new StaticSamplerDescription() // Trilinear-Wrap (Post-processing / UI)
    {
      ShaderRegister = 2,
      MaxLOD = float.MaxValue,
      Filter = Filter.MinMagMipLinear,
      AddressUVW = TextureAddressMode.Clamp,
      ShaderVisibility = ShaderVisibility.All,
    },
    new StaticSamplerDescription() // Anisotropic-Clamp (Mesh)
    {
      ShaderRegister = 3,
      MaxLOD = float.MaxValue,
      Filter = Filter.Anisotropic,
      AddressUVW = TextureAddressMode.Wrap,
      ShaderVisibility = ShaderVisibility.All,
      MaxAnisotropy = AppConstants.AnisotropyLevel
    },
    new StaticSamplerDescription() // Anisotropic-Wrap (Mesh)
    {
      ShaderRegister = 4,
      MaxLOD = float.MaxValue,
      Filter = Filter.Anisotropic,
      AddressUVW = TextureAddressMode.Wrap,
      ShaderVisibility = ShaderVisibility.All,
      MaxAnisotropy = AppConstants.AnisotropyLevel
    },
    new StaticSamplerDescription() // LessEqual-PCF-Comparison (Shadow)
    {
      ShaderRegister = 5,
      MaxLOD = 0,
      Filter = Filter.ComparisonMinMagLinearMipPoint,
      AddressUVW = TextureAddressMode.Clamp,
      ShaderVisibility = ShaderVisibility.All,
      ComparisonFunc = Comparison.LessEqual,
    }
  };

  private static Dictionary<string, PipelineConfig> configs = new Dictionary<string, PipelineConfig>();

  public static PipelineConfig GetPipelineConfig(string name) => configs[name];

  public static void Initialize(D12Device device)
  {
    LoadNewPSO(device, "NoLit.hlsl", true, true, false);
    LoadNewPSO(device, "UI.hlsl", false, false, true);
    LoadNewPSO(device, "GBuffer.hlsl", true, true, false);
  }

  static void LoadNewPSO(D12Device device, string fileName, bool zTest, bool zWrite, bool blend)
  {
    CompileShader(out D12ByteCode vs, out D12ByteCode ps, fileName);
    GetShaderInputInfo(out Type layoutType, out int rtCount, out int vsTexCount, out int psTexCount, vs, ps);
    CreateSignature(out RootSignature rootSignature, device, vsTexCount, psTexCount);
    CreatePSO(out PipelineState pso, device, rootSignature, layoutType, vs, ps, rtCount, zTest, zWrite, blend);
    configs.Add(fileName, new PipelineConfig(pso, rootSignature, rtCount, vsTexCount, psTexCount));
  }

  static void CompileShader(out D12ByteCode vs, out D12ByteCode ps, string fileName)
  {
    if (Path.GetExtension(fileName) != ".hlsl")
      throw new ArgumentException($"\"{fileName}\" is not a hlsl file.");
    ShaderFlags shaderFlags = ShaderFlags.PackMatrixRowMajor;
#if DEBUG
    shaderFlags |= ShaderFlags.Debug | ShaderFlags.OptimizationLevel0;
#else
    shaderFlags |= ShaderFlags.OptimizationLevel3;
#endif
    HLSLInclude include = new HLSLInclude();
    string path = PathHelper.GetPath(Path.Combine("Shaders", fileName));

    Func<string, string, CompilationResult> Compile = (string entryPoint, string profile) =>
      DCByteCode.CompileFromFile(path, entryPoint, profile, shaderFlags, (EffectFlags)0, null, include);
    var vsResult = Compile("VS", "vs_5_0");
    var psResult = Compile("PS", "ps_5_0");
    vs = new D12ByteCode((byte[])vsResult);
    ps = new D12ByteCode((byte[])psResult);
    include.Dispose();
    vsResult.Dispose();
    psResult.Dispose();
  }

  static void GetShaderInputInfo(out Type layoutType, out int rtCount, out int vsTexCount, out int psTexCount, in D12ByteCode vs, in D12ByteCode ps)
  {
    var vsReflection = new ShaderReflection(vs.Buffer);
    var psReflection = new ShaderReflection(ps.Buffer);
    layoutType = InputLayoutManager.GetLayoutType(vsReflection);
    rtCount = psReflection.Description.OutputParameters;
    vsTexCount = GetSRVCount(vsReflection);
    psTexCount = GetSRVCount(psReflection);
    vsReflection.Dispose();
    psReflection.Dispose();
  }
  static int GetSRVCount(ShaderReflection reflection)
  {
    var srvs = new List<int>();
    int resourceCount = reflection.Description.BoundResources;
    for (int i = 0; i < resourceCount; i++)
    {
      var resource = reflection.GetResourceBindingDescription(i);
      if (resource.Type != ShaderInputType.Texture) continue;
      if (srvs.Contains(resource.BindPoint)) continue;
      srvs.Add(resource.BindPoint);
    }
    // Check the register indices of textures
    int count = srvs.Count;
    if (count > 0 && srvs.Max() != count - 1) throw new ArgumentException("SRV register numbers are not successive. See comments in the \"Common.hlsl\".");
    return count;
  }

  static void CreateSignature(out RootSignature rootSignature, D12Device dx12Device, int vsTexCount, int psTexCount)
  {
    List<RootParameter> rootParams = new List<RootParameter>();
    // The root parameter is the smallest unit when updated, thus use two root params for each CBV.
    rootParams.Add(new RootParameter(ShaderVisibility.All, new RootDescriptor(0, 0), RootParameterType.ConstantBufferView));
    rootParams.Add(new RootParameter(ShaderVisibility.All, new RootDescriptor(1, 0), RootParameterType.ConstantBufferView));
    if (vsTexCount > 0)
    {
      for(int i = 0;i < vsTexCount;i++)
      {
        // Multiple SRVs in one root table must be continuously, but we want to set each one seperately,
        // Thus we make a root table for each SRV.
        var vsTextures = new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, i);
        rootParams.Add(new RootParameter(ShaderVisibility.Vertex, new DescriptorRange[] { vsTextures }));
      }
    }
    if (psTexCount > 0)
    {
      for (int i = 0; i < psTexCount; i++)
      {
        var psTextures = new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, i);
        rootParams.Add(new RootParameter(ShaderVisibility.Pixel, new DescriptorRange[] { psTextures }));
      }
    }
    var flags = RootSignatureFlags.AllowInputAssemblerInputLayout;
    var rootSignatureDesc = new RootSignatureDescription(flags, rootParams.ToArray(), commonSamplers);
    rootSignature = dx12Device.CreateRootSignature(rootSignatureDesc.Serialize());
  }

  static void CreatePSO(out PipelineState pso, D12Device device, RootSignature sign, Type layoutType, in D12ByteCode vs, in D12ByteCode ps, int RTVCount, bool zTest, bool zWrite, bool blend)
  {
    var dsState = DepthStencilStateDescription.Default();
    dsState.DepthWriteMask = zWrite ? DepthWriteMask.All : DepthWriteMask.Zero;
    dsState.DepthComparison = Comparison.LessEqual;
    dsState.IsDepthEnabled = zTest;
    var blendState = new BlendStateDescription() { AlphaToCoverageEnable = false, IndependentBlendEnable = false };
    blendState.RenderTarget[0] = new RenderTargetBlendDescription()
    {
      IsBlendEnabled = blend,
      LogicOpEnable = false,
      SourceBlend = BlendOption.SourceAlpha,
      DestinationBlend = BlendOption.InverseSourceAlpha,
      BlendOperation = BlendOperation.Add,
      SourceAlphaBlend = BlendOption.Zero,
      DestinationAlphaBlend = BlendOption.Zero,
      AlphaBlendOperation = BlendOperation.Add,
      RenderTargetWriteMask = ColorWriteMaskFlags.Red | ColorWriteMaskFlags.Green | ColorWriteMaskFlags.Blue
    };
    var psoDesc = new GraphicsPipelineStateDescription()
    {
      RootSignature = sign,
      VertexShader = vs,
      PixelShader = ps,
      StreamOutput = new StreamOutputDescription(),
      BlendState = blendState,
      SampleMask = ~0,
      RasterizerState = RasterizerStateDescription.Default(),
      DepthStencilState = dsState,
      InputLayout = InputLayoutManager.GetLayout(layoutType),
      PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
      RenderTargetCount = RTVCount,
      DepthStencilFormat = Format.D32_Float,
      SampleDescription = new SampleDescription(1, 0)
    };
    for (int i = 0; i < RTVCount; i++)
    {
      psoDesc.RenderTargetFormats[i] = Format.R8G8B8A8_UNorm;
    }
    pso = device.CreateGraphicsPipelineState(psoDesc);
  }
}