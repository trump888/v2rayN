// ============================================================================
// RxSchedulers.cs  --  ReactiveUI 23 -> 18 polyfill
// ============================================================================
// ReactiveUI 23.x introduced a static class `RxSchedulers` with properties
// `MainThreadScheduler`, `TaskpoolScheduler`, etc. ReactiveUI 18.x (the
// last version supporting net48) does NOT ship this class. v2rayN 7.24.4
// uses `RxSchedulers.MainThreadScheduler` in 36 places (for `.ObserveOn()`
// and `.Schedule()` calls).
//
// We provide a drop-in replacement here. On net48 we map MainThreadScheduler
// to the current synchronization context (which on WPF is the Dispatcher
// sync context). For non-UI callers it falls back to ImmediateScheduler.
//
// This is a static class to match the API shape exactly; no source rewrite
// needed.
// ============================================================================

using System.Reactive.Concurrency;
using System.Threading;
using Splat;

namespace ServiceLib.Common
{
    /// <summary>
    /// Provides scheduler aliases matching ReactiveUI 23's RxSchedulers shape.
    /// All members are safe to call from any thread.
    /// </summary>
    public static class RxSchedulers
    {
        private static IScheduler _mainThreadScheduler;
        private static IScheduler _taskpoolScheduler;

        /// <summary>
        /// Scheduler that marshals work to the UI thread.
        /// On WPF this is DispatcherScheduler. On non-UI threads it
        /// falls back to ImmediateScheduler (synchronous execution).
        /// </summary>
        public static IScheduler MainThreadScheduler
        {
            get
            {
                if (_mainThreadScheduler != null) return _mainThreadScheduler;

                // Try to use RxUI's scheduler resolver if available
                try
                {
                    // Splat's Locator provides platform schedulers on net48
                    // when ReactiveUI.WPF is loaded.
                    var scheduler = Locator.Current.GetService<IScheduler>();
                    if (scheduler != null)
                    {
                        _mainThreadScheduler = scheduler;
                        return scheduler;
                    }
                }
                catch { /* fall through */ }

                // Fallback: use the current SynchronizationContext if any
                if (SynchronizationContext.Current != null)
                {
                    // SynchronizationContextScheduler is in System.Reactive.
                    _mainThreadScheduler = new SynchronizationContextScheduler(SynchronizationContext.Current);
                    return _mainThreadScheduler;
                }

                // Last resort: run synchronously
                _mainThreadScheduler = ImmediateScheduler.Instance;
                return _mainThreadScheduler;
            }
            set { _mainThreadScheduler = value; }
        }

        /// <summary>
        /// Scheduler that runs work on the thread pool.
        /// </summary>
        public static IScheduler TaskpoolScheduler
        {
            get
            {
                if (_taskpoolScheduler != null) return _taskpoolScheduler;
                _taskpoolScheduler = TaskPoolScheduler.Default;
                return _taskpoolScheduler;
            }
            set { _taskpoolScheduler = value; }
        }

        /// <summary>
        /// Scheduler that runs work immediately on the calling thread.
        /// </summary>
        public static IScheduler ImmediateScheduler => ImmediateScheduler.Instance;
    }
}
