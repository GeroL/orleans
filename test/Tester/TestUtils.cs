﻿using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Orleans;
using Orleans.Runtime;
using Orleans.TestingHost;
using Orleans.TestingHost.Utils;
using Xunit;

namespace Tester
{
    public class TestUtils
    {
        public static readonly Random Random = new Random();

        public static long GetRandomGrainId()
        {
            return Random.Next();
        }

        public static void CheckForAzureStorage()
        {
            bool usingLocalWAS = StorageTestConstants.UsingAzureLocalStorageEmulator;

            if (!usingLocalWAS)
            {
                // Tests are using Azure Cloud Storage, not local WAS emulator.
                return;
            }

            //Starts the storage emulator if not started already and it exists (i.e. is installed).
            if (!StorageEmulator.TryStart())
            {
                string errorMsg = "Azure Storage Emulator could not be started.";
                Console.WriteLine(errorMsg);
                throw new SkipException(errorMsg);
            }
        }

        public static string DumpTestContext(TestContext context)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(@"TestName={0}", context.TestName).AppendLine();
            sb.AppendFormat(@"FullyQualifiedTestClassName={0}", context.FullyQualifiedTestClassName).AppendLine();
            sb.AppendFormat(@"CurrentTestOutcome={0}", context.CurrentTestOutcome).AppendLine();
            // Those fields doesn't exist on MSTest apis for DNX. 
            //sb.AppendFormat(@"DeploymentDirectory={0}", context.DeploymentDirectory).AppendLine();
            //sb.AppendFormat(@"TestRunDirectory={0}", context.TestRunDirectory).AppendLine();
            //sb.AppendFormat(@"TestResultsDirectory={0}", context.TestResultsDirectory).AppendLine();
            //sb.AppendFormat(@"ResultsDirectory={0}", context.ResultsDirectory).AppendLine();
            //sb.AppendFormat(@"TestRunResultsDirectory={0}", context.TestRunResultsDirectory).AppendLine();
            sb.AppendFormat(@"Properties=[ ");
            foreach (var key in context.Properties.Keys)
            {
                sb.AppendFormat(@"{0}={1} ", key, context.Properties[key]);
            }
            sb.AppendFormat(@" ]").AppendLine();
            return sb.ToString();
        }

        public static double CalibrateTimings()
        {
            const int NumLoops = 10000;
            TimeSpan baseline = TimeSpan.FromTicks(80); // Baseline from jthelin03D
            int n;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < NumLoops; i++)
            {
                n = i;
            }
            sw.Stop();
            double multiple = 1.0 * sw.ElapsedTicks / baseline.Ticks;
            Console.WriteLine("CalibrateTimings: {0} [{1} Ticks] vs {2} [{3} Ticks] = x{4}",
                sw.Elapsed, sw.ElapsedTicks,
                baseline, baseline.Ticks,
                multiple);
            return multiple > 1.0 ? multiple : 1.0;
        }

        public static TimeSpan TimeRun(int numIterations, TimeSpan baseline, string what, Action action)
        {
            var stopwatch = new Stopwatch();

            long startMem = GC.GetTotalMemory(true);
            stopwatch.Start();

            action();

            stopwatch.Stop();
            long stopMem = GC.GetTotalMemory(false);
            long memUsed = stopMem - startMem;
            TimeSpan duration = stopwatch.Elapsed;

            string timeDeltaStr = "";
            if (baseline > TimeSpan.Zero)
            {
                double delta = (duration - baseline).TotalMilliseconds / baseline.TotalMilliseconds;
                timeDeltaStr = String.Format("-- Change = {0}%", 100.0 * delta);
            }
            Console.WriteLine("Time for {0} loops doing {1} = {2} {3} Memory used={4}", numIterations, what, duration, timeDeltaStr, memUsed);
            return duration;
        }

        public static void ConfigureClientThreadPoolSettingsForStorageTests(int NumDotNetPoolThreads = 200)
        {
            ThreadPool.SetMinThreads(NumDotNetPoolThreads, NumDotNetPoolThreads);
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = NumDotNetPoolThreads; // 1000;
            ServicePointManager.UseNagleAlgorithm = false;
        }

        public static async Task<int> GetActivationCount(string fullTypeName)
        {
            int result = 0;

            IManagementGrain mgmtGrain = GrainClient.GrainFactory.GetGrain<IManagementGrain>(RuntimeInterfaceConstants.SYSTEM_MANAGEMENT_ID);
            SimpleGrainStatistic[] stats = await mgmtGrain.GetSimpleGrainStatistics();
            foreach (var stat in stats)
            {
                if (stat.GrainType == fullTypeName)
                    result += stat.ActivationCount;
            }
            return result;
        }
    }
}
