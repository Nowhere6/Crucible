using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.Direct3D12;
using SharpDX.DXGI;
using System.Collections.Generic;
using System.Windows.Documents;
using static SharpDX.DirectWrite.GdiInterop;
using System.IO;
using SharpDX.D3DCompiler;
using ShaderBytecode = SharpDX.Direct3D12.ShaderBytecode;

namespace SharpD12
{
  public enum PSOType : int
  {
    PLACEHOLDER,
  }

  public static class PSO
  {
    protected struct PipelineConfig
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
        new RootParameter(ShaderVisibility.Vertex, new RootDescriptor(0, 0), RootParameterType.ConstantBufferView),
        new RootParameter(ShaderVisibility.Vertex, new RootDescriptor(1, 0), RootParameterType.ConstantBufferView),
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
        InputLayout = StandardInputLayout.value,
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
    }
  }
}