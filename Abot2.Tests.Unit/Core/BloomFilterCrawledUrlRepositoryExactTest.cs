using Abot2.Core;
using Abot2.Tests.Unit.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Abot2.Tests.Unit.Core
{
    /// <summary>
    /// Verifies the exact confirmation chain of <see cref="BloomFilterCrawledUrlRepository"/>:
    /// a uri may only be reported as "seen" after exact confirmation, AddIfNew must be
    /// thread safe, memory must stay bounded (overflow spills to temp disk) and Dispose
    /// must clean up the spill files.
    /// </summary>
    [TestClass]
    public class BloomFilterCrawledUrlRepositoryExactTest
    {
        ControllableFalsePositiveBloomFilter _filter;

        [TestInitialize]
        public void SetUp()
        {
            _filter = new ControllableFalsePositiveBloomFilter();
        }

        [TestMethod]
        public void Contains_FilterFalsePositiveOnNeverAddedUri_ReturnsFalse()
        {
            var uri = new Uri("http://neveradded.com/");
            _filter.RegisterFalsePositive(uri.AbsoluteUri);

            using (var repo = new BloomFilterCrawledUrlRepository(_filter))
            {
                Assert.IsFalse(repo.Contains(uri), "False positive from the filter must not be reported as seen");
            }
        }

        [TestMethod]
        public void AddIfNew_FilterFalsePositiveOnNeverAddedUri_StillReturnsTrue()
        {
            var uri = new Uri("http://falsepositive.com/");
            _filter.RegisterFalsePositive(uri.AbsoluteUri);

            using (var repo = new BloomFilterCrawledUrlRepository(_filter))
            {
                Assert.IsTrue(repo.AddIfNew(uri), "False positive from the filter must not cause the uri to be dropped");
                Assert.IsTrue(repo.Contains(uri));
            }
        }

        [TestMethod]
        public void AddIfNew_ActualDuplicate_StillReturnsFalse()
        {
            var uri = new Uri("http://duplicate.com/");

            using (var repo = new BloomFilterCrawledUrlRepository(_filter))
            {
                Assert.IsTrue(repo.AddIfNew(uri));
                Assert.IsFalse(repo.AddIfNew(uri));
                Assert.IsFalse(repo.AddIfNew(new Uri("http://duplicate.com/")));
            }
        }

        [TestMethod]
        public void Contains_FilterSaturated_OnlyAddedUrisReturnTrue()
        {
            _filter.AlwaysPositive = true;

            using (var repo = new BloomFilterCrawledUrlRepository(_filter))
            {
                var added = new Uri("http://added.com/");
                Assert.IsTrue(repo.AddIfNew(added));

                Assert.IsTrue(repo.Contains(added));
                Assert.IsFalse(repo.Contains(new Uri("http://notadded.com/")));
            }
        }

        [TestMethod]
        public void AddIfNew_ConcurrentSameUri_ExactlyOneSucceeds()
        {
            using (var repo = new BloomFilterCrawledUrlRepository(_filter))
            {
                var uri = new Uri("http://contended.com/");
                var successCount = 0;

                Parallel.For(0, 128, _ =>
                {
                    if (repo.AddIfNew(uri))
                        Interlocked.Increment(ref successCount);
                });

                Assert.AreEqual(1, successCount, "Concurrent adds of the same uri must succeed exactly once");
                Assert.IsTrue(repo.Contains(uri));
            }
        }

        [TestMethod]
        public void AddIfNew_ConcurrentDistinctUris_FilterSaturated_NoneLost()
        {
            _filter.AlwaysPositive = true;

            using (var repo = new BloomFilterCrawledUrlRepository(_filter))
            {
                var uris = Enumerable.Range(0, 2000)
                    .Select(i => new Uri("http://host" + i + ".com/"))
                    .ToList();
                var successes = new ConcurrentBag<Uri>();

                Parallel.ForEach(uris, uri =>
                {
                    if (repo.AddIfNew(uri))
                        successes.Add(uri);
                });

                Assert.AreEqual(uris.Count, successes.Count, "Every distinct uri must be accepted exactly once, even when the filter always false-positives");
                foreach (var uri in uris)
                    Assert.IsTrue(repo.Contains(uri), "Lost uri " + uri.AbsoluteUri);
            }
        }

        [TestMethod]
        public void AddIfNew_ExceedsInMemoryBound_SpillsToDiskAndStaysExact()
        {
            var spillRoot = Path.Combine(Path.GetTempPath(), "Abot2Tests", Guid.NewGuid().ToString("N"));
            var uris = Enumerable.Range(0, 35)
                .Select(i => new Uri("http://overflow" + i + ".com/"))
                .ToList();

            using (var repo = new BloomFilterCrawledUrlRepository(_filter, 10, spillRoot))
            {
                foreach (var uri in uris)
                    Assert.IsTrue(repo.AddIfNew(uri));

                Assert.IsTrue(repo.SpillFileCount > 0, "Overflow beyond the memory bound must spill to disk");
                Assert.IsTrue(Directory.Exists(repo.SpillDirectory));
                Assert.IsTrue(repo.InMemoryEntryCount <= 10, "In memory entries must stay bounded");

                foreach (var uri in uris)
                    Assert.IsTrue(repo.Contains(uri), "Spilled uri must still be found: " + uri.AbsoluteUri);

                foreach (var uri in uris)
                    Assert.IsFalse(repo.AddIfNew(uri), "Duplicate of spilled uri must be rejected: " + uri.AbsoluteUri);

                Assert.IsFalse(repo.Contains(new Uri("http://neverspilled.com/")));
            }
        }

        [TestMethod]
        public void Dispose_WithSpillFiles_DeletesSpillDirectory()
        {
            string spillDirectory;
            var repo = new BloomFilterCrawledUrlRepository(_filter, 5, null);

            for (var i = 0; i < 20; i++)
                Assert.IsTrue(repo.AddIfNew(new Uri("http://cleanup" + i + ".com/")));

            spillDirectory = repo.SpillDirectory;
            Assert.IsTrue(repo.SpillFileCount > 0);
            Assert.IsTrue(Directory.Exists(spillDirectory));

            repo.Dispose();

            Assert.IsFalse(Directory.Exists(spillDirectory), "Dispose must delete the spill directory and its files");
        }

        [TestMethod]
        public void Dispose_WithoutSpill_DoesNotThrow()
        {
            var repo = new BloomFilterCrawledUrlRepository(_filter);
            repo.AddIfNew(new Uri("http://a.com/"));

            repo.Dispose();
            repo.Dispose();//must be idempotent
        }

        [TestMethod]
        public void Constructor_InvalidMemoryBound_Throws()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new BloomFilterCrawledUrlRepository(_filter, 0));
        }

        [TestMethod]
        public void AddIfNew_NullUri_ReturnsFalse()
        {
            using (var repo = new BloomFilterCrawledUrlRepository(_filter))
            {
                Assert.IsFalse(repo.AddIfNew(null));
                Assert.IsFalse(repo.Contains(null));
            }
        }
    }
}
