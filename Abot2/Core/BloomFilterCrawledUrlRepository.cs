using Abot2.Util;
using System;

namespace Abot2.Core
{
    /// <summary>
    /// Uses a bloom filter as a fast negative check in front of an exact,
    /// bounded-memory confirmation store. A uri is only reported as "seen" when
    /// the exact store confirms it, so bloom filter false positives can never
    /// cause a never-crawled uri to be treated as already crawled (which would
    /// silently drop pages from the schedule).
    /// </summary>
    public class BloomFilterCrawledUrlRepository : ICrawledUrlRepository
    {
        internal const int DefaultMaxInMemoryEntries = 1000000;

        protected IBloomFilter<string> BloomFilter { get; set; }

        private readonly object _syncRoot = new object();
        private readonly SpillableExactUriStore _exactStore;

        public BloomFilterCrawledUrlRepository()
            : this(null)
        {

        }

        public BloomFilterCrawledUrlRepository(IBloomFilter<string> bloomFilter)
            : this(bloomFilter, DefaultMaxInMemoryEntries)
        {

        }

        /// <summary>
        /// Creates an instance whose exact confirmation store keeps at most
        /// <paramref name="maxInMemoryEntries"/> uris in memory and spills any
        /// overflow to temporary files on disk.
        /// </summary>
        /// <param name="bloomFilter">The filter used for fast negative checks. If null a default instance is used.</param>
        /// <param name="maxInMemoryEntries">Maximum number of uris held in memory before overflowing to disk.</param>
        public BloomFilterCrawledUrlRepository(IBloomFilter<string> bloomFilter, int maxInMemoryEntries)
            : this(bloomFilter, maxInMemoryEntries, null)
        {

        }

        /// <summary>
        /// Creates an instance whose exact confirmation store keeps at most
        /// <paramref name="maxInMemoryEntries"/> uris in memory and spills any
        /// overflow to temporary files under <paramref name="spillDirectory"/>.
        /// The spill files are deleted when this instance is disposed.
        /// </summary>
        /// <param name="bloomFilter">The filter used for fast negative checks. If null a default instance is used.</param>
        /// <param name="maxInMemoryEntries">Maximum number of uris held in memory before overflowing to disk.</param>
        /// <param name="spillDirectory">Root directory for spill files. If null the system temp directory is used.</param>
        public BloomFilterCrawledUrlRepository(IBloomFilter<string> bloomFilter, int maxInMemoryEntries, string spillDirectory)
        {
            BloomFilter = bloomFilter ?? new BloomFilter<string>(2000001, 0.001F);
            _exactStore = new SpillableExactUriStore(maxInMemoryEntries, spillDirectory);
        }

        internal string SpillDirectory
        {
            get { return _exactStore.SpillDirectory; }
        }

        internal int InMemoryEntryCount
        {
            get { return _exactStore.InMemoryCount; }
        }

        internal int SpillFileCount
        {
            get { return _exactStore.SpillFileCount; }
        }

        public bool Contains(Uri uri)
        {
            if (uri == null)
                return false;

            lock (_syncRoot)
            {
                var key = uri.AbsoluteUri;

                //A bloom filter negative is definitive, but a positive is only a
                //"maybe" and must be confirmed by the exact store before the uri
                //can be reported as seen.
                return BloomFilter.Contains(key) && _exactStore.Contains(key);
            }
        }

        public bool AddIfNew(Uri uri)
        {
            if (uri == null)
                return false;

            lock (_syncRoot)
            {
                var key = uri.AbsoluteUri;

                //Check-and-add is atomic under the lock, so concurrent adds of the
                //same uri can only succeed once.
                if (BloomFilter.Contains(key) && _exactStore.Contains(key))
                    return false;

                BloomFilter.Add(key);
                _exactStore.Add(key);

                return true;
            }
        }

        public void Dispose()
        {
            //Deletes any spill files written to the temp directory
            _exactStore.Dispose();
        }
    }
}
