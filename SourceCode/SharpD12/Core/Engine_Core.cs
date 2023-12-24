using SharpDX;
using SharpDX.DXGI;
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Input;

namespace SharpD12
{
  using Linearstar.Windows.RawInput;
  using SharpDX.Direct3D12;
  using SharpDX.Windows;
  using System.Collections.Generic;
  using System.Linq;
  using System.Windows.Forms;
  using static ProgramDefinedConstants;

  public partial class SD12Engine
  {
    readonly Stopwatch gameClock;
    float lastTime = 0;

    Vector3 cameraPos = new Vector3(-3, 3, -3);
    Vector3 cameraZAxis = new Vector3(1, -1, 1);
    CursorScreenPos currMousePos;
    CustomedForm form;

    int width;
    int height;
    Fence fence;
    Device dx12Device;
    SwapChain swapChain;
    FrameResource[] frames;
    List<RenderItem> renderItems;
    CommandQueue commandQueue;
    GraphicsCommandList cmdList;

    ViewportF viewPort;
    Rectangle scissorRectangle;
    int currFrameIdx = 0;
    long currFenceValue = 1; // Fence value when current frame completed.
    AutoResetEvent syncEvent = new AutoResetEvent(false);
    IntPtr syncEventHandle;
    public static int RTVSize;
    public static int DSVSize;
    public static int CBVSRVUAVSize;

    public void Run()
    {
      // Initialize engine before loop will make program stuck for a while, and form.backcolor don't work in this moment.
      // Therefore, show form after engine initial could reduces this issue.
      EngineInitialize();
      form.Show();
      int frameCount = 0;
      Stopwatch titleTimer = new Stopwatch();
      titleTimer.Start();

      var loop = new RenderLoop(form);
      while (loop.NextFrame())
      {
        // Display info in title.
        frameCount++;
        double frameTime = titleTimer.Elapsed.TotalMilliseconds;
        if (frameTime > 500)
        {
          form.Text = $"FrameTime = {(frameTime / frameCount).ToString("f2")}ms\t{(frameCount * 1000d / frameTime).ToString("f0")}FPS";
          titleTimer.Restart();
          frameCount = 0;
        }

        // Update and render for MiniEngine
        Update();
        Render();
      }
      loop.Dispose();
    }

    void Update()
    {
      // Update time.
      float deltaTime = 0;
      if(gameClock.IsRunning)
      {
        float currentTime = gameClock.ElapsedMilliseconds * 0.001f;
        deltaTime = currentTime - lastTime;
        lastTime = currentTime;
      }
      else
      {
        gameClock.Start();
      }

      // Update input.
      Input.PostProcess();

      // Update render items.
      int itemCount = renderItems.Count;
      foreach (int i in Enumerable.Range(0, itemCount))
      {
        var item = renderItems[i];
        if (item.dirtyFrameCount > 0)
        {
          --item.dirtyFrameCount;
          int elementIndex = MaxRenderItems * currFrameIdx + i;
          FrameResource.objectBuffer.Write(elementIndex, ref item.objectConst);
        }
      }

      UpdateDummyCamera(deltaTime);
      Matrix view = SharpDX.Matrix.LookAtLH(cameraPos, cameraPos + cameraZAxis, Vector3.Up);
      Matrix proj = SharpDX.Matrix.PerspectiveFovLH(MathUtil.DegreesToRadians(60), (float)width / (float)height, 0.1f, 100f);
      var passConst = new PassConstants { viewProj = Matrix.Multiply(view, proj) };
      FrameResource.passBuffer.Write(currFrameIdx, ref passConst);
    }

    void UpdateDummyCamera(float deltaTime)
    {
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

      var pos = new CursorScreenPos();
      GetCursorPos(ref pos);
      if ((Control.MouseButtons & MouseButtons.Right) == MouseButtons.Right)
      {
        var mouseDelta = pos - currMousePos;
        float deltaYaw = MathUtil.DegreesToRadians(mouseDelta.x) * 0.2f;
        float deltaPitch = MathUtil.DegreesToRadians(mouseDelta.y) * 0.2f;
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
      currMousePos = pos;
      //currMousePos = Mouse.GetPosition;
    }

    void Render()
    {
      // Record all the commands we need to render the scene into the command list
      PopulateCommandList();
      // Execute the command list
      commandQueue.ExecuteCommandList(cmdList);
      // Swap the back and front buffers
      swapChain.Present(0, PresentFlags.AllowTearing);
      // Fence synchrony
      FenceSync_MultipleBuffers();
    }

    /// <summary>
    /// Fill the command list with commands
    /// </summary>
    void PopulateCommandList()
    {
      frames[currFrameIdx].cmdAllocator.Reset();
      cmdList.Reset(frames[currFrameIdx].cmdAllocator, PSO.GetPSO(PSOType.PLACEHOLDER));

      // setup viewport and scissors
      cmdList.SetViewport(viewPort);
      cmdList.SetScissorRectangles(scissorRectangle);

      // Use barrier to notify that we are using the RenderTarget to clear it
      cmdList.ResourceBarrierTransition(frames[currFrameIdx].backBuffer, ResourceStates.Present, ResourceStates.RenderTarget);

      cmdList.ClearRenderTargetView(frames[currFrameIdx].backBufferHandle, CleanColor);
      cmdList.ClearDepthStencilView(FrameResource.dsvHandle, ClearFlags.FlagsDepth, 1.0f, 0);

      cmdList.SetRenderTargets(new CpuDescriptorHandle[] { frames[currFrameIdx].backBufferHandle }, FrameResource.dsvHandle);
      cmdList.SetDescriptorHeaps(FrameResource.srvDescHeap);
      cmdList.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
      cmdList.SetGraphicsRootSignature(PSO.GetRootSign(PSOType.PLACEHOLDER));
      cmdList.SetGraphicsRootConstantBufferView(1, FrameResource.passBuffer.GetGPUAddress(currFrameIdx));

      // Update default heaps.
      DefaultHeapManager.UpdateAll(cmdList);

      // Draw render items.
      int itemCount = renderItems.Count;
      foreach (int index in Enumerable.Range(0, itemCount))
      {
        var item = renderItems[index];
        cmdList.SetGraphicsRootConstantBufferView(0, FrameResource.objectBuffer.GetGPUAddress(currFrameIdx * MaxRenderItems + index));
        cmdList.SetGraphicsRootDescriptorTable(2, TextureManager.textures["DefaultTexture"].gpuDescriptor);
        cmdList.SetVertexBuffer(0, item.mesh.vertexBufferView);
        cmdList.SetIndexBuffer(item.mesh.indexBufferView);
        cmdList.DrawIndexedInstanced(item.mesh.IndexCount, 1, 0, 0, 0);
      }

      // Use barrier to notify that we are going to present the RenderTarget
      cmdList.ResourceBarrierTransition(frames[currFrameIdx].backBuffer, ResourceStates.RenderTarget, ResourceStates.Present);
      // Execute the command
      cmdList.Close();
    }

    /// <summary>
    /// Get next frame resource, wait if no available frame. It's efficent.<br/>
    /// <b>Invoke after CommandQueue.ExecuteCommandList</b>
    /// </summary>
    void FenceSync_MultipleBuffers()
    {
      commandQueue.Signal(fence, currFenceValue);
      frames[currFrameIdx].fenceValue = currFenceValue;
      // Cycle through the circular frame resource array.
      currFrameIdx = (currFrameIdx + 1) % SwapChainSize;
      long fenceValue = frames[currFrameIdx].fenceValue;
      // Has the GPU finished processing the commands of the current frame resource?
      // If not, wait until the GPU has completed commands up to this fence point.
      if (fenceValue != 0 && fence.CompletedValue < fenceValue)
      {
        fence.SetEventOnCompletion(fenceValue, syncEventHandle);
        syncEvent.WaitOne();
      }
      ++currFenceValue;
    }

    /// <summary>
    /// Wait for all commands to be finished. It's simple but inefficent.<br/>
    /// <b>Invoke after CommandQueue.ExecuteCommandList</b>
    /// </summary>
    void FenceSync_FlushCommandQueue()
    {
      commandQueue.Signal(fence, currFenceValue);
      if (fence.CompletedValue < currFenceValue)
      {
        fence.SetEventOnCompletion(currFenceValue, syncEventHandle);
        syncEvent.WaitOne();
      }
      ++currFenceValue;
    }
  }
}