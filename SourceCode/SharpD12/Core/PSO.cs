using System;
using System.IO;
using System.Collections.Generic;
using SharpDX.DXGI;
using SharpDX.Direct3D12;
using SharpDX.D3DCompiler;
using ShaderBytecode = SharpDX.Direct3D12.ShaderBytecode;

namespace SharpD12
{
  public enum PSOType : int
  {
    PLACEHOLDER,
    UI
  }

  public static class PSO
  {
    protected class PipelineConfig
    {
      public PipelineState pso;
      public RootSignature rootSign;
    }

    private static List<PipelineConfig> configs;
    private static bool initialized = false;
#if DEBUG
    private static ShaderFlags shaderFlags = ShaderFlags.PackMatrixRowMajor | SharpDX.D3DCompiler.ShaderFlags.Debug;
#else
    private static ShaderFlags shaderFlags = ShaderFlags.PackMatrixRowMajor;
#endif
    private static EffectFlags effectFlags = EffectFlags.None;

    static PSO()
    {
      configs = new List<PipelineConfig>();
    }

    public static PipelineState GetPSO(PSOType psoType) => configs[(int)psoType].pso;

    public static RootSignature GetRootSign(PSOType psoType) => configs[(int)psoType].rootSign;

    public static void ReInitialize(SharpDX.Direct3D12.Device dx12Device, string shaderRootPath)
    {
      if (initialized)
      {
        foreach (PipelineConfig config in configs)
        {
          configs.Remove(config);
          config.rootSign.Dispose();
          config.pso.Dispose();
        }
      }

      initialized = true;
      HLSLInclude include = new HLSLInclude(shaderRootPath);

      // PSOType.PLACEHOLDER
      // Create root signature. Root parameter is the smallest unit when update.
      var srvTable = new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0);
      var rootParams = new RootParameter[]
      {
        new RootParameter(ShaderVisibility.All, new RootDescriptor(0, 0), RootParameterType.ConstantBufferView),
        new RootParameter(ShaderVisibility.All, new RootDescriptor(1, 0), RootParameterType.ConstantBufferView),
        new RootParameter(ShaderVisibility.Pixel, new DescriptorRange[] { srvTable })
      };
      var rootSignatureDesc = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout, rootParams, StandardSampler.value);
      RootSignature rootSignature = dx12Device.CreateRootSignature(rootSignatureDesc.Serialize());
      // Shaders and input layout.
      string shaderLoc = Path.Combine(shaderRootPath, "NoLit.hlsl");
      var vs = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile(shaderLoc, "VS", "vs_5_0", shaderFlags, effectFlags, null, include));
      var ps = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile(shaderLoc, "PS", "ps_5_0", shaderFlags, effectFlags, null, include));
      // Build pso.
      var psoDesc = new GraphicsPipelineStateDescription()
      {
        DepthStencilState = DepthStencilStateDescription.Default(),
        RasterizerState = RasterizerStateDescription.Default(),
        PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
        SampleDescription = new SampleDescription(1, 0),
        BlendState = BlendStateDescription.Default(),
        StreamOutput = new StreamOutputDescription(),
        InputLayout = InputLayoutManager.Layout_Vertex,
        DepthStencilFormat = Format.D32_Float,
        RootSignature = rootSignature,
        RenderTargetCount = 1,
        VertexShader = vs,
        PixelShader = ps,
        SampleMask = ~0,
      };
      psoDesc.RenderTargetFormats[0] = Format.R8G8B8A8_UNorm;
      PipelineState pso = dx12Device.CreateGraphicsPipelineState(psoDesc);
      configs.Add(new PipelineConfig { pso = pso, rootSign = rootSignature });

      // PSOType.UI
      shaderLoc = Path.Combine(shaderRootPath, "UI.hlsl");
      vs = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile(shaderLoc, "VS", "vs_5_0", shaderFlags, effectFlags, null, include));
      ps = new ShaderBytecode(SharpDX.D3DCompiler.ShaderBytecode.CompileFromFile(shaderLoc, "PS", "ps_5_0", shaderFlags, effectFlags, null, include));
      psoDesc.VertexShader = vs;
      psoDesc.PixelShader = ps;
      var depthState = DepthStencilStateDescription.Default();
      depthState.IsDepthEnabled = false;
      psoDesc.DepthStencilState = depthState;
      var blendState = BlendStateDescription.Default();
      var RT_BlendState = new RenderTargetBlendDescription()
      {
        IsBlendEnabled = true,
        SourceBlend = BlendOption.SourceAlpha,
        DestinationBlend = BlendOption.InverseSourceAlpha,
        BlendOperation = BlendOperation.Add,
        SourceAlphaBlend = (BlendOption)1,
        DestinationAlphaBlend = (BlendOption)1,
        AlphaBlendOperation = (BlendOperation)1,
        RenderTargetWriteMask = ColorWriteMaskFlags.Red | ColorWriteMaskFlags.Green | ColorWriteMaskFlags.Blue,
      };
      for(int i = 0; i < blendState.RenderTarget.Length; i++)
      {
        blendState.RenderTarget[i] = RT_BlendState;
      }
      psoDesc.BlendState = blendState;
      psoDesc.InputLayout = InputLayoutManager.Layout_UIVertex;
      pso = dx12Device.CreateGraphicsPipelineState(psoDesc);
      configs.Add(new PipelineConfig { pso = pso, rootSign = rootSignature });

    }
  }

  /// <summary>
  /// Help D3DCompiler to open HLSL header.<br/>
  /// Only support relative location for "#include" currently.
  /// </summary>
  public class HLSLInclude : SharpDX.D3DCompiler.Include
  {
    string rootDir;

    public IDisposable Shadow { get; set; }

    public HLSLInclude(string rootFolder) => rootDir = rootFolder;

    ~HLSLInclude() => Dispose();

    public void Close(Stream stream) => stream?.Dispose();

    public void Dispose() => Shadow?.Dispose();

    public Stream Open(IncludeType type, string fileName, Stream parentStream)
    {
      string includeDir = Path.Combine(rootDir, fileName);
      return new FileStream(includeDir, FileMode.Open, FileAccess.Read);
    }
  }
}