﻿using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Orleans.Runtime;
using Orleans.Runtime.Configuration;
using Orleans.Runtime.MultiClusterNetwork;
using Orleans.TestingHost;
using UnitTests.StorageTests;
using Orleans.MultiCluster;

namespace Tests.GeoClusterTests
{
    [TestClass]
    [DeploymentItem("OrleansAzureUtils.dll")]
    public class AzureGossipTableTests
    {
        private readonly TraceLogger logger;

        private string globalServiceId; //this should be the same for all clusters. Use this as partition key.
        //this should be unique per cluster. Can we use deployment id? 
        //problem with only using deployment id is that it is not known before deployment and hence not in the config file.
        private string deploymentId;
        private SiloAddress siloAddress1;
        private SiloAddress siloAddress2;
        private static readonly TimeSpan timeout = TimeSpan.FromMinutes(1);
        private AzureTableBasedGossipChannel gossipTable; // This type is internal

        public AzureGossipTableTests()
        {
            logger = TraceLogger.GetLogger("AzureGossipTableTests", TraceLogger.LoggerType.Application);
        }

        [ClassInitialize]
        public static void ClassInitialize(TestContext testContext)
        {
            TraceLogger.Initialize(new NodeConfiguration());
        }

        [TestInitialize]
        public void TestInitialize()
        {
            globalServiceId = "test-multiDC-gossip";
            deploymentId = "test-" + Guid.NewGuid();

            IPAddress ip;
            if (!IPAddress.TryParse("127.0.0.1", out ip))
            {
                logger.Error(-1, "Could not parse ip address");
                return;
            }
            IPEndPoint ep1 = new IPEndPoint(ip, 21111);
            siloAddress1 = SiloAddress.New(ep1, 0);
            IPEndPoint ep2 = new IPEndPoint(ip, 21112);
            siloAddress2 = SiloAddress.New(ep2, 0);

            logger.Info("DeploymentId={0}", deploymentId);

            GlobalConfiguration config = new GlobalConfiguration
            {
                GlobalServiceId = globalServiceId,
                ClusterId = "0",
                DeploymentId = deploymentId,
                DataConnectionString = StorageTestConstants.DataConnectionString
            };

            gossipTable = new AzureTableBasedGossipChannel();
            var done = gossipTable.Initialize(config, config.DataConnectionString);
            if (!done.Wait(timeout))
            {
                throw new TimeoutException("Could not create/read table.");
            }
        }

        [TestCleanup]
        public void TestCleanup()
        {
            if (gossipTable != null && SiloInstanceTableTestConstants.DeleteEntriesAfterTest)
            {
                gossipTable.DeleteAllEntries().Wait();
                gossipTable = null;
            }
        }

        [TestMethod, TestCategory("Functional"), TestCategory("GeoCluster"), TestCategory("Azure"), TestCategory("Storage")]
        public async Task AzureGossip_ConfigGossip()
        {
            // start clean
            await gossipTable.DeleteAllEntries();

            // push empty data
            await gossipTable.Push(new MultiClusterData());

            // push and pull empty data
            var answer = await gossipTable.PushAndPull(new MultiClusterData());
            Assert.IsTrue(answer.IsEmpty);

            var ts1 = new DateTime(year: 2011, month: 1, day: 1);
            var ts2 = new DateTime(year: 2012, month: 2, day: 2);
            var ts3 = new DateTime(year: 2013, month: 3, day: 3);

            var conf1 = new MultiClusterConfiguration(ts1, new string[] { "A" }.ToList(), "comment");
            var conf2 = new MultiClusterConfiguration(ts2, new string[] { "A", "B", "C" }.ToList());
            var conf3 = new MultiClusterConfiguration(ts3, new string[] { }.ToList());

            // push configuration 1
            await gossipTable.Push(new MultiClusterData(conf1));

            // retrieve (by push/pull empty)
            answer = await gossipTable.PushAndPull(new MultiClusterData());
            Assert.IsTrue(answer.Configuration.Equals(conf1));

            // gossip stable
            answer = await gossipTable.PushAndPull(new MultiClusterData(conf1));
            Assert.IsTrue(answer.IsEmpty);

            // push configuration 2
            answer = await gossipTable.PushAndPull(new MultiClusterData(conf2));
            Assert.IsTrue(answer.IsEmpty);

            // gossip returns latest
            answer = await gossipTable.PushAndPull(new MultiClusterData(conf1));
            Assert.IsTrue(answer.Configuration.Equals(conf2));
            await gossipTable.Push(new MultiClusterData(conf1));
            answer = await gossipTable.PushAndPull(new MultiClusterData());
            Assert.IsTrue(answer.Configuration.Equals(conf2));
            answer = await gossipTable.PushAndPull(new MultiClusterData(conf2));
            Assert.IsTrue(answer.IsEmpty);

            // push final configuration
            answer = await gossipTable.PushAndPull(new MultiClusterData(conf3));
            Assert.IsTrue(answer.IsEmpty);

            //Assert.IsTrue(false, "test how failing tests are reported");       
        }

        [TestMethod, TestCategory("Functional"), TestCategory("GeoCluster"), TestCategory("Azure"), TestCategory("Storage")]
        public async Task AzureGossip_GatewayGossip()
        {
            // start clean
            await gossipTable.DeleteAllEntries();

            var ts1 = DateTime.UtcNow;
            var ts2 = ts1 + new TimeSpan(hours: 0, minutes: 0, seconds: 1);
            var ts3 = ts1 + new TimeSpan(hours: 0, minutes: 0, seconds: 2);

            var G1 = new GatewayEntry()
            {
                SiloAddress = siloAddress1,
                ClusterId = "1",
                HeartbeatTimestamp = ts1,
                Status = GatewayStatus.Active
            };
            var G2 = new GatewayEntry()
            {
                SiloAddress = siloAddress1,
                ClusterId = "1",
                HeartbeatTimestamp = ts3,
                Status = GatewayStatus.Inactive
            };
            var H1 = new GatewayEntry()
            {
                SiloAddress = siloAddress2,
                ClusterId = "2",
                HeartbeatTimestamp = ts2,
                Status = GatewayStatus.Active
            };
            var H2 = new GatewayEntry()
            {
                SiloAddress = siloAddress2,
                ClusterId = "2",
                HeartbeatTimestamp = ts3,
                Status = GatewayStatus.Inactive
            };

            // push G1
            await gossipTable.Push(new MultiClusterData(G1));

            // push H1, retrieve G1 
            var answer = await gossipTable.PushAndPull(new MultiClusterData(H1));
            Assert.IsTrue(answer.Gateways.Count == 1);
            Assert.IsTrue(answer.Gateways.ContainsKey(siloAddress1));
            Assert.IsTrue(answer.Gateways[siloAddress1].Equals(G1));

            // push G2, retrieve H1
            answer = await gossipTable.PushAndPull(new MultiClusterData(G2));
            Assert.IsTrue(answer.Gateways.Count == 1);
            Assert.IsTrue(answer.Gateways.ContainsKey(siloAddress2));
            Assert.IsTrue(answer.Gateways[siloAddress2].Equals(H1));

            // gossip stable
            await gossipTable.Push(new MultiClusterData(H1));
            await gossipTable.Push(new MultiClusterData(G1));
            answer = await gossipTable.PushAndPull(new MultiClusterData(new GatewayEntry[] { H1, G2 }));
            Assert.IsTrue(answer.IsEmpty);

            // retrieve
            answer = await gossipTable.PushAndPull(new MultiClusterData(new GatewayEntry[] { H1, G2 }));
            Assert.IsTrue(answer.IsEmpty);

            // push H2 
            await gossipTable.Push(new MultiClusterData(H2));

            // retrieve all
            answer = await gossipTable.PushAndPull(new MultiClusterData(new GatewayEntry[] { G1, H1 }));
            Assert.IsTrue(answer.Gateways.Count == 2);
            Assert.IsTrue(answer.Gateways.ContainsKey(siloAddress1));
            Assert.IsTrue(answer.Gateways.ContainsKey(siloAddress2));
            Assert.IsTrue(answer.Gateways[siloAddress1].Equals(G2));
            Assert.IsTrue(answer.Gateways[siloAddress2].Equals(H2));
        }
    }
}
