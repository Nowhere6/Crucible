using SharpDX;
using SharpDX.DXGI;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace SharpD12
{
  using SharpDX.Direct3D12;
  using SharpDX.Windows;
  using System.Collections.Generic;
  using System.IO;
  using static AppConstants;

  public partial class SD12Engine
  {
    public SD12Engine(CustomedForm form)
    {
      this.form = form; 
      this.width = form.Width;
      this.height = form.Height;
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
      BuildRenderItems();
    }

    void CreateQueueAndChain()
    {
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
      FrameResource.srvDescHeap = dx12Device.CreateDescriptorHeap(cbvHeapDesc);

      // Create constant buffer and pass cbv.
      FrameResource.passBuffer = new UploadBuffer<PassConstants>(dx12Device, SwapChainSize, true);
      FrameResource.objectBuffer = new UploadBuffer<ObjectConstants>(dx12Device, MaxRenderItems * SwapChainSize, true);

      PSO.ReInitialize(dx12Device, PathHelper.GetPath("Shaders"));
    }

    void LoadTextures()
    {
      TextureManager.LoadPNG(dx12Device, PathHelper.GetPath(@"Textures\Default.png"), "DefaultTexture");
    }

    void BuildRenderItems()
    {
      renderItems = new List<RenderItem>();
      renderItems.Capacity = MaxRenderItems;

      // Create one render item.
      var renderItem = new RenderItem();
      renderItems.Add(renderItem);
      renderItem.objectConst = new ObjectConstants { world = Matrix.Identity };
      //renderItem.mesh = StaticMesh.CreateBox(dx12Device, 1, 1, 1);
      renderItem.mesh = StaticMesh.LoadOBJ(dx12Device, @"Models\stanford-bunny.obj");
    }
  }
}
