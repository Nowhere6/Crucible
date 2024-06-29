using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Crucible;

public class IOTask
{
  const int INTERVAL_MS = 1;

  public enum IOType
  {
    Write,
    Read
  }

  private class CachedStream
  {
    const int MAXLIFE = 1000;

    public readonly Stream stream;
    public readonly string loc;
    public int life;

    static List<CachedStream> streams = new List<CachedStream>();

    private CachedStream(string _loc)
    {
      stream = new FileStream(_loc, FileMode.OpenOrCreate, FileAccess.ReadWrite);
      life = MAXLIFE;
      loc = _loc;
    }

    public static void UpdateAll()
    {
      streams.ForEach((item) => item.life--);
      streams.RemoveAll((item) => item.life <= 0);
    }

    public static void DisposeAll()
    {
      streams.ForEach((item) => item.stream.Dispose());
      streams.Clear();
    }

    public static Stream GetStream(string loc)
    {
      CachedStream item = streams.Find((item) => item.loc == loc);
      if (item == null)
      {
        item = new CachedStream(loc);
        streams.Add(item);
      }
      else
      {
        item.life = MAXLIFE;
      }
      return item.stream;
    }
  }

  public readonly IOType type;
  public readonly string loc;
  public readonly int offset;
  public readonly int size;

  int completed;
  byte[] data;

  public IOTask(IOType _type, string _loc, int _offset, int _size, byte[] _data = null)
  {
    completed = 0;
    offset = _offset;
    type = _type;
    data = _data;
    size = _size;
    loc = _loc;
  }

  public bool IsCompleted() => Interlocked.CompareExchange(ref completed, 1, 1) == 1;

  public bool TryGetData(out byte[] _data)
  {
    if (type == IOType.Write) throw new ArgumentException();
    bool result = IsCompleted();
    _data = result ? data : null;
    return result;
  }

  public byte[] GetData()
  {
    byte[] result;
    while (!TryGetData(out result)) ;
    return result;
  }

  static ConcurrentQueue<IOTask> tasks;
  static CancellationTokenSource cts;
  static Thread IOThread;

  public static void StartIOThread()
  {
    tasks = new ConcurrentQueue<IOTask>();
    cts = new CancellationTokenSource();
    IOThread = new Thread(IOMain);
    IOThread.Name = "IO Thread";
    IOThread.Start(cts.Token);
  }

  public static void EndIOThread()
  {
    cts.Cancel();
    SpinWait spinWait = new SpinWait();
    while (IOThread.ThreadState != ThreadState.Stopped) spinWait.SpinOnce();
  }

  public static IOTask Serialize(string loc, byte[] data, int offset = 0) => CreateTask(loc, IOType.Write, offset, 0, data);

  public static IOTask Deserialize(string loc, int size, int offset = 0) => CreateTask(loc, IOType.Read, offset, size);

  static IOTask CreateTask(string loc, IOType type, int offset, int size, byte[] data = null)
  {
    if ((type == IOType.Read) ^ (data == null)) throw new ArgumentException();
    IOTask task = new IOTask(type, loc, offset, size, data);
    tasks.Enqueue(task);
    return task;
  }

  static void IOMain(object param)
  {
    CancellationToken token = (CancellationToken)param;
    while (true)
    {
      Thread.Sleep(INTERVAL_MS);
      if (token.IsCancellationRequested && tasks.Count == 0) break;
      CachedStream.UpdateAll();
      while (tasks.TryDequeue(out IOTask task))
      {
        if (task.type == IOType.Write)
        {
          Write(task.loc, task.offset, task.data);
          task.data = null;
        }
        else
        {
          Read(task.loc, task.offset, out task.data, task.size);
        }
        Interlocked.Exchange(ref task.completed, 1);
      }
    }
    CachedStream.DisposeAll();
  }

  static void Write(string location, int offset, Byte[] buffer)
  {
    var stream = CachedStream.GetStream(location);
    stream.Position = offset;
    stream.Write(buffer);
  }

  static void Read(string location, int offset, out byte[] data, int size)
  {
    data = new byte[size];
    var stream = CachedStream.GetStream(location);
    stream.Position = offset;
    stream.Read(data);
  }
}