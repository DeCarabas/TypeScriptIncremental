﻿namespace TypeScript.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;
    using Microsoft.Build.Framework;

    /// <summary>
    /// Tracks dependencies between .ts files.
    /// </summary>
    public class DependencyCache
    {
        readonly Dictionary<string, DependencyRecord> cache;
        bool modified;

        /// <summary>
        /// Constructs a new instance of the <see cref="DependencyCache"/> class.
        /// </summary>
        public DependencyCache()
        {
            this.cache = new Dictionary<string, DependencyRecord>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Constructs a new instance of the <see cref="DependencyCache"/> class.
        /// </summary>
        /// <param name="reader">The reader containing the serialized form of the dependency cache.</param>
        public DependencyCache(TextReader reader)
        {
            int fileCount = Int32.Parse(reader.ReadLine());
            var data = new Dictionary<string, DependencyRecord>(fileCount, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fileCount; i++)
            {
                string fname = reader.ReadLine();
                DateTimeOffset lastScan = DateTimeOffset.Parse(reader.ReadLine());

                int depCount = Int32.Parse(reader.ReadLine());
                string[] deps = new string[depCount];
                for (int id = 0; id < depCount; id++)
                {
                    deps[id] = reader.ReadLine();
                }

                data.Add(fname, new DependencyRecord(lastScan, deps));
            }

            this.cache = data;
        }

        /// <summary>
        /// Gets the count of items currently tracked in the cache.
        /// </summary>
        public int Count
        {
            get { return this.cache.Count; }
        }

        /// <summary>
        /// Gets or sets a boolean value indicating whether or not the cache has changes that should be written to 
        /// disk.
        /// </summary>
        public bool IsModified
        {
            get { return this.modified; }
            set { this.modified = true; }
        }

        /// <summary>
        /// Gets the dependency record of the specified file.
        /// </summary>
        /// <param name="fullPath">The full path of the file whose dependency record is sought.</param>
        /// <returns>The dependency record. It's dependencies may or may not be up-to-date; this allows us to save 
        /// extra filesystem calls.</returns>
        public DependencyRecord GetDependencyRecord(string fullPath)
        {
            DependencyRecord record = null;
            if (!this.cache.TryGetValue(fullPath, out record))
            {
                record = new DependencyRecord();
                this.cache[fullPath] = record;
                this.modified = true;
            }

            return record;
        }

        /// <summary>
        /// Gets the "effective modified time" of the specified file, which is the last time this file or any of its
        /// dependencies has been modified.
        /// </summary>
        /// <param name="fullPath">The full path of the file whose effective modified time is sought.</param>
        /// <returns>The last time this file or any of its dependencies has been modified.</returns>
        public DateTime GetEffectiveModifiedTime(string fullPath)
        {
            DependencyRecord record = GetDependencyRecord(fullPath);
            if (record.EffectiveModifiedTime == null)
            {
                // Set it here to stop cycles...
                DateTime modifiedTime = File.GetLastWriteTimeUtc(fullPath);
                record.EffectiveModifiedTime = modifiedTime;

                if (record.Update(fullPath, modifiedTime)) { this.modified = true; }
                for (int i = 0; i < record.Dependencies.Count; i++)
                {
                    DateTime dependentTime = GetEffectiveModifiedTime(record.Dependencies[i]);
                    if (dependentTime > modifiedTime) { modifiedTime = dependentTime; }
                }

                // ...and here because it's right.
                record.EffectiveModifiedTime = modifiedTime;
            }

            return record.EffectiveModifiedTime.Value;
        }
        
        /// <summary>
        /// Writes the cache to disk.
        /// </summary>
        /// <param name="writer">The writer indicating where the cache should be saved.</param>
        public void Write(TextWriter writer)
        {
            writer.WriteLine(this.cache.Count);
            foreach (KeyValuePair<string, DependencyRecord> kvp in this.cache)
            {
                writer.WriteLine(kvp.Key);
                writer.WriteLine(kvp.Value.LastScanned.ToString("o"));
                writer.WriteLine(kvp.Value.Dependencies.Count);
                for (int i = 0; i < kvp.Value.Dependencies.Count; i++)
                {
                    writer.WriteLine(kvp.Value.Dependencies[i]);
                }
            }
        }
    }
}
