using System;
using System.Threading;
using System.Threading.Tasks;

namespace sc
{
    public static class Utilities
    {
        private static Logger _logger;
        public static void InBackground<T>(Func<T> function, bool longRunning = false) {
            if (function == null) {
                _logger.LogNullError(nameof(function));

                return;
            }

            TaskCreationOptions options = TaskCreationOptions.DenyChildAttach;

            if (longRunning) {
                options |= TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness;
            }

            Task.Factory.StartNew(function, CancellationToken.None, options, TaskScheduler.Default);
        } 
    }
}