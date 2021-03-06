﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Applications.Scheduled.Diagnostics;
using Vostok.Hosting.Abstractions;
using Vostok.Hosting.Abstractions.Diagnostics;

namespace Vostok.Applications.Scheduled
{
    [PublicAPI]
    public abstract class VostokScheduledApplication : IVostokApplication, IDisposable
    {
        private readonly List<IDisposable> disposables = new List<IDisposable>();

        private volatile ScheduledActionsRunner runner;

        public abstract void Setup([NotNull] IScheduledActionsBuilder builder, [NotNull] IVostokHostingEnvironment environment);

        public Task InitializeAsync(IVostokHostingEnvironment environment)
        {
            var builder = new ScheduledActionsBuilder(environment.Log);

            Setup(builder, environment);

            runner = builder.BuildRunnerInternal();

            RegisterDiagnosticFeatures(environment);

            return Task.CompletedTask;
        }

        public Task RunAsync(IVostokHostingEnvironment environment)
            => runner.RunAsync(environment.ShutdownToken);

        public void Dispose()
        {
            disposables.ForEach(disposable => disposable.Dispose());
            DoDispose();
        }

        public virtual void DoDispose()
        {
        }

        private void RegisterDiagnosticFeatures(IVostokHostingEnvironment environment)
        {
            if (!environment.HostExtensions.TryGet<IVostokApplicationDiagnostics>(out var diagnostics))
                return;

            foreach (var actionRunner in runner.Runners)
            {
                var info = actionRunner.GetInfo();
                var infoEntry = new DiagnosticEntry("scheduled", info.Name);
                var infoProvider = new ScheduledActionsInfoProvider(actionRunner);
                var healthCheck = new ScheduledActionsHealthCheck(actionRunner);

                disposables.Add(diagnostics.Info.RegisterProvider(infoEntry, infoProvider));
                disposables.Add(diagnostics.HealthTracker.RegisterCheck($"scheduled ({info.Name})", healthCheck));
            }
        }
    }
}
