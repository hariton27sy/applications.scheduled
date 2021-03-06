﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Logging.Abstractions;

namespace Vostok.Applications.Scheduled
{
    [PublicAPI]
    public class ScheduledActionsBuilder : IScheduledActionsBuilder
    {
        private readonly ILog log;
        private readonly List<ScheduledAction> actions;

        public ScheduledActionsBuilder(ILog log)
        {
            this.log = log.ForContext("Scheduler");

            actions = new List<ScheduledAction>();
        }

        public IScheduledActionsRunner BuildRunner()
            => BuildRunnerInternal();

        public IScheduledActionsBuilder Schedule(string name, IScheduler scheduler, Action<IScheduledActionContext> payload)
            => Schedule(name, scheduler, payload, new ScheduledActionOptions());

        public IScheduledActionsBuilder Schedule(string name, IScheduler scheduler, Func<IScheduledActionContext, Task> payload)
            => Schedule(name, scheduler, payload, new ScheduledActionOptions());

        public IScheduledActionsBuilder Schedule(string name, IScheduler scheduler, Action<IScheduledActionContext> payload, ScheduledActionOptions options)
            => Schedule(name, scheduler, WrapAction(payload), options);

        public IScheduledActionsBuilder Schedule(string name, IScheduler scheduler, Func<IScheduledActionContext, Task> payload, ScheduledActionOptions options)
        {
            actions.Add(new ScheduledAction(name, scheduler, options, payload));

            log.Info("Scheduled '{ActionName}' action with scheduler '{Scheduler}'. ", name, scheduler.GetType().Name);

            return this;
        }

        internal ScheduledActionsRunner BuildRunnerInternal()
            => new ScheduledActionsRunner(actions.Select(action => new ScheduledActionRunner(action, log)).ToArray(), log);

        private static Func<IScheduledActionContext, Task> WrapAction(Action<IScheduledActionContext> action)
        {
            return context =>
            {
                action(context);
                return Task.CompletedTask;
            };
        }
    }
}
