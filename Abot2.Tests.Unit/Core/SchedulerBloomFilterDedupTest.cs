using Abot2.Core;
using Abot2.Poco;
using Abot2.Tests.Unit.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Abot2.Tests.Unit.Core
{
    /// <summary>
    /// Verifies that the Scheduler, backed by a <see cref="BloomFilterCrawledUrlRepository"/>,
    /// neither drops candidates because of bloom filter false positives nor schedules
    /// duplicates.
    /// </summary>
    [TestClass]
    public class SchedulerBloomFilterDedupTest
    {
        ControllableFalsePositiveBloomFilter _filter;

        [TestInitialize]
        public void SetUp()
        {
            _filter = new ControllableFalsePositiveBloomFilter();
        }

        Scheduler CreateScheduler()
        {
            return new Scheduler(false, new BloomFilterCrawledUrlRepository(_filter), new FifoPagesToCrawlRepository());
        }

        [TestMethod]
        public void Add_FilterFalsePositivesOnEveryUri_NoCandidateLost()
        {
            _filter.AlwaysPositive = true;//worst case: every lookup is a false positive
            var pages = Enumerable.Range(0, 500)
                .Select(i => new PageToCrawl { Uri = new Uri("http://site" + i + ".com/") })
                .ToList();

            using (var scheduler = CreateScheduler())
            {
                scheduler.Add(pages);

                Assert.AreEqual(pages.Count, scheduler.Count, "False positives must not cause candidates to be dropped");

                var retrieved = new List<PageToCrawl>();
                PageToCrawl next;
                while ((next = scheduler.GetNext()) != null)
                    retrieved.Add(next);

                CollectionAssert.AreEquivalent(
                    pages.Select(p => p.Uri.AbsoluteUri).ToList(),
                    retrieved.Select(p => p.Uri.AbsoluteUri).ToList());
            }
        }

        [TestMethod]
        public void Add_DuplicatePages_ScheduledOnlyOnce()
        {
            var pages = new List<PageToCrawl>();
            for (var i = 0; i < 100; i++)
            {
                pages.Add(new PageToCrawl { Uri = new Uri("http://dup" + i + ".com/") });
                pages.Add(new PageToCrawl { Uri = new Uri("http://dup" + i + ".com/") });
                pages.Add(new PageToCrawl { Uri = new Uri("http://dup" + i + ".com/") });
            }

            using (var scheduler = CreateScheduler())
            {
                scheduler.Add(pages);

                Assert.AreEqual(100, scheduler.Count, "Each distinct uri must be scheduled exactly once");

                var seen = new HashSet<string>();
                PageToCrawl next;
                while ((next = scheduler.GetNext()) != null)
                    Assert.IsTrue(seen.Add(next.Uri.AbsoluteUri), "Duplicate crawl of " + next.Uri.AbsoluteUri);

                Assert.AreEqual(100, seen.Count);
            }
        }

        [TestMethod]
        public void Add_ConcurrentProducersWithDuplicates_NoLossNoDuplicates()
        {
            _filter.AlwaysPositive = true;
            var distinct = Enumerable.Range(0, 300)
                .Select(i => new Uri("http://concurrent" + i + ".com/"))
                .ToList();

            using (var scheduler = CreateScheduler())
            {
                //Multiple producers all racing to schedule the same set of uris
                Parallel.For(0, 8, _ =>
                {
                    foreach (var uri in distinct)
                        scheduler.Add(new PageToCrawl { Uri = uri });
                });

                Assert.AreEqual(distinct.Count, scheduler.Count, "Every distinct candidate must be scheduled exactly once");

                var seen = new HashSet<string>();
                PageToCrawl next;
                while ((next = scheduler.GetNext()) != null)
                    Assert.IsTrue(seen.Add(next.Uri.AbsoluteUri), "Duplicate crawl of " + next.Uri.AbsoluteUri);

                Assert.AreEqual(distinct.Count, seen.Count);
            }
        }

        [TestMethod]
        public void IsUriKnown_FilterFalsePositiveOnUnknownUri_ReturnsFalse()
        {
            var unknown = new Uri("http://unknown.com/");
            _filter.RegisterFalsePositive(unknown.AbsoluteUri);

            using (var scheduler = CreateScheduler())
            {
                Assert.IsFalse(scheduler.IsUriKnown(unknown), "False positive must not make an unknown uri appear known");

                scheduler.AddKnownUri(unknown);
                Assert.IsTrue(scheduler.IsUriKnown(unknown));
            }
        }
    }
}
