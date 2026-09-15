namespace PicoBench.Tests;

/// <summary>
/// Tests for <see cref="Runner"/> low-level timing utilities.
/// Covers validation, timing accuracy, setup/teardown, and generic state overload.
/// </summary>
public class RunnerTests
{
    // ─── Time(int, Action) — basic overload ─────────────────────────

    [Test]
    [Property("Category", "Runner")]
    public async Task Time_BasicOverload_ReturnsTimingSample()
    {
        var sample = Runner.Time(10, () => { });

        await Assert.That(sample).IsNotNull();
        await Assert.That(sample.ElapsedNanoseconds).IsGreaterThanOrEqualTo(0);
        await Assert.That(sample.ElapsedMilliseconds).IsGreaterThanOrEqualTo(0);
        await Assert.That(sample.ElapsedTicks).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task Time_BasicOverload_GcInfoIsPopulated()
    {
        var sample = Runner.Time(10, () => { });

        await Assert.That(sample.GcInfo).IsNotNull();
        await Assert.That(sample.GcInfo!.Gen0).IsGreaterThanOrEqualTo(0);
    }

    // ─── Time(int, Action, Action?, Action?) — with setup/teardown ──

    [Test]
    [Property("Category", "Runner")]
    public async Task Time_WithSetupAndTeardown_ExecutesThem()
    {
        bool setupCalled = false;
        bool teardownCalled = false;

        Runner.Time(
            1,
            () => { },
            setup: () => setupCalled = true,
            teardown: () => teardownCalled = true
        );

        await Assert.That(setupCalled).IsTrue();
        await Assert.That(teardownCalled).IsTrue();
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task Time_NullSetupAndTeardown_DoesNotThrow()
    {
        var sample = Runner.Time(1, () => { }, setup: null, teardown: null);

        await Assert.That(sample).IsNotNull();
    }

    [Test]
    [NotInParallel] // reads process-wide GC counters
    [Property("Category", "Runner")]
    public async Task Time_SetupForcedCollection_IsNotAttributedToBenchmark()
    {
        var sample = Runner.Time(
            1,
            () => { },
            setup: () => GC.Collect(0, GCCollectionMode.Forced),
            teardown: null
        );

        await Assert.That(sample.GcInfo!.Gen0).IsEqualTo(0);
    }

    [Test]
    [NotInParallel] // reads process-wide GC counters
    [Property("Category", "Runner")]
    public async Task Time_TeardownForcedCollection_IsNotAttributedToBenchmark()
    {
        var sample = Runner.Time(
            1,
            () => { },
            setup: null,
            teardown: () => GC.Collect(0, GCCollectionMode.Forced)
        );

        await Assert.That(sample.GcInfo!.Gen0).IsEqualTo(0);
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task Time_ZeroIterations_ThrowsArgumentOutOfRangeException()
    {
        await Assert.That(() => Runner.Time(0, () => { })).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task Time_NegativeIterations_ThrowsArgumentOutOfRangeException()
    {
        await Assert.That(() => Runner.Time(-1, () => { })).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task Time_NullAction_ThrowsArgumentNullException()
    {
        await Assert.That(() => Runner.Time(1, (Action)null!)).Throws<ArgumentNullException>();
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task Time_FullOverload_NullAction_ThrowsArgumentNullException()
    {
        await Assert
            .That(() => Runner.Time(1, (Action)null!, null, null))
            .Throws<ArgumentNullException>();
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task Time_FullOverload_ZeroIterations_ThrowsArgumentOutOfRangeException()
    {
        await Assert
            .That(() => Runner.Time(0, () => { }, null, null))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task Time_ExecutesCorrectNumberOfIterations()
    {
        int count = 0;
        Runner.Time(5, () => count++);

        await Assert.That(count).IsEqualTo(5);
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task Time_ElapsedNanoseconds_MatchesMilliseconds()
    {
        var sample = Runner.Time(1, () => Thread.Sleep(5));

        // ElapsedMilliseconds should be approximately ElapsedNanoseconds / 1_000_000
        var expectedMs = sample.ElapsedNanoseconds / 1_000_000.0;
        var diff = Math.Abs(sample.ElapsedMilliseconds - expectedMs);

        await Assert.That(diff).IsLessThan(0.01); // allow small floating-point error
    }

    // ─── Time<TState>(int, TState, Action<TState>) — generic overload

    [Test]
    [Property("Category", "Runner")]
    public async Task TimeWithState_ReturnsTimingSample()
    {
        var sample = Runner.Time(
            10,
            42,
            s =>
            {
                var _ = s + 1;
            }
        );

        await Assert.That(sample).IsNotNull();
        await Assert.That(sample.ElapsedNanoseconds).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task TimeWithState_ZeroIterations_ThrowsArgumentOutOfRangeException()
    {
        await Assert
            .That(() => Runner.Time(0, 42, (Action<int>)(s => { })))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task TimeWithState_NegativeIterations_ThrowsArgumentOutOfRangeException()
    {
        await Assert
            .That(() => Runner.Time(-1, 42, (Action<int>)(s => { })))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task TimeWithState_NullAction_ThrowsArgumentNullException()
    {
        await Assert.That(() => Runner.Time<int>(1, 42, null!)).Throws<ArgumentNullException>();
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task TimeWithState_ExecutesCorrectNumberOfIterations()
    {
        int count = 0;
        Runner.Time(7, 0, s => count++);

        await Assert.That(count).IsEqualTo(7);
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task TimeWithState_PassesStateCorrectly()
    {
        int sum = 0;
        Runner.Time(3, 10, s => sum += s);

        await Assert.That(sum).IsEqualTo(30);
    }

    // ─── Initialize ─────────────────────────────────────────────────

    [Test]
    [Property("Category", "Runner")]
    public async Task Initialize_CanBeCalledMultipleTimes_WithoutError()
    {
        // Initialize is idempotent via Lazy<T>
        Runner.Initialize();
        Runner.Initialize();

        // Verify the runner still works after multiple Initialize calls
        var sample = Runner.Time(1, () => { });
        await Assert.That(sample).IsNotNull();
    }

    // ─── Async timing tests ────────────────────────────────────────

    [Test]
    [NotInParallel] // reads process-wide GC counters
    [Property("Category", "Runner")]
    public async Task TimeAsync_SetupForcedCollection_IsNotAttributedToBenchmark()
    {
        var sample = await Runner.TimeAsync(
            1,
            () => Task.CompletedTask,
            setup: () =>
            {
                GC.Collect(0, GCCollectionMode.Forced);
                return Task.CompletedTask;
            }
        );

        await Assert.That(sample.GcInfo!.Gen0).IsEqualTo(0);
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task TimeAsync_ReturnsGcInfoWithIsApproximateTrue()
    {
        var sample = await Runner.TimeAsync(
            10,
            async () =>
            {
                await Task.CompletedTask;
            }
        );

        await Assert.That(sample.GcInfo).IsNotNull();
        await Assert.That(sample.GcInfo!.IsApproximate).IsTrue();
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task TimeAsync_WithState_PassesStateCorrectly()
    {
        int sum = 0;
        await Runner.TimeAsync(
            3,
            10,
            async s =>
            {
                sum += s;
                await Task.CompletedTask;
            }
        );

        await Assert.That(sum).IsEqualTo(30);
    }

    [Test]
    [Property("Category", "Runner")]
#pragma warning disable CS8619 // Task<T> nullability mismatch in assertion lambda
    public async Task TimeAsync_WithState_ZeroIterations_ThrowsArgumentOutOfRangeException()
    {
        await Assert
            .That(() => Runner.TimeAsync<int>(0, 42, s => Task.CompletedTask))
            .Throws<ArgumentOutOfRangeException>();
    }
#pragma warning restore CS8619

    // ─── Async CPU cycles must stay valid across thread hops ───────

    [Test]
    [Property("Category", "Runner")]
    public async Task TimeAsync_CpuCycles_AcrossThreadHops_DoNotUnderflow()
    {
        var sample = await Runner.TimeAsync(
            200,
            async () =>
            {
                await Task.Yield();
                Thread.SpinWait(500);
            }
        );

        // Reading per-thread cycle counters on two different threads and
        // subtracting them wraps ulong near 2^64. Any plausible count is far
        // below 2^50.
        await Assert.That(sample.CpuCycles).IsLessThan(1UL << 50);
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task TimeCpuAsync_CpuCycles_AcrossThreadHops_DoNotUnderflow()
    {
        var sample = await Runner.TimeCpuAsync(
            200,
            async () =>
            {
                await Task.Yield();
                Thread.SpinWait(500);
            }
        );

        await Assert.That(sample.CpuCycles).IsLessThan(1UL << 50);
    }

    // ─── Linux perf-event guard and attribute flags ─────────────────

    public static IEnumerable<(int? Paranoid, bool Expected)> GetPerfParanoidCases()
    {
        yield return (null, true); // unreadable -> attempt the syscall
        yield return (-1, true);
        yield return (0, true);
        yield return (1, true);
        yield return (2, true); // user-space counting still allowed
        yield return (3, false);
        yield return (4, false);
    }

    [Test]
    [Property("Category", "Runner")]
    [MethodDataSource(nameof(GetPerfParanoidCases))]
    public async Task ShouldEnableLinuxPerf_RespectsParanoidThreshold(int? paranoid, bool expected)
    {
        await Assert.That(Runner.ShouldEnableLinuxPerf(paranoid)).IsEqualTo(expected);
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task CreateCyclesPerfEventAttr_ExcludesKernelCycles()
    {
        var attr = Runner.CreateCyclesPerfEventAttr();

        await Assert.That((attr.Flags & Runner.PerfAttrExcludeKernel) != 0).IsTrue();
        await Assert.That(attr.Type).IsEqualTo(0u);
        await Assert.That(attr.Config).IsEqualTo(0UL);
    }

    [Test]
    [Property("Category", "Runner")]
    public async Task CreateProcessCyclesPerfEventAttr_InheritsThreads()
    {
        var attr = Runner.CreateProcessCyclesPerfEventAttr();

        await Assert.That((attr.Flags & Runner.PerfAttrInherit) != 0).IsTrue();
        await Assert.That((attr.Flags & Runner.PerfAttrExcludeKernel) != 0).IsTrue();
    }

    // ─── CPU clock granularity for CpuOnly calibration ─────────────

    [Test]
    [Property("Category", "Runner")]
    public async Task GetCpuClockGranularity_ReturnsPlausibleValue()
    {
        var granularity = Runner.GetCpuClockGranularity();

        await Assert.That(granularity).IsGreaterThan(TimeSpan.Zero);
        await Assert.That(granularity).IsLessThan(TimeSpan.FromMilliseconds(250));
    }

    // ─── Priority boost scope ──────────────────────────────────────

    [Test]
    [NotInParallel]
    [Property("Category", "Runner")]
    public async Task BoostPriorities_OnDispose_RestoresProcessAndThreadPriorities()
    {
        var originalProcess = Process.GetCurrentProcess().PriorityClass;
        var originalThread = Thread.CurrentThread.Priority;

        using (Runner.BoostPriorities(enabled: true))
        {
            // Scope is active here (or the environment refused the boost).
        }

        await Assert.That(Process.GetCurrentProcess().PriorityClass).IsEqualTo(originalProcess);
        await Assert.That(Thread.CurrentThread.Priority).IsEqualTo(originalThread);
    }

    [Test]
    [NotInParallel]
    [Property("Category", "Runner")]
    public async Task BoostPriorities_Disabled_DoesNotChangePriorities()
    {
        var originalProcess = Process.GetCurrentProcess().PriorityClass;
        var originalThread = Thread.CurrentThread.Priority;

        using (var scope = Runner.BoostPriorities(enabled: false))
        {
            await Assert.That(scope.IsActive).IsFalse();
        }

        await Assert.That(Process.GetCurrentProcess().PriorityClass).IsEqualTo(originalProcess);
        await Assert.That(Thread.CurrentThread.Priority).IsEqualTo(originalThread);
    }
}
