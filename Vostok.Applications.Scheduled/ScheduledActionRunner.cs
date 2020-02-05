﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vostok.Commons.Time;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Context;

// ReSharper disable MethodSupportsCancellation

namespace Vostok.Applications.Scheduled
{
    internal class ScheduledActionRunner
    {
        private readonly ScheduledAction action;
        private readonly ILog log;

        public ScheduledActionRunner(ScheduledAction action, ILog log)
        {
            this.action = action;
            this.log = log.ForContext("Scheduler");
        }

        public async Task RunAsync(CancellationToken token)
        {
            var lastExecutionTime = PreciseDateTime.Now;
            var iteration = 0L;

            using (new OperationContextToken(action.Name))
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        using (new OperationContextToken($"{++iteration}"))
                        {
                            await WaitForNextExecutionAsync(lastExecutionTime, token).ConfigureAwait(false);

                            lastExecutionTime = PreciseDateTime.Now;

                            await ExecutePayloadAsync(lastExecutionTime, token).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                }

                log.Info("Finished executing.");
            }
        }

        private async Task WaitForNextExecutionAsync(DateTimeOffset lastExecutionTime, CancellationToken token)
        {
            var nextExecutionTime = null as DateTimeOffset?;
            var firstActualizationDone = false;

            while (!token.IsCancellationRequested)
            {
                var newNextExecutionTime = GetNextExecutionTime(lastExecutionTime);
                if (newNextExecutionTime != nextExecutionTime || !firstActualizationDone)
                    LogNextExecutionTime(nextExecutionTime = newNextExecutionTime);

                firstActualizationDone = true;

                if (nextExecutionTime == null)
                {
                    await Task.Delay(action.Options.ActualizationPeriod, token).ConfigureAwait(false);
                    continue;
                }

                if (nextExecutionTime <= lastExecutionTime)
                    return;

                var timeToWait = TimeSpanArithmetics.Max(TimeSpan.Zero, nextExecutionTime.Value - PreciseDateTime.Now);
                if (timeToWait > action.Options.ActualizationPeriod)
                {
                    await Task.Delay(action.Options.ActualizationPeriod, token).ConfigureAwait(false);
                    continue;
                }

                if (timeToWait > TimeSpan.Zero)
                    await Task.Delay(timeToWait, token).ConfigureAwait(false);

                while (PreciseDateTime.Now < nextExecutionTime)
                    await Task.Delay(1.Milliseconds(), token).ConfigureAwait(false);

                return;
            }
        }

        private async Task ExecutePayloadAsync(DateTimeOffset executionTime, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            var nextExecution = GetNextExecutionTime(executionTime);

            var timeBudget = nextExecution.HasValue
                ? TimeBudget.StartNew(TimeSpanArithmetics.Max(TimeSpan.Zero, nextExecution.Value - executionTime))
                : TimeBudget.Infinite;

            var context = new ScheduledActionContext(timeBudget, token);

            log.Info("Executing with time budget = {TimeBudget}.", timeBudget.Total.ToPrettyString());

            async Task ExecutePayload()
            {
                try
                {
                    var watch = Stopwatch.StartNew();

                    await action.Payload(context).ConfigureAwait(false);

                    watch.Stop();

                    log.Info("Executed in {ExecutionTime}.", new
                    {
                        ExecutionTime = watch.Elapsed.ToPrettyString(),
                        ExecutionTimeMs = watch.Elapsed.TotalMilliseconds
                    });

                    if (watch.Elapsed > timeBudget.Total)
                        log.Warn("Execution did not fit into the time budget before the next planned execution.");
                }
                catch (Exception error)
                {
                    if (action.Options.CrashOnPayloadException || error is OperationCanceledException)
                        throw;

                    log.Error(error, "Scheduled action threw an exception.");
                }
            }

            var payloadTask = action.Options.PreferSeparateThread
                ? Task.Factory.StartNew(ExecutePayload, TaskCreationOptions.LongRunning)
                : Task.Run(ExecutePayload);

            if (action.Options.AllowOverlappingExecution)
                return;

            await payloadTask.ConfigureAwait(false);
        }

        private DateTimeOffset? GetNextExecutionTime(DateTimeOffset from)
        {
            try
            {
                return action.Scheduler.ScheduleNext(from);
            }
            catch (Exception error)
            {
                if (action.Options.CrashOnSchedulerException)
                    throw;

                log.Error(error, "Scheduler failure. Can't schedule next iteration.");

                return null;
            }
        }

        private void LogNextExecutionTime(DateTimeOffset? nextExecutionTime)
        {
            if (nextExecutionTime == null)
                log.Warn("Next execution time: unknown.");
            else
                log.Info("Next execution time = {NextExecutionTime:yyyy-MM-dd HH:mm:ss.fff} (~{TimeToNextExecution} from now).", 
                    nextExecutionTime.Value.DateTime, TimeSpanArithmetics.Max(TimeSpan.Zero, nextExecutionTime.Value - PreciseDateTime.Now).ToPrettyString());
        }
    }
}