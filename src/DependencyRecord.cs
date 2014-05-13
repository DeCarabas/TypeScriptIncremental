/*
Copyright (C) 2014 John Doty

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/
namespace TypeScript.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Records the dependencies of a single file.
    /// </summary>
    public class DependencyRecord
    {
        static readonly Regex referenceTag = new Regex(
            @"^\s*///\s*<\s*reference\s*path\s*=\s*[""']([^""']+)[""']",
            RegexOptions.Multiline | RegexOptions.Compiled);

        readonly List<string> dependencies;

        /// <summary>
        /// Constructs a new instance of the <see cref="DependencyRecord"/> class.
        /// </summary>
        public DependencyRecord()
        {
            LastScanned = DateTime.MinValue;
            this.dependencies = new List<string>();
        }

        /// <summary>
        /// Constructs a new instance of the <see cref="DependencyRecord"/> class.
        /// </summary>
        /// <param name="lastScanned">The last time the file this record represents was scanned.</param>
        /// <param name="deps">The dependencies of the file this record represents.</param>
        public DependencyRecord(DateTimeOffset lastScanned, string[] deps)
        {
            LastScanned = lastScanned;
            this.dependencies = new List<string>(deps);
        }

        /// <summary>
        /// Gets the list of files that this file depends on.
        /// </summary>
        public IList<string> Dependencies { get { return this.dependencies; } }

        /// <summary>
        /// Gets or sets the date and time of the last scan of the file.
        /// </summary>
        public DateTimeOffset LastScanned { get; set; }

        /// <summary>
        /// Gets or sets the last time that this file or any of its dependencies was modified.
        /// </summary>
        public DateTime? EffectiveModifiedTime { get; set; }

        /// <summary>
        /// Scans the file specified by <paramref name="fullPath"/> and fills in the list of dependencies.
        /// </summary>
        /// <param name="fullPath">The path of the file to scan.</param>
        /// <param name="lastWriteTime">The last write time of the file, or null if unknown.</param>
        /// <returns>true if the dependencies were updated, otherwise false.</returns>
        /// <remarks>This could be both faster and more accurate if it didn't use regular expressions. Alas, it 
        /// would also not be *done*.</remarks>
        public bool Update(string fullPath, DateTimeOffset lastWriteTime)
        {
            if (LastScanned == lastWriteTime) { return false; }

            string content = File.ReadAllText(fullPath);
            string dir = Path.GetDirectoryName(fullPath);

            this.dependencies.Clear();
            foreach (Match match in referenceTag.Matches(content))
            {
                this.dependencies.Add(Path.GetFullPath(Path.Combine(dir, match.Groups[1].Value)));
            }

            LastScanned = lastWriteTime;
            return true;
        }
    }
}
