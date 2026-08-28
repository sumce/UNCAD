using System;
using System.Diagnostics;
using UNCAD.Core.Contracts;

namespace UNCAD.Infra
{
    public static class ModuleRunner
    {
        public static T Run<T>(ModuleDescriptor module, string stage, Func<T> operation)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            string operationName = string.IsNullOrWhiteSpace(stage) ? "执行" : stage.Trim();
            var stopwatch = Stopwatch.StartNew();
            Log.Info("MODULE " + module.Label + " [" + operationName + "] started");
            try
            {
                T output = operation();
                stopwatch.Stop();
                Log.Info("MODULE " + module.Label + " [" + operationName
                    + "] completed in " + stopwatch.ElapsedMilliseconds + "ms");
                return output;
            }
            catch (ModuleExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Log.Error("MODULE " + module.Label + " [" + operationName
                    + "] failed after " + stopwatch.ElapsedMilliseconds + "ms", ex);
                throw new ModuleExecutionException(module, operationName, ex);
            }
        }

        public static void Run(ModuleDescriptor module, string stage, Action operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            Run(module, stage, () =>
            {
                operation();
                return true;
            });
        }
    }
}
