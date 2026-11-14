using System;
using System.Collections.Generic;
using System.IO;

namespace Abot2.Core
{
    /// <summary>
    /// Exact (no false positives, no false negatives) store of uri strings with a
    /// bounded in-memory footprint. Once the in-memory set reaches the configured
    /// bound it is flushed to a spill file in a temporary directory and the memory
    /// is released. All spill files are deleted when <see cref="Dispose"/> is called.
    /// </summary>
    /// <remarks>
    /// Not thread safe. Callers must synchronize access.
    /// </remarks>
    internal sealed class SpillableExactUriStore : IDisposable
    {
        private readonly int _maxInMemoryEntries;
        private readonly List<string> _spillFiles = new List<string>();
        private HashSet<string> _entries;
        private bool _disposed;

        public SpillableExactUriStore(int maxInMemoryEntries, string spillDirectory)
        {
            if (maxInMemoryEntries < 1)
                throw new ArgumentOutOfRangeException("maxInMemoryEntries", maxInMemoryEntries, "maxInMemoryEntries must be > 0");

            _maxInMemoryEntries = maxInMemoryEntries;
            _entries = new HashSet<string>(StringComparer.Ordinal);

            var root = string.IsNullOrWhiteSpace(spillDirectory)
                ? Path.Combine(Path.GetTempPath(), "Abot2")
                : spillDirectory;

            //Always work in a unique subdirectory so Dispose can safely delete it
            SpillDirectory = Path.Combine(root, "ExactUriStore-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Directory that holds the spill files. Created lazily on the first spill
        /// and deleted (recursively) on <see cref="Dispose"/>.
        /// </summary>
        public string SpillDirectory { get; private set; }

        /// <summary>
        /// Number of entries currently held in memory. Never exceeds the configured bound.
        /// </summary>
        public int InMemoryCount
        {
            get { return _entries.Count; }
        }

        /// <summary>
        /// Number of spill files written to disk so far.
        /// </summary>
        public int SpillFileCount
        {
            get { return _spillFiles.Count; }
        }

        public bool Contains(string uri)
        {
            if (_entries.Contains(uri))
                return true;

            for (var i = 0; i < _spillFiles.Count; i++)
            {
                if (SpillFileContains(_spillFiles[i], uri))
                    return true;
            }

            return false;
        }

        public void Add(string uri)
        {
            if (_entries.Add(uri) && _entries.Count >= _maxInMemoryEntries)
                FlushToDisk();
        }

        private void FlushToDisk()
        {
            Directory.CreateDirectory(SpillDirectory);

            var spillFile = Path.Combine(SpillDirectory, "spill-" + _spillFiles.Count.ToString("D6") + ".dat");
            using (var stream = new FileStream(spillFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                foreach (var entry in _entries)
                    writer.Write(entry);
            }

            _spillFiles.Add(spillFile);
            _entries = new HashSet<string>(StringComparer.Ordinal);
        }

        private static bool SpillFileContains(string spillFile, string uri)
        {
            using (var stream = new FileStream(spillFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                while (stream.Position < stream.Length)
                {
                    if (string.Equals(reader.ReadString(), uri, StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _entries = new HashSet<string>(StringComparer.Ordinal);
            _spillFiles.Clear();

            try
            {
                if (Directory.Exists(SpillDirectory))
                    Directory.Delete(SpillDirectory, true);
            }
            catch (IOException)
            {
                //Best effort cleanup of temp files, nothing more we can do
            }
            catch (UnauthorizedAccessException)
            {
                //Best effort cleanup of temp files, nothing more we can do
            }
        }
    }
}
