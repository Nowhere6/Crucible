using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;
using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D12;
using static Crucible.AppConstants;
using D12Device = SharpDX.Direct3D12.Device;
using D12Resource = SharpDX.Direct3D12.Resource;

namespace Crucible;

public partial class CrucibleEngine
{
  public CrucibleEngine()
  {
    form = new CustomedForm();
    form.SetClientSize(1920, 1080);
    form.SetLoopBody(LoopBody);
    width = form.ClientSize.Width;
    height = form.ClientSize.Height;
    form.SetInputEvent(Input.PerMessageProcess);
    viewPort = new ViewportF(0, 0, width, height);
    scissorRectangle = new Rectangle(0, 0, width, height);
    CreateDX12Device();
    rtv_size = dx12Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
    dsv_size = dx12Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
    csu_size = dx12Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
  }

  /// <summary>
  /// Create dx12 fundamental objects.
  /// </summary>
  void CreateDX12Device()
  {
    // Enable the D3D12 debug layer.
    if (enableDebugLayer) DebugInterface.Get().EnableDebugLayer();

    try
    {
      try
      {
        dx12Device = new D12Device(null, DX12FeatureLevel);
      }
      catch
      {
        MessageBox.Show("Can't create ID3D12Device for GPU, use warp instead.", "Fatal Error", MessageBoxButtons.OK);
        dx12Device = new D12Device(factory.GetWarpAdapter(), DX12FeatureLevel);
      }
    }
    catch
    {
      MessageBox.Show("Can't create ID3D12Device with warp neither! Game exits.", "Fatal Error", MessageBoxButtons.OK);
      System.Environment.Exit(-1);
    }
  }

  void EngineInitialize()
  {
    CreateQueueAndChain();
    CreateHeapsAndPSOs();
    CreateFrames();
    LoadTextures();
    UI.BitFont.Reinitialize(dx12Device, PathHelper.GetPath("Font"));
    BuildRenderItems();
  }

  void CreateQueueAndChain()
  {
    commandQueue = dx12Device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
    fence = new FenceSync(dx12Device, commandQueue, SwapChainSize);
    var description = new SwapChainDescription()
    {
      IsWindowed = true,
      OutputHandle = form.Handle,
      BufferCount = SwapChainSize,
      Usage = Usage.RenderTargetOutput,
      SwapEffect = SwapEffect.FlipDiscard,
      Flags = SwapChainFlags.AllowTearing,
      SampleDescription = new SampleDescription(1, 0),
      ModeDescription = new ModeDescription(Format.R8G8B8A8_UNorm)
    };
    swapChain = new SwapChain(factory, commandQueue, description);
  }

  void CreateHeapsAndPSOs()
  {
    // Build all descriptor heaps.
    DescHeapManager.Initialize(dx12Device);

    // Create constant buffer and pass cbv.
    FrameResource.passBuffer = new UploadBuffer<SuperPassConsts>(dx12Device, SwapChainSize, BufferType.ConstantBuffer);
    FrameResource.staticRenderItemObjectBuffer = new UploadBuffer<SuperObjectConsts>(dx12Device, MaxStaticRenderItems * SwapChainSize, BufferType.ConstantBuffer);
    FrameResource.uiRenderItemObjectBuffer = new UploadBuffer<SuperObjectConsts>(dx12Device, MaxUIRenderItems * SwapChainSize, BufferType.ConstantBuffer);

    PipelineStateManager.Initialize(dx12Device);
  }

  void CreateFrames()
  {
    // Create frame resources, pass resources, command allocator.
    RenderPassResource.Reinitialize(dx12Device, width, height);
    frames = new FrameResource[SwapChainSize];
    var rtvDesc = new RenderTargetViewDescription { Format = Format.R8G8B8A8_UNorm, Dimension = RenderTargetViewDimension.Texture2D };
    foreach (int i in Enumerable.Range(0, SwapChainSize))
    {
      frames[i] = new FrameResource();
      frames[i].backBuffer = swapChain.GetBackBuffer<D12Resource>(i);
      frames[i].rtvIndex = DescHeapManager.CreateView(dx12Device, frames[i].backBuffer, rtvDesc, ViewType.RTV);
      frames[i].cmdAllocator = dx12Device.CreateCommandAllocator(CommandListType.Direct);
    }

    // Create depth buffer and DSV.
    var depthDesc = ResourceDescription.Texture2D(Format.R32_Typeless, width, height, 1, 1, 1, 0, ResourceFlags.AllowDepthStencil | ResourceFlags.DenyShaderResource);
    var optimizedClear = new ClearValue { Format = Format.D32_Float, DepthStencil = new DepthStencilValue { Depth = 1.0f } };
    FrameResource.depthBuffer = dx12Device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None, depthDesc, ResourceStates.DepthWrite, optimizedClear);
    DepthStencilViewDescription dsvDesc = new DepthStencilViewDescription { Format = Format.D32_Float, Dimension = DepthStencilViewDimension.Texture2D };
    FrameResource.dsvIndex = DescHeapManager.CreateView(dx12Device, FrameResource.depthBuffer, dsvDesc, ViewType.DSV);

    // Create command list, which needs to be closed before reset.
    cmdList = dx12Device.CreateCommandList(CommandListType.Direct, frames[fence.FrameIndex].cmdAllocator, null);
    cmdList.Close();
  }

  void LoadTextures()
  {
    Texture.Load_RGBA32AutoMip(dx12Device, PathHelper.GetPath(@"Textures\Default.png"), "Default");
    Texture.Load_RGBA32AutoMip(dx12Device, PathHelper.GetPath(@"Textures\can_n.png"), "Normal");
  }

  void BuildRenderItems()
  {
    staticRenderItems = new List<StaticRenderItem>();
    uiRenderItems = new List<UIRenderItem>();

    // Create one render item.
    var renderItem = new StaticRenderItem();
    staticRenderItems.Add(renderItem);
    renderItem.objectConst = new SuperObjectConsts { world = Matrix.Identity };
    //renderItem.mesh = MeshManager.CreateBox(dx12Device, 1, 1, 1);
    // FRAME DROP because big upload heap.
    renderItem.mesh = MeshManager.LoadExternalModel(dx12Device, "can.fbx");
    renderItem.albedoTex = "Default";
    renderItem.normalTex = "Normal";

    // Temp
    var uiRenderItem = new UIRenderItem();
    uiRenderItems.Add(uiRenderItem);
    uiRenderItem.objectConst = new SuperObjectConsts { color = Vector4.One };
    uiRenderItem.mesh = MeshManager.MakeSimpleTextMesh(dx12Device, $"Hello World! \nOriginal Font Size={UI.BitFont.FontSize}");
    uiRenderItem.tex = UI.BitFont.Name;
  }
}