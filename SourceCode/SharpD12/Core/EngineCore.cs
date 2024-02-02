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
using static SharpD12.AppConstants;
using Device = SharpDX.Direct3D12.Device;
using Resource = SharpDX.Direct3D12.Resource;

namespace SharpD12
{
  public partial class SD12Engine
  {
    readonly Stopwatch gameClock = new Stopwatch();
    float lastTime = 0;
    float deltaTime = 0;

    Vector3 cameraPos = new Vector3(-3, 3, -3);
    Vector3 cameraZAxis = new Vector3(1, -1, 1);
    CustomedForm form;

    int width;
    int height;
    Device dx12Device;
    SwapChain swapChain;
    FrameResource[] frames;
    List<UIRenderItem> uiRenderItems;
    List<StaticRenderItem> staticRenderItems;
    FenceSync fence;
    CommandQueue commandQueue;
    GraphicsCommandList cmdList;
    Factory4 factory = new Factory4();

    bool vSyncEnabled = false;
    ViewportF viewPort;
    Rectangle scissorRectangle;
    private static int rtv_size;
    private static int dsv_size;
    private static int csu_size;
    public static int RTVSize { get => rtv_size; }
    public static int DSVSize { get => dsv_size; }
    /// <summary> size of CBV SRV UAV </summary>
    public static int CSUSize { get => csu_size; }

    public void Run()
    {
      EngineInitialize();

      Input.Register(form.Handle);
      var loop = new RenderLoop(form);
      while (loop.NextFrame())
      {
        Resize();
        // Update and render for MiniEngine
        Update();
        Render();
      }
      Input.UnRegister();
      loop.Dispose();
    }

    void Resize()
    {
      if (form.DrawingPanel.Width == width && form.DrawingPanel.Height == height) return;
      if (form.WindowState == FormWindowState.Minimized) return;

      width = form.DrawingPanel.Width;
      height = form.DrawingPanel.Height;
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
        frames[frameIndex].backBuffer = swapChain.GetBackBuffer<Resource>(resIndex);
        frames[frameIndex].rtvIndex = DescHeapManager.CreateView(dx12Device, frames[frameIndex].backBuffer, rtvDesc, ViewType.RTV);
      }
      // Create depth buffer and DSV.
      var depthDesc = ResourceDescription.Texture2D(Format.R32_Typeless, width, height, 1, 1, 1, 0, ResourceFlags.AllowDepthStencil | ResourceFlags.DenyShaderResource);
      var optimizedClear = new ClearValue { Format = Format.D32_Float, DepthStencil = new DepthStencilValue { Depth = 1.0f } };
      FrameResource.depthBuffer = dx12Device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None, depthDesc, ResourceStates.DepthWrite, optimizedClear);
      DepthStencilViewDescription dsvDesc = new DepthStencilViewDescription { Format = Format.D32_Float, Dimension = DepthStencilViewDimension.Texture2D };
      FrameResource.dsvIndex = DescHeapManager.CreateView(dx12Device, FrameResource.depthBuffer, dsvDesc, ViewType.DSV);
    }

    void Update()
    {
      UpdateTimer();
      Input.Update();
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
      // Fence synchronization.
      fence.Synchronize(false);
    }

    /// <summary>
    /// Fill the command list with commands
    /// </summary>
    void PopulateCommandList()
    {
      frames[fence.FrameIndex].cmdAllocator.Reset();
      cmdList.Reset(frames[fence.FrameIndex].cmdAllocator, PSO.GetPSO(PSOType.PLACEHOLDER));

      // Update default heaps.
      DefaultBuffer<byte>.UpdateAll(cmdList);

      // setup viewport and scissors
      cmdList.SetViewport(viewPort);
      cmdList.SetScissorRectangles(scissorRectangle);

      // Use barrier to notify that we are using the RenderTarget to clear it
      cmdList.ResourceBarrierTransition(frames[fence.FrameIndex].backBuffer, ResourceStates.Present, ResourceStates.RenderTarget);

      cmdList.ClearRenderTargetView(DescHeapManager.GetCPUHandle(frames[fence.FrameIndex].rtvIndex, ViewType.RTV), CleanColor);
      cmdList.ClearDepthStencilView(DescHeapManager.GetCPUHandle(FrameResource.dsvIndex, ViewType.DSV), ClearFlags.FlagsDepth, 1.0f, 0);

      cmdList.SetRenderTargets(1, DescHeapManager.GetCPUHandle(frames[fence.FrameIndex].rtvIndex, ViewType.RTV), DescHeapManager.GetCPUHandle(FrameResource.dsvIndex, ViewType.DSV));
      DescHeapManager.BindSrvUavHeap(cmdList);
      cmdList.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
      cmdList.SetGraphicsRootSignature(PSO.GetRootSign(PSOType.PLACEHOLDER));
      cmdList.SetGraphicsRootConstantBufferView(1, FrameResource.passBuffer.GetGPUAddress(fence.FrameIndex));

      // Draw static render items.
      int itemCount = staticRenderItems.Count;
      for (int index = 0; index < itemCount; index++)
      {
        var item = staticRenderItems[index];
        cmdList.SetGraphicsRootConstantBufferView(0, FrameResource.staticRenderItemObjectBuffer.GetGPUAddress(fence.FrameIndex * MaxStaticRenderItems + index));
        cmdList.SetGraphicsRootDescriptorTable(2, Texture.GetHandle(item.albedoTex));
        cmdList.SetVertexBuffer(0, item.mesh.vertexBufferView);
        cmdList.SetIndexBuffer(item.mesh.indexBufferView);
        cmdList.DrawIndexedInstanced(item.mesh.IndexCount, 1, 0, 0, 0);
      }

      // Draw UI render items.
      cmdList.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleStrip;
      cmdList.PipelineState = PSO.GetPSO(PSOType.UI);
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
      long currFrameCompletedValue;

      public int FrameIndex { get => currFrameIdx; }

      public long CompletedFence { get => currFrameCompletedValue; }

      public FenceSync(Device dx12Device, CommandQueue commandQueue, int frameCount)
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
      }
    }
  }
}