using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Boogie;
using NUnit.Framework;

namespace CoreTests;

[TestFixture]
public class AsyncQueueTest
{
  [Test]
  public async Task DequeueOrder()
  {
    var queue = new AsyncQueue<int>();
    var firstValue = 1;
    var secondValue = 2;
    var waitingDequeueTask = queue.Dequeue(CancellationToken.None);
    queue.Enqueue(firstValue);
    queue.Enqueue(secondValue);
    var firstResult = await waitingDequeueTask;
    var secondResult = await queue.Dequeue(CancellationToken.None);

    Assert.AreEqual(firstValue, firstResult);
    Assert.AreEqual(secondValue, secondResult);
  }

  [Test]
  public async Task CancellationSupport()
  {
    var queue = new AsyncQueue<int>();
    var source = new CancellationTokenSource();
    var firstResultTask = queue.Dequeue(source.Token);
    var secondResultTask = queue.Dequeue(CancellationToken.None);
    var firstValue = 3;
    source.Cancel();
    queue.Enqueue(firstValue);

    try {
      await firstResultTask;
      Assert.True(false);
    }
    catch (TaskCanceledException) {
      Assert.True(firstResultTask.IsCanceled);
    }
    var secondResult = await secondResultTask;
    Assert.AreEqual(firstValue, secondResult);
  }

  [Test]
  public void ItemsAreKeptInOrder()
  {
    var queue = new AsyncQueue<int>();
    var tasks = new List<Task<int>>();
    for (int i = 0; i < 100; i++) {
      tasks.Add(queue.Dequeue(CancellationToken.None));
    }
    for (int i = 0; i < 100; i++) {
      queue.Enqueue(i);
    }

    var results = tasks.Select(t => t.Result).ToList();
    for (int i = 0; i < 100; i++) {
      Assert.AreEqual(i, results[i]);
    }
  }

  [Test]
  public async Task EnqueueReturnsBeforeCompletingDequeueTask()
  {
    var queue = new AsyncQueue<int>();
    var semaphore = new Semaphore(0, 1);

    var firstResultTask = queue.Dequeue(CancellationToken.None);
    var secondValue = 2;
    var mappedTask = firstResultTask.ContinueWith(t =>
    {
      semaphore.WaitOne();
      return t.Result + secondValue;
    }, TaskContinuationOptions.ExecuteSynchronously);

    var firstValue = 3;
    queue.Enqueue(firstValue);
    semaphore.Release();

    var mappedResult = await mappedTask;
    Assert.AreEqual(firstValue + secondValue, mappedResult);
  }

  [Test]
  public async Task ThreadSafe()
  {
    var queue = new AsyncQueue<int>();
    var amount = 1000;
    var tasks1 = new List<Task<int>>(amount);
    var tasks2 = new List<Task<int>>(amount);

    void EnqueueAction()
    {
      for (int i = 0; i < amount; i++) {
        queue.Enqueue(i);
      }
    }
    void DequeueAction1()
    {
      for (int i = 0; i < amount; i++) {
        tasks1.Add(queue.Dequeue(CancellationToken.None));
      }
    }
    void DequeueAction2()
    {
      for (int i = 0; i < amount; i++) {
        tasks2.Add(queue.Dequeue(CancellationToken.None));
      }
    }

    var enqueueThread1 = new Thread(EnqueueAction);
    var enqueueThread2 = new Thread(EnqueueAction);
    var dequeueThread1 = new Thread(DequeueAction1);
    var dequeueThread2 = new Thread(DequeueAction2);
    enqueueThread1.Start();
    dequeueThread1.Start();
    enqueueThread2.Start();
    dequeueThread2.Start();
    enqueueThread1.Join();
    dequeueThread1.Join();
    enqueueThread2.Join();
    dequeueThread2.Join();
    Assert.AreEqual(2 * amount, tasks1.Count + tasks2.Count);
    await Task.WhenAll(tasks1.Concat(tasks2));
  }
}