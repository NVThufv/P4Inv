using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Boogie;

/// <summary>
/// Allows grouping writes from concurrent sources before sending them to a single TextWriter.
/// For each concurrent source, a TextWriter can be spawned using AppendWriter.
/// </summary>
public class ConcurrentToSequentialWriteManager
{
  public TextWriter Writer { get; }
  private readonly Queue<SubWriter> writers = new();

  public ConcurrentToSequentialWriteManager(TextWriter writer) {
    Writer = writer;
  }

  private readonly object myLock = new();

  private void Disposed() {
    lock (myLock) {
      while (writers.Count > 0 && writers.Peek().Disposed) {
        var disposedWriter = writers.Dequeue();
        lock (disposedWriter.Lock) {
          Writer.Write(disposedWriter.SetTargetAndGetBuffer(null));
        }
      }
      if (writers.Count > 0) {
        var subWriter = writers.Peek();
        lock (subWriter.Lock) {
          Writer.Write(subWriter.SetTargetAndGetBuffer(Writer));
        }
      }
    }
    Writer.Flush();
  }

  public TextWriter AppendWriter() {
    lock (myLock) {
      var target = writers.Count == 0 ? Writer : null;
      var result = new SubWriter(this, target);
      writers.Enqueue(result);
      return result;
    }
  }

  class SubWriter : SyncWriterWrapper {
    private readonly ConcurrentToSequentialWriteManager collector;
    private StringWriter bufferWriter;
    public bool Disposed { get; private set; }

    public new object Lock => base.Lock;

    public SubWriter(ConcurrentToSequentialWriteManager collector, TextWriter target) : base(null) {
      this.collector = collector;
      if (target == null) {
        bufferWriter = new StringWriter();
        this.target = bufferWriter;
      } else {
        this.target = target;
        bufferWriter = null;
      }
    }

    /// <summary>
    /// Only called for disposed writers that aren't being written to any more.
    /// </summary>
    public string SetTargetAndGetBuffer(TextWriter newTarget) {
      lock (Lock) {
        if (bufferWriter == null && newTarget != target && newTarget != null) {
          throw new Exception("Can not change the target when not buffering, except to null");
        }
        var result = bufferWriter?.ToString() ?? "";
        target = newTarget;
        bufferWriter = null;
        return result;
      }
    }

    protected override void Dispose(bool disposing) {
      Disposed = true;
      collector.Disposed();
    }
  }
}