namespace PicoBench.Tests.Batch0;

/// <summary>
/// Batch 0 · G6 (guard gap of 0.8) — the asynchronous timing paths must clean up
/// exactly like the synchronous one.
/// <para>
/// <see cref="Runner.TimeAsync(int, Func{Task}, Func{Task}?, Func{Task}?)"/> and
/// <see cref="Runner.TimeCpuAsync(int, Func{Task}, Func{Task}?, Func{Task}?)"/> got
/// the same <c>try/finally</c> as <see cref="Runner.Time(int, Action, Action?, Action?)"/>
/// in 0.8, but G5's mutation matrix
/// (<c>.review-2026-11/16-verification.md</c> §3.2, rows <c>0.8b</c>/<c>0.8c</c>)
/// measured that deleting the <c>await teardown()</c> from the async paths and
/// moving it back to the success path kept the <b>whole</b> suite green: the only
/// teardown test was written against <see cref="Runner.Time(int, Action, Action?, Action?)"/>.
/// These four tests pin both asynchronous loops and both failure modes.
/// </para>
/// </summary>
public class AsyncTeardownTests
{
    private const int Iterations = 100;
    private const int FailingIteration = 50;

    /// <summary>
    /// 0.8 — an iteration that throws in <see cref="Runner.TimeAsync(int, Func{Task}, Func{Task}?, Func{Task}?)"/>
    /// must still run the teardown once, and the exception must still surface.
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.8/async-throw")]
    public async Task TimeAsync_IterationThrows_TeardownRunsOnce()
    {
        var (setups, invoked, teardowns, caught) = await RunAsyncTiming(throwOnIteration: true);

        Batch0Support.Log(
            $"TimeAsync throw: setups={setups} iterations={invoked} teardowns={teardowns} "
                + $"caught={caught?.GetType().Name ?? "<none>"}"
        );

        await Assert.That(caught).IsNotNull();
        await Assert.That(setups).IsEqualTo(1);
        await Assert.That(invoked).IsEqualTo(FailingIteration);
        await Assert.That(teardowns).IsEqualTo(1);
    }

    /// <summary>
    /// 0.8 — the same for a <b>cancelled</b> iteration
    /// (<see cref="OperationCanceledException"/> from the delegate, which is how a
    /// cancellation surfaces inside the timing loop).
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.8/async-cancel")]
    public async Task TimeAsync_Cancelled_TeardownRunsOnce()
    {
        var (setups, invoked, teardowns, caught) = await RunAsyncTiming(cancelOnIteration: true);

        Batch0Support.Log(
            $"TimeAsync cancel: setups={setups} iterations={invoked} teardowns={teardowns} "
                + $"caught={caught?.GetType().Name ?? "<none>"}"
        );

        await Assert.That(caught).IsAssignableTo<OperationCanceledException>();
        await Assert.That(setups).IsEqualTo(1);
        await Assert.That(invoked).IsEqualTo(FailingIteration);
        await Assert.That(teardowns).IsEqualTo(1);
    }

    /// <summary>
    /// 0.8 — <see cref="Runner.TimeCpuAsync(int, Func{Task}, Func{Task}?, Func{Task}?)"/>
    /// (the <see cref="AsyncTimingMode.CpuOnly"/> path) runs the teardown once when
    /// an iteration throws.
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.8/async-cpu-throw")]
    public async Task TimeCpuAsync_IterationThrows_TeardownRunsOnce()
    {
        var (setups, invoked, teardowns, caught) = await RunAsyncTiming(
            throwOnIteration: true,
            cpuOnly: true
        );

        Batch0Support.Log(
            $"TimeCpuAsync throw: setups={setups} iterations={invoked} teardowns={teardowns} "
                + $"caught={caught?.GetType().Name ?? "<none>"}"
        );

        await Assert.That(caught).IsNotNull();
        await Assert.That(setups).IsEqualTo(1);
        await Assert.That(invoked).IsEqualTo(FailingIteration);
        await Assert.That(teardowns).IsEqualTo(1);
    }

    /// <summary>
    /// 0.8 — and when an iteration of the CPU-only path is cancelled.
    /// </summary>
    [Test]
    [Property("Category", "Batch0")]
    [Property("Batch0", "0.8/async-cpu-cancel")]
    public async Task TimeCpuAsync_Cancelled_TeardownRunsOnce()
    {
        var (setups, invoked, teardowns, caught) = await RunAsyncTiming(
            cancelOnIteration: true,
            cpuOnly: true
        );

        Batch0Support.Log(
            $"TimeCpuAsync cancel: setups={setups} iterations={invoked} teardowns={teardowns} "
                + $"caught={caught?.GetType().Name ?? "<none>"}"
        );

        await Assert.That(caught).IsAssignableTo<OperationCanceledException>();
        await Assert.That(setups).IsEqualTo(1);
        await Assert.That(invoked).IsEqualTo(FailingIteration);
        await Assert.That(teardowns).IsEqualTo(1);
    }

    /// <summary>
    /// Drives one asynchronous timing call whose <see cref="FailingIteration"/>-th
    /// iteration either throws or observes a cancelled token, and reports what was
    /// counted.
    /// </summary>
    private static async Task<(int Setups, int Invoked, int Teardowns, Exception? Caught)> RunAsyncTiming(
        bool throwOnIteration = false,
        bool cancelOnIteration = false,
        bool cpuOnly = false
    )
    {
        var invoked = 0;
        var setups = 0;
        var teardowns = 0;
        Exception? caught = null;

        using var cancellation = new CancellationTokenSource();

        Func<Task> action = async () =>
        {
            invoked++;
            if (invoked == FailingIteration)
            {
                if (throwOnIteration)
                    throw new InvalidOperationException($"iteration {FailingIteration} failed");

                if (cancelOnIteration)
                {
                    await cancellation.CancelAsync();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
            }

            await Task.Yield();
        };

        Func<Task> setup = () =>
        {
            setups++;
            return Task.CompletedTask;
        };
        Func<Task> teardown = () =>
        {
            teardowns++;
            return Task.CompletedTask;
        };

        try
        {
            if (cpuOnly)
                await Runner.TimeCpuAsync(Iterations, action, setup, teardown);
            else
                await Runner.TimeAsync(Iterations, action, setup, teardown);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        return (setups, invoked, teardowns, caught);
    }
}
