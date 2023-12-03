using SharpDX;
using SharpDX.DXGI;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SharpD12
{
  using SharpDX.Direct3D12;
  using SharpDX.Windows;
  using System.Collections.Generic;
  using System.IO;
  using System.Windows.Media.Imaging;
  using static ProgramDefinedConstants;

  public partial class SD12Engine
  {
    public SD12Engine(RenderForm form)
    {
      this.form = form; 
      this.width = form.Width;
      this.height = form.Height;
      clock = Stopwatch.StartNew();
      deltaclock = new Stopwatch();
    }

    void EngineInitialize()
    {
      CreateDX12BaseObjects();
      syncEventHandle = syncEvent.SafeWaitHandle.DangerousGetHandle();
      RTVSize = dx12Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
      DSVSize = dx12Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
      CBVSRVUAVSize = dx12Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
      CreateFrames();
      PiplelineConfig();
      LoadTextures();
      BuildRenderItems();
    }

    /// <summary>
    /// Create dx12 fundamental objects.
    /// </summary>
    void CreateDX12BaseObjects()
    {
#if DEBUG
      // Enable the D3D12 debug layer.
      DebugInterface.Get().EnableDebugLayer();
#endif
      var factory = new Factory4();
      try
      {
        try
        {
          dx12Device = new Device(null, DX12FeatureLevel);
        }
        catch
        {
          MessageBox.Show("Can't create ID3D12Device for GPU. Use warp instead.", "Fatal Error", MessageBoxButtons.OK);
          dx12Device = new Device(factory.GetWarpAdapter(), DX12FeatureLevel);
        }
      }
      catch
      {
        MessageBox.Show("Can't create ID3D12Device with warp neither! Game exits.", "Fatal Error", MessageBoxButtons.OK);
        System.Environment.Exit(-1);
      }
      commandQueue = dx12Device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
      var description = new SwapChainDescription()
      {
        IsWindowed = true,
        OutputHandle = form.Handle,
        BufferCount = SwapChainSize,
        Usage = Usage.RenderTargetOutput,
        SwapEffect = SwapEffect.FlipDiscard,
        Flags = SwapChainFlags.AllowModeSwitch | SwapChainFlags.AllowTearing,
        SampleDescription = new SampleDescription(1, 0),
        ModeDescription = new ModeDescription(Format.R8G8B8A8_UNorm),
      };
      swapChain = new SwapChain(factory, commandQueue, description);
      Utilities.Dispose<Factory4>(ref factory);

      viewPort = new ViewportF(0, 0, width, height);
      scissorRectangle = new Rectangle(0, 0, width, height);
      fence = dx12Device.CreateFence(0, FenceFlags.None);
    }

    void CreateFrames()
    {
      frames = new FrameResource[SwapChainSize];

      // Create rtv/dsv descriptor heap.
      var rtvHeapDesc = new DescriptorHeapDescription { Type = DescriptorHeapType.RenderTargetView, DescriptorCount = SwapChainSize };
      var dsvHeapDesc = new DescriptorHeapDescription { Type = DescriptorHeapType.DepthStencilView, DescriptorCount = 1 };
      FrameResource.rtvDescHeap = dx12Device.CreateDescriptorHeap(rtvHeapDesc);
      FrameResource.dsvDescHeap = dx12Device.CreateDescriptorHeap(dsvHeapDesc);

      // Create RTV and command allocator.
      var rtvDesc = new RenderTargetViewDescription { Format = Format.R8G8B8A8_UNorm_SRgb, Dimension = RenderTargetViewDimension.Texture2D };
      foreach (int i in Enumerable.Range(0, SwapChainSize))
      {
        frames[i] = new FrameResource();
        frames[i].backBuffer = swapChain.GetBackBuffer<Resource>(i);
        frames[i].rtvHandle = FrameResource.rtvDescHeap.CPUDescriptorHandleForHeapStart + i * RTVSize;
        frames[i].cmdAllocator = dx12Device.CreateCommandAllocator(CommandListType.Direct);
        dx12Device.CreateRenderTargetView(frames[i].backBuffer, rtvDesc, frames[i].rtvHandle);
      }

      // Create depth buffer and DSV.
      FrameResource.dsvHandle = FrameResource.dsvDescHeap.CPUDescriptorHandleForHeapStart;
      var depthDesc = ResourceDescription.Texture2D(Format.R32_Typeless, width, height, 1, 1, 1, 0, ResourceFlags.AllowDepthStencil);
      var optimizedClear = new ClearValue { Format = Format.D32_Float, DepthStencil = new DepthStencilValue { Depth = 1.0f } };
      FrameResource.depthBuffer = dx12Device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None, depthDesc, ResourceStates.DepthWrite, optimizedClear);
      DepthStencilViewDescription dsvDesc = new DepthStencilViewDescription { Format = Format.D32_Float, Dimension = DepthStencilViewDimension.Texture2D };
      dx12Device.CreateDepthStencilView(FrameResource.depthBuffer, dsvDesc, FrameResource.dsvHandle);

      // Create command list, which needs to be closed before reset.
      cmdList = dx12Device.CreateCommandList(CommandListType.Direct, frames[currFrameIdx].cmdAllocator, null);
      cmdList.Close();
    }

    void PiplelineConfig()
    {
      // Build cbv/srv/uav descriptor heap.
      var heapType = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView;
      var cbvHeapDesc = new DescriptorHeapDescription { Type = heapType, DescriptorCount = (1 + MaxRenderItems) * SwapChainSize + 1, Flags = DescriptorHeapFlags.ShaderVisible };
      FrameResource.cbvSrvUavDescHeap = dx12Device.CreateDescriptorHeap(cbvHeapDesc);

      // Create constant buffer and pass cbv.
      FrameResource.passBuffer = new UploadBuffer<PassConstants>(dx12Device, SwapChainSize, true);
      FrameResource.objectBuffer = new UploadBuffer<ObjectConstants>(dx12Device, MaxRenderItems * SwapChainSize, true);
      var CpuHandle0 = FrameResource.cbvSrvUavDescHeap.CPUDescriptorHandleForHeapStart;
      var GpuHandle0 = FrameResource.cbvSrvUavDescHeap.GPUDescriptorHandleForHeapStart;
      foreach (int i in Enumerable.Range(0, SwapChainSize))
      {
        frames[i].passCpuHandle = CpuHandle0 + i * CBVSRVUAVSize;
        frames[i].passGpuHandle = GpuHandle0 + i * CBVSRVUAVSize;
        frames[i].objectCpuHandle0 = CpuHandle0 + SwapChainSize * CBVSRVUAVSize + i * MaxRenderItems * CBVSRVUAVSize;
        frames[i].objectGpuHandle0 = GpuHandle0 + SwapChainSize * CBVSRVUAVSize + i * MaxRenderItems * CBVSRVUAVSize;
        var passCbvDesc = new ConstantBufferViewDescription { BufferLocation = FrameResource.passBuffer.GetGPUAddress(i), SizeInBytes = FrameResource.passBuffer.ElementSize };
        dx12Device.CreateConstantBufferView(passCbvDesc, frames[i].passCpuHandle);
      }

      // Create root signature. Root parameter is the smallest unit when update.
      var cbvTablePerPass = new DescriptorRange(DescriptorRangeType.ConstantBufferView, 1, 0);
      var cbvTablePerObject = new DescriptorRange(DescriptorRangeType.ConstantBufferView, 1, 1);
      var srvTable = new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0);
      var rootParams = new RootParameter[]
      {
        new RootParameter(ShaderVisibility.Vertex, new DescriptorRange[] { cbvTablePerPass }),
        new RootParameter(ShaderVisibility.Vertex, new DescriptorRange[] { cbvTablePerObject }),
        new RootParameter(ShaderVisibility.Pixel, new DescriptorRange[] { srvTable })
      };
      var rootSignatureDesc = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout, rootParams, StandardSampler.value);
      rootSignature = dx12Device.CreateRootSignature(rootSignatureDesc.Serialize());

      // Shaders and input layout.
      string shaderDir = PathHelper.GetPath("Shaders");
      string shaderLoc = Path.Combine(shaderDir, "NoLit.hlsl");
      var shaderFlags = SharpDX.D3DCompiler.ShaderFlags.PackMatrixRowMajor | SharpDX.D3DCompiler.ShaderFlags.Debug;
      var effectFlags = SharpDX.D3DCompiler.EffectFlags.None;
      var include = new HLSLInclude(shaderDir);
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
      psoDesc.RenderTargetFormats[0] = Format.R8G8B8A8_UNorm_SRgb;
      //psoDesc.RasterizerState.FillMode = FillMode.Wireframe;
      //psoDesc.RasterizerState.CullMode = CullMode.None;
      pso = dx12Device.CreateGraphicsPipelineState(psoDesc);
    }

    void LoadTextures()
    {
      TextureManager.LoadPNG(dx12Device, PathHelper.GetPath(@"Textures\Tex64.png"), "DefaultTexture");
    }

    void BuildRenderItems()
    {
      renderItems = new List<RenderItem>();

      // Create one render item.
      var renderItem = new RenderItem();
      renderItems.Add(renderItem);
      renderItem.objectConst = new ObjectConstants { world = Matrix.Identity };
      renderItem.mesh = StaticMesh.CreateBox(dx12Device, 1, 1, 1);

      // Create object cbv.
      foreach (int frameIndex in Enumerable.Range(0, SwapChainSize))
      {
        foreach (int itemIndex in Enumerable.Range(0, renderItems.Count))
        {
          CpuDescriptorHandle currentHandle = frames[frameIndex].objectCpuHandle0 + itemIndex * CBVSRVUAVSize;
          long gpuAddr = FrameResource.objectBuffer.GetGPUAddress(frameIndex * MaxRenderItems + itemIndex);
          var cbvDesc = new ConstantBufferViewDescription()
          {
            BufferLocation = gpuAddr,
            SizeInBytes = FrameResource.objectBuffer.ElementSize
          };
          dx12Device.CreateConstantBufferView(cbvDesc, currentHandle);
        }
      }
    }
  }
}
