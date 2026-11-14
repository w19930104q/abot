using Abot2.Util;
using System.Collections.Generic;

namespace Abot2.Tests.Unit.Util
{
    /// <summary>
    /// Bloom filter test double with fully controllable false positives.
    /// Contains() returns true for anything that was added, for anything
    /// explicitly registered via <see cref="RegisterFalsePositive"/>, and for
    /// everything when <see cref="AlwaysPositive"/> is set.
    /// </summary>
    public class ControllableFalsePositiveBloomFilter : IBloomFilter<string>
    {
        private readonly HashSet<string> _added = new HashSet<string>();
        private readonly HashSet<string> _falsePositives = new HashSet<string>();

        /// <summary>
        /// When true, Contains() returns true for every item, simulating a
        /// saturated filter where every lookup is a false positive.
        /// </summary>
        public bool AlwaysPositive { get; set; }

        public void RegisterFalsePositive(string item)
        {
            _falsePositives.Add(item);
        }

        public void Add(string item)
        {
            _added.Add(item);
        }

        public bool Contains(string item)
        {
            return AlwaysPositive || _added.Contains(item) || _falsePositives.Contains(item);
        }
    }
}
