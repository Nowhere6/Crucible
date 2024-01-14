using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;
using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D12;
using static SharpD12.AppConstants;
using Device = SharpDX.Direct3D12.Device;
using Resource = SharpDX.Direct3D12.Resource;

namespace SharpD12
{
  public partial class SD12Engine
  {
    public SD12Engine(CustomedForm winForm)
    {
      form = winForm; 
      width = form.DrawingPanel.Width;
      height = form.DrawingPanel.Height;
      form.InputEvent += Input.PerMessageProcess;
      syncEventHandle = syncEvent.SafeWaitHandle.DangerousGetHandle();
      viewPort = new ViewportF(0, 0, width, height);
      scissorRectangle = new Rectangle(0, 0, width, height);
      CreateDX12Device();
      fence = dx12Device.CreateFence(0, FenceFlags.None);
      rtv_size = dx12Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
      dsv_size = dx12Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
      csu_size = dx12Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
    }

    /// <summary>
    /// Create dx12 fundamental objects.
    /// </summary>
    void CreateDX12Device()
    {
#if DEBUG
      // Enable the D3D12 debug layer.
      DebugInterface.Get().EnableDebugLayer();
#endif
      try
      {
        try
        {
          dx12Device = new Device(null, DX12FeatureLevel);
        }
        catch
        {
          MessageBox.Show("Can't create ID3D12Device for GPU, use warp instead.", "Fatal Error", MessageBoxButtons.OK);
          dx12Device = new Device(factory.GetWarpAdapter(), DX12FeatureLevel);
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
      CreateFrames();
      PiplelineConfig();
      LoadTextures();
      UI.BitFont.Reinitialize(dx12Device, PathHelper.GetPath("Font"));
      BuildRenderItems();
    }

    void CreateQueueAndChain()
    {
      commandQueue = dx12Device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
      var description = new SwapChainDescription()
      {
        IsWindowed = true,
        OutputHandle = form.DrawingPanel.Handle,
        BufferCount = SwapChainSize,
        Usage = Usage.RenderTargetOutput,
        SwapEffect = SwapEffect.FlipDiscard,
        Flags = SwapChainFlags.AllowTearing,
        SampleDescription = new SampleDescription(1, 0),
        ModeDescription = new ModeDescription(Format.R8G8B8A8_UNorm),
      };
      swapChain = new SwapChain(factory, commandQueue, description);
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
      var rtvDesc = new RenderTargetViewDescription { Format = Format.R8G8B8A8_UNorm, Dimension = RenderTargetViewDimension.Texture2D };
      foreach (int i in Enumerable.Range(0, SwapChainSize))
      {
        frames[i] = new FrameResource();
        frames[i].backBuffer = swapChain.GetBackBuffer<Resource>(i);
        frames[i].backBufferHandle = FrameResource.rtvDescHeap.CPUDescriptorHandleForHeapStart + i * RTVSize;
        frames[i].cmdAllocator = dx12Device.CreateCommandAllocator(CommandListType.Direct);
        dx12Device.CreateRenderTargetView(frames[i].backBuffer, rtvDesc, frames[i].backBufferHandle);
      }

      // Create depth buffer and DSV.
      FrameResource.dsvHandle = FrameResource.dsvDescHeap.CPUDescriptorHandleForHeapStart;
      var depthDesc = ResourceDescription.Texture2D(Format.R32_Typeless, width, height, 1, 1, 1, 0, ResourceFlags.AllowDepthStencil | ResourceFlags.DenyShaderResource);
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
      // Build srv descriptor heap.
      SRV_Heap.Initialize(dx12Device);

      // Create constant buffer and pass cbv.
      FrameResource.passBuffer = new UploadBuffer<SuperPassConsts>(dx12Device, SwapChainSize, true);
      FrameResource.staticRenderItemObjectBuffer = new UploadBuffer<SuperObjectConsts>(dx12Device, MaxStaticRenderItems * SwapChainSize, true);
      FrameResource.uiRenderItemObjectBuffer = new UploadBuffer<SuperObjectConsts>(dx12Device, MaxUIRenderItems * SwapChainSize, true);

      PSO.ReInitialize(dx12Device, PathHelper.GetPath("Shaders"));
    }

    void LoadTextures()
    {
      Texture.Load_PNG_RGBA32_AutoMip(dx12Device, PathHelper.GetPath(@"Textures\Default.png"), "Default");
    }

    void BuildRenderItems()
    {
      staticRenderItems = new List<StaticRenderItem>();
      uiRenderItems = new List<UIRenderItem>();

      // Create one render item.
      var renderItem = new StaticRenderItem();
      staticRenderItems.Add(renderItem);
      renderItem.objectConst = new SuperObjectConsts { world = Matrix.Identity };
      //renderItem.mesh = StaticMesh.CreateBox(dx12Device, 1, 1, 1);
      renderItem.mesh = MeshManager.LoadExternalModel(dx12Device, "dragon.obj");
      renderItem.albedoTex = "Default";

      // Temp
      var uiRenderItem = new UIRenderItem();
      uiRenderItems.Add(uiRenderItem);
      uiRenderItem.objectConst = new SuperObjectConsts { color = Vector4.One };
      uiRenderItem.mesh = MeshManager.MakeSimpleTextMesh(dx12Device, $"Hello World! \nOriginal Font Size={UI.BitFont.FontSize}");
      uiRenderItem.tex = UI.BitFont.Name;
    }
  }
}
