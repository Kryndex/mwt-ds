﻿using Microsoft.Research.MultiWorldTesting.ClientLibrary;
using Microsoft.Research.MultiWorldTesting.JoinUploader;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Text;
using System.Net;

namespace Microsoft.Research.DecisionServiceTest
{
    [TestClass]
    public class SimplePolicyHttpTestClass
    {
        private const string contextType = "policy";

        private Dictionary<string, int> freq;
        private string[] features;
        private Random rnd;
        private int eventCount;

        WebClient wc;

        public SimplePolicyHttpTestClass()
        {

        }

        [TestMethod]
        [Ignore]
        [TestCategory("End to End")]
        [Priority(2)]
        public void SimplePolicyHttpTest()
        {
            var deployment = new ProvisioningUtil().Deploy();

            wc = new WebClient();
            wc.Headers.Add("auth", deployment.ManagementPassword);

            deployment.ConfigureDecisionService("--cb_explore 4 --epsilon 1", initialExplorationEpsilon: 1, isExplorationEnabled: true);
            Thread.Sleep(TimeSpan.FromSeconds(5));

            // 4 Actions
            // why does this need to be different from default?
            var config = new DecisionServiceConfiguration(deployment.SettingsUrl)
            {
                InteractionUploadConfiguration = new BatchingConfiguration
                {
                    MaxEventCount = 64
                },
                ObservationUploadConfiguration = new BatchingConfiguration
                {
                    MaxEventCount = 64
                }
            };

            config.InteractionUploadConfiguration.ErrorHandler += JoinServiceBatchConfiguration_ErrorHandler;
            config.InteractionUploadConfiguration.SuccessHandler += JoinServiceBatchConfiguration_SuccessHandler;
            this.features = new string[] { "a", "b", "c", "d" };
            this.freq = new Dictionary<string, int>();
            this.rnd = new Random(123);

            // reset the model
            deployment.OnlineTrainerReset();

            Console.WriteLine("Waiting after reset...");
            Thread.Sleep(TimeSpan.FromSeconds(2));

            Console.WriteLine("Exploration");
            var expectedEvents = 0;
            for (int i = 0; i < 1000; i++)
            {
                int featureIndex = i % features.Length;
                expectedEvents += SendEvents(deployment, wc, featureIndex);
            }
            // Thread.Sleep(500);                        
            // TODO: flush doesn't work
            // Assert.AreEqual(expectedEvents, this.eventCount);

            // 4 actions times 4 feature values
            Assert.AreEqual(4 * 4, freq.Keys.Count);

            var total = freq.Values.Sum();
            foreach (var k in freq.Keys.OrderBy(k => k))
            {
                var f = freq[k] / (float)total;
                Assert.IsTrue(f < 0.08); // 1/(4*4) = 0.0625
                Console.WriteLine("{0} | {1}", k, f);
            }

            freq.Clear();

            deployment.ConfigureDecisionService("--cb_explore 4 --epsilon 0", initialExplorationEpsilon: 1, isExplorationEnabled: false);
            Thread.Sleep(TimeSpan.FromSeconds(5));

            // check here to make sure model was updated
            Console.WriteLine("Exploitation");
            expectedEvents = 0;
            for (int i = 0; i < 1000; i++)
            {
                var featureIndex = i % features.Length;
                expectedEvents += SendEvents(deployment, wc, featureIndex, false);
            }

            total = freq.Values.Sum();
            foreach (var k in freq.Keys.OrderBy(k => k))
            {
                var f = freq[k] / (float)total;
                Assert.AreEqual(0.25f, f, 0.1);
                Console.WriteLine("{0} | {1}", k, f);
            }
        }

        void JoinServiceBatchConfiguration_SuccessHandler(object source, int eventCount, int sumSize, int inputQueueSize)
        {
            this.eventCount += eventCount;
        }

        void JoinServiceBatchConfiguration_ErrorHandler(object source, Exception e)
        {
            Assert.Fail("Exception during upload: " + e.Message);
        }

        public class MyContext
        {
            public string Feature { get; set; }
        }

        public JObject InteractionParts1and2(DecisionServiceDeployment deployment, string contextType, string contextString)
        {
            string contextUri = string.Format(CultureInfo.InvariantCulture, "{0}/API/{1}", deployment.ManagementCenterUrl, contextType);
            byte[] context = System.Text.Encoding.ASCII.GetBytes(contextString);
            var response = wc.UploadData(contextUri, "POST", context);
            var utf8response = UnicodeEncoding.UTF8.GetString(response);
            JObject responseJObj = JObject.Parse(utf8response);
            return responseJObj;
        }

        public string InteractionPart3(DecisionServiceDeployment deployment, JObject responseJObj, float reward)
        {
            string eventID = (string)responseJObj["EventId"];
            string rewardUri = string.Format(CultureInfo.InvariantCulture, "{0}/API/reward/?eventId={1}", deployment.ManagementCenterUrl, eventID);
            string rewardString = reward.ToString();
            byte[] rewardBytes = System.Text.Encoding.ASCII.GetBytes(rewardString);
            var response = wc.UploadData(rewardUri, "POST", rewardBytes);
            string utf8response = UnicodeEncoding.UTF8.GetString(response);
            return utf8response;
        }

        private int SendEvents(DecisionServiceDeployment deployment, WebClient client, int featureIndex, bool sendReward = true)
        {
            const float reward = 2.0F;

            var expectedEvents = 0;
            string contextString = $"{{a: \"{features[featureIndex]}\"}}";
            var responseJObj = InteractionParts1and2(deployment, contextType, contextString);
            int action = (int)responseJObj["Action"];

            // Feature | Action
            //    A   -->  1
            //    B   -->  2
            //    C   -->  3
            //    D   -->  4
            // only report in 50% of the cases
            if (sendReward && rnd.NextDouble() < .75 && action - 1 == featureIndex)
            {
                InteractionPart3(deployment, responseJObj, reward);
                expectedEvents = 1;
            }

            var stat = string.Format("'{0}' '{1}' ", features[featureIndex], action);
            int count;
            if (freq.TryGetValue(stat, out count))
                freq[stat]++;
            else
                freq.Add(stat, count);

            return expectedEvents;
        }

    }
}