using System;
using System.Diagnostics;
using System.Threading;
using SharpDX;
using SharpDX.DXGI;
using SharpDX.Direct3D12;
using SharpDX.Windows;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using static Crucible.AppConstants;
using D12Device = SharpDX.Direct3D12.Device;
using D12Resource = SharpDX.Direct3D12.Resource;

namespace Crucible;

public partial class CrucibleEngine
{
  bool vSyncEnabled = true;
  bool enableDebugLayer = true;

  readonly Stopwatch gameClock = new Stopwatch();
  float lastTime = 0;
  float deltaTime = 0;

  Vector3 cameraPos = new Vector3(-3, 3, -3);
  Vector3 cameraZAxis = new Vector3(1, -1, 1);
  CustomedForm form;

  int width;
  int height;
  D12Device dx12Device;
  SwapChain swapChain;
  FrameResource[] frames;
  List<UIRenderItem> uiRenderItems;
  List<StaticRenderItem> staticRenderItems;
  FenceSync fence;
  CommandQueue commandQueue;
  GraphicsCommandList cmdList;
  Factory4 factory = new Factory4();

  ViewportF viewPort;
  Rectangle scissorRectangle;
  private static int rtv_size;
  private static int dsv_size;
  private static int csu_size;
  public static int RTVSize { get => rtv_size; }
  public static int DSVSize { get => dsv_size; }
  /// <summary> size of CBV or SRV or UAV </summary>
  public static int CSUSize { get => csu_size; }

  public void Run()
  {
    EngineInitialize();

    form.Icon = Properties.Resources.icon;
    form.Show();
    Input.Register(form.Handle);
    var loop = new RenderLoop(form);
    while (loop.NextFrame())
    {
      LoopBody();
    }
    Input.UnRegister();
    loop.Dispose();
  }

  void LoopBody()
  {
    Resize(); Update(); Render();
    fence.Synchronize(false);
  }

  /*void MemoryLeakExample()
  {
    // Memory leak example
    uiRenderItems[0].mesh = MeshManager.MakeSimpleTextMesh(dx12Device, new string('a', 1 << 15));
    // EXPLAINATION
    // Even we add finalizer which has dispose() into mesh class, ID3D12Resource stil can't be released correctly.
    // The only way to resolve this is to implement dispose() for ID3D12Resource container class and invoke dispose() manually.
    // But ID3D12Resource which you want to release may be used in previous frames, so delay release is required.

    // Memory leak fix example
    //DelayReleaseManager.Enqueue(fence.TargetFence, uiRenderItems[0]);
    //var uiRenderItem = new UIRenderItem();
    //uiRenderItem.objectConst = new SuperObjectConsts { color = Vector4.One };
    //uiRenderItem.mesh = MeshManager.MakeSimpleTextMesh(dx12Device, new string('a', 1 << 15));
    //uiRenderItem.tex = UI.BitFont.Name;
    //uiRenderItems[0] = uiRenderItem;
  }*/

  void Resize()
  {
    if (form.ClientSize.Width == width && form.ClientSize.Height == height) return;
    if (form.WindowState == FormWindowState.Minimized) return;

    width = form.ClientSize.Width;
    height = form.ClientSize.Height;
    viewPort.Width = width;
    viewPort.Height = height;
    scissorRectangle.Width = width;
    scissorRectangle.Height = height;
    // Finish all rendering tasks.
    fence.Synchronize(true);
    // Dispose old buffers.
    FrameResource.depthBuffer.Dispose();
    for (int i = 0; i < SwapChainSize; ++i)
      frames[i].backBuffer.Dispose();
    // Create new buffers.
    swapChain.ResizeBuffers(SwapChainSize, width, height, Format.R8G8B8A8_UNorm, SwapChainFlags.AllowTearing);
    var rtvDesc = new RenderTargetViewDescription { Format = Format.R8G8B8A8_UNorm, Dimension = RenderTargetViewDimension.Texture2D };
    for (int resIndex = 0; resIndex < SwapChainSize; ++resIndex)
    {
      // Make sure current frame has first buffer in swapchain.
      int frameIndex = (fence.FrameIndex + resIndex) % SwapChainSize;
      frames[frameIndex].backBuffer = swapChain.GetBackBuffer<D12Resource>(resIndex);
      DescHeapManager.RemoveView(frames[frameIndex].rtvIndex, ViewType.RTV);
      frames[frameIndex].rtvIndex = DescHeapManager.CreateView(dx12Device, frames[frameIndex].backBuffer, rtvDesc, ViewType.RTV);
    }
    // Create depth buffer and DSV.
    var depthDesc = ResourceDescription.Texture2D(Format.R32_Typeless, width, height, 1, 1, 1, 0, ResourceFlags.AllowDepthStencil | ResourceFlags.DenyShaderResource);
    var optimizedClear = new ClearValue { Format = Format.D32_Float, DepthStencil = new DepthStencilValue { Depth = 1.0f } };
    FrameResource.depthBuffer = dx12Device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None, depthDesc, ResourceStates.DepthWrite, optimizedClear);
    DepthStencilViewDescription dsvDesc = new DepthStencilViewDescription { Format = Format.D32_Float, Dimension = DepthStencilViewDimension.Texture2D };
    DescHeapManager.RemoveView(FrameResource.dsvIndex, ViewType.DSV);
    FrameResource.dsvIndex = DescHeapManager.CreateView(dx12Device, FrameResource.depthBuffer, dsvDesc, ViewType.DSV);
    RenderPassResource.Reinitialize(dx12Device, width, height);
  }

  void Update()
  {
    UpdateTimer();
    Input.Update(); DelayReleaseManager.Update(fence.CompletedFence);
    UpdateRenderItems();
    UpdateDummyCamera();
  }

  void UpdateTimer()
  {
    if (gameClock.IsRunning)
    {
      float currentTime = gameClock.ElapsedMilliseconds * 0.001f;
      deltaTime = currentTime - lastTime;
      lastTime = currentTime;
    }
    else
    {
      gameClock.Start();
    }
  }

  void UpdateRenderItems()
  {
    int itemCount = staticRenderItems.Count;
    foreach (int i in Enumerable.Range(0, itemCount))
    {
      var item = staticRenderItems[i];
      if (item.NeedUpdate())
      {
        int elementIndex = MaxStaticRenderItems * fence.FrameIndex + i;
        FrameResource.staticRenderItemObjectBuffer.Write(elementIndex, ref item.objectConst);
      }
    }

    itemCount = uiRenderItems.Count;
    foreach (int i in Enumerable.Range(0, itemCount))
    {
      var item = uiRenderItems[i];
      if (item.NeedUpdate())
      {
        int elementIndex = MaxUIRenderItems * fence.FrameIndex + i;
        FrameResource.uiRenderItemObjectBuffer.Write(elementIndex, ref item.objectConst);
      }
    }
  }

  void UpdateDummyCamera()
  {
    // Keyboard
    if (Input.GetKey(Keys.Menu) && Input.GetKey(Keys.F4))
      Application.Exit();
    if (Input.GetKeyDown(Keys.F11))
      form.SetWindowMode(form.FormBorderStyle != FormBorderStyle.None);
    cameraZAxis = Vector3.Normalize(cameraZAxis);
    Vector3 moveVec = Vector3.Zero;
    if (Input.GetKey(Keys.W))
      moveVec += cameraZAxis;
    if (Input.GetKey(Keys.S))
      moveVec -= cameraZAxis;
    if (Input.GetKey(Keys.A))
      moveVec -= Vector3.Normalize(Vector3.Cross(Vector3.Up, cameraZAxis));
    if (Input.GetKey(Keys.D))
      moveVec += Vector3.Normalize(Vector3.Cross(Vector3.Up, cameraZAxis));
    if (Input.GetKey(Keys.ControlKey))
      moveVec -= Vector3.Up;
    if (Input.GetKey(Keys.Space))
      moveVec += Vector3.Up;
    moveVec = Vector3.Normalize(moveVec);
    cameraPos += moveVec * deltaTime * 4;

    // Mouse
    if (Input.GetButton(MiceButton.RIGHT))
    {
      var mouseOffset = Input.MouseOffset;
      float deltaYaw = MathUtil.DegreesToRadians(mouseOffset.X) * 0.2f;
      float deltaPitch = MathUtil.DegreesToRadians(mouseOffset.Y) * 0.2f;
      var currentPitch = MathF.Acos(Vector3.Dot(cameraZAxis, Vector3.Up));
      deltaPitch = MathUtil.Clamp(deltaPitch, MathUtil.DegreesToRadians(15) - currentPitch, MathUtil.DegreesToRadians(180 - 15) - currentPitch);
      Matrix.RotationY(deltaYaw, out var rotationYaw);
      var cameraXAxis = Vector3.Cross(Vector3.Up, cameraZAxis);
      Matrix.RotationAxis(ref cameraXAxis, deltaPitch, out var rotationPitch);
      Vector4 zAxis = new Vector4(cameraZAxis, 0);
      zAxis = Vector4.Transform(zAxis, rotationPitch);
      zAxis = Vector4.Transform(zAxis, rotationYaw);
      cameraZAxis = (Vector3)zAxis;
    }

    Matrix view = SharpDX.Matrix.LookAtLH(cameraPos, cameraPos + cameraZAxis, Vector3.Up);
    Matrix proj = SharpDX.Matrix.PerspectiveFovLH(MathUtil.DegreesToRadians(60), (float)width / (float)height, 0.1f, 100f);
    var passConst = new SuperPassConsts { viewProj = Matrix.Multiply(view, proj), viewportSize = new Vector4(width, height, 1f / width, 1f / height) };
    FrameResource.passBuffer.Write(fence.FrameIndex, ref passConst);
  }

  void Render()
  {
    // Record all the commands we need to render the scene into the command list
    PopulateCommandList();
    // Execute the command list
    commandQueue.ExecuteCommandList(cmdList);
    // Swap the back and front buffers
    swapChain.Present(vSyncEnabled ? 1 : 0, vSyncEnabled ? PresentFlags.None : PresentFlags.AllowTearing);
  }

  /// <summary>
  /// Fill the command list with commands
  /// </summary>
  void PopulateCommandList()
  {
    frames[fence.FrameIndex].cmdAllocator.Reset();
    cmdList.Reset(frames[fence.FrameIndex].cmdAllocator, PipelineStateManager.GetPipelineConfig("GBuffer.hlsl").pso);

    // Update default heaps.
    DefaultBufferUpdater.UpdateAll(cmdList, fence.TargetFence);

    // setup viewport and scissors
    cmdList.SetViewport(viewPort);
    cmdList.SetScissorRectangles(scissorRectangle);

    // Use barrier to notify that we are using the RenderTarget to clear it
    cmdList.ResourceBarrierTransition(frames[fence.FrameIndex].backBuffer, ResourceStates.Present, ResourceStates.RenderTarget);

    cmdList.ClearRenderTargetView(DescHeapManager.GetCPUHandle(frames[fence.FrameIndex].rtvIndex, ViewType.RTV), Color4.Black);
    cmdList.ClearDepthStencilView(DescHeapManager.GetCPUHandle(FrameResource.dsvIndex, ViewType.DSV), ClearFlags.FlagsDepth, 1.0f, 0);
    RenderPassResource.CLeanAll(cmdList);

    var rtvs = new CpuDescriptorHandle[]
    {
      DescHeapManager.GetCPUHandle(frames[fence.FrameIndex].rtvIndex, ViewType.RTV),
      DescHeapManager.GetCPUHandle(RenderPassResource.Get(GBufferType.GBuffer0).rtvIndex, ViewType.RTV),
      DescHeapManager.GetCPUHandle(RenderPassResource.Get(GBufferType.GBuffer1).rtvIndex, ViewType.RTV)
    };
    cmdList.SetRenderTargets(rtvs, DescHeapManager.GetCPUHandle(FrameResource.dsvIndex, ViewType.DSV));
    DescHeapManager.BindSrvUavHeap(cmdList);
    cmdList.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
    cmdList.SetGraphicsRootSignature(PipelineStateManager.GetPipelineConfig("GBuffer.hlsl").sign);
    cmdList.SetGraphicsRootConstantBufferView(1, FrameResource.passBuffer.GetGPUAddress(fence.FrameIndex));

    // Draw static render items.
    int itemCount = staticRenderItems.Count;
    for (int index = 0; index < itemCount; index++)
    {
      var item = staticRenderItems[index];
      cmdList.SetGraphicsRootConstantBufferView(0, FrameResource.staticRenderItemObjectBuffer.GetGPUAddress(fence.FrameIndex * MaxStaticRenderItems + index));
      cmdList.SetGraphicsRootDescriptorTable(2, Texture.GetHandle(item.albedoTex));
      cmdList.SetGraphicsRootDescriptorTable(3, Texture.GetHandle(item.normalTex));
      cmdList.SetVertexBuffer(0, item.mesh.vertexBufferView);
      cmdList.SetIndexBuffer(item.mesh.indexBufferView);
      cmdList.DrawIndexedInstanced(item.mesh.IndexCount, 1, 0, 0, 0);
    }

    // Draw UI render items.
    cmdList.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleStrip;
    cmdList.PipelineState = PipelineStateManager.GetPipelineConfig("UI.hlsl").pso;
    cmdList.SetGraphicsRootSignature(PipelineStateManager.GetPipelineConfig("UI.hlsl").sign);
    cmdList.SetGraphicsRootConstantBufferView(1, FrameResource.passBuffer.GetGPUAddress(fence.FrameIndex));
    itemCount = uiRenderItems.Count;
    for (int index = 0; index < itemCount; index++)
    {
      var item = uiRenderItems[index];
      cmdList.SetGraphicsRootConstantBufferView(0, FrameResource.uiRenderItemObjectBuffer.GetGPUAddress(fence.FrameIndex * MaxUIRenderItems + index));
      cmdList.SetGraphicsRootDescriptorTable(2, Texture.GetHandle(item.tex));
      cmdList.SetVertexBuffer(0, item.mesh.vertexBufferView);
      cmdList.DrawInstanced(item.mesh.VertexCount, 1, 0, 0);
    }

    // Use barrier to notify that we are going to present the RenderTarget
    cmdList.ResourceBarrierTransition(frames[fence.FrameIndex].backBuffer, ResourceStates.RenderTarget, ResourceStates.Present);
    // Execute the command
    cmdList.Close();
  }

  /// <summary>Fence synchronization wrapper.</summary>
  private class FenceSync
  {
    Fence fence;
    AutoResetEvent syncEvent;
    CommandQueue commandQueue;
    long[] frames;
    int currFrameIdx;
    IntPtr syncEventHandle;
    readonly int frameCount;
    long cachedCompletedValue;
    long currFrameCompletedValue;

    public int FrameIndex => currFrameIdx;

    public long CompletedFence => cachedCompletedValue;

    public long TargetFence => currFrameCompletedValue;

    public FenceSync(D12Device dx12Device, CommandQueue commandQueue, int frameCount)
    {
      if (frameCount <= 0)
        throw new ArgumentOutOfRangeException("FrameCount must greater than 0.");
      frames = new long[frameCount];
      this.frameCount = frameCount;
      this.commandQueue = commandQueue;
      syncEvent = new AutoResetEvent(false);
      syncEventHandle = syncEvent.SafeWaitHandle.DangerousGetHandle();
      fence = dx12Device.CreateFence(0, FenceFlags.None);
      currFrameCompletedValue = 1;
    }

    ~FenceSync()
    {
      // Command queue is owned by engine, do not dispose there.
      syncEvent.Dispose();
      fence.Dispose();
    }

    /// <summary>
    /// <b>For multi-frame instance:</b> Get next frame index, wait if no available frame.<br/>
    /// <b>For single-frame instance:</b> Flush command queue.<br/><br/>
    /// <b>WARNING:</b> Invoke this after ExecuteCommandList()
    /// </summary>
    /// <param name="flushQueue"> Frame index unchanges if set this TRUE.</param>
    public void Synchronize(bool flushQueue)
    {
      commandQueue.Signal(fence, currFrameCompletedValue);
      frames[currFrameIdx] = currFrameCompletedValue;
      currFrameCompletedValue++;
      // Cycle through the circular frame resource array.
      if (!flushQueue) currFrameIdx = (currFrameIdx + 1) % frameCount;
      long fenceRecord = frames[currFrameIdx];
      // Has the GPU finished processing the commands of the current frame resource?
      // If not, wait until the GPU has completed commands up to this fence point.
      if (fenceRecord > 0 && fence.CompletedValue < fenceRecord)
      {
        fence.SetEventOnCompletion(fenceRecord, syncEventHandle);
        syncEvent.WaitOne();
      }
      cachedCompletedValue = fence.CompletedValue;
    }
  }
}